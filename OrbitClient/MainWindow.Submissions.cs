using Microsoft.Win32;
using OrbitClient.Scripts;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OrbitClient;

public partial class MainWindow
{
    private int submissionViewGeneration;
    private int submissionPage;

    private async void SubmitMod_Click(object sender, RoutedEventArgs e) => await OpenModSubmissionsAsync();

    private async Task OpenModSubmissionsAsync(bool resetPage = true, bool manage = false)
    {
        if (resetPage) submissionPage = 0;
        Nav_Click(ProfileNav, new RoutedEventArgs());
        if (orbitAccount?.Account is null)
        {
            orbitStatus.Text = "Sign in to your Orbit account to submit a mod.";
            return;
        }
        Accounts.Visibility = Visibility.Collapsed;
        FeaturePage.Visibility = Visibility.Visible;
        FeatureContent.Children.Clear();
        featurePage = manage ? "Your mods" : "Mod submissions";
        PageTitle.Text = featurePage;
        PageSubtitle.Text = manage ? "Manage approved listings and proposed revisions." : "Share a DLL with the Orbit review team.";
        var generation = ++submissionViewGeneration;
        bool Current() => !windowClosed && generation == submissionViewGeneration && FeaturePage.Visibility == Visibility.Visible;
        FeatureContent.Children.Add(FeatureText(manage ? "Manage your mods" : "Upload your mod", 28));
        var toolbar = new WrapPanel();
        toolbar.Children.Add(FeatureButton("Refresh", () => OpenModSubmissionsAsync(resetPage: false, manage: manage)));
        toolbar.Children.Add(FeatureButton("My profile", () => { Nav_Click(ProfileNav, new RoutedEventArgs()); return Task.CompletedTask; }));
        FeatureContent.Children.Add(toolbar);
        toolbar.Children.Add(FeatureButton(manage ? "Submit a new mod" : "Manage my mods",()=>OpenModSubmissionsAsync(manage: !manage)));
        featureStatus = FeatureText("", 12, true);
        FeatureContent.Children.Add(featureStatus);
        var placeholders = new StackPanel();
        FeatureContent.Children.Add(placeholders);
        ShowCatalogSkeletons(placeholders, 2);
        AnimateActivePage();
        var service = RequireOrbitAccount();
        try
        {
            var state = await service.GetModSubmissionStateAsync();
            if (!Current()) return;
            var items = await service.GetModSubmissionsAsync(reviewQueue: false, page: submissionPage, ownedMods: manage);
            if (!Current()) return;
            placeholders.Children.Clear();
            FeatureContent.Children.Remove(placeholders);
            featureStatus.Text = "";
            if (!manage) BuildSubmissionForm(state, service, generation);
            else FeatureContent.Children.Add(FeatureText("Edit listing details or release a new DLL. Every change goes to inspection; the current approved version stays live until its replacement is approved and published.",13,true));
            FeatureContent.Children.Add(FeatureText(manage ? "Your published listings" : "Your submissions", 21));
            if (items.Count == 0) FeatureContent.Children.Add(FeatureCard(new StackPanel { Children = { FeatureText(manage ? "No published mods yet. Check Mod submissions for files awaiting review." : "Your first submission starts here.", 16) } }));
            foreach (var item in items.Take(20)) BuildSubmissionCard(item, service, state);
            var pager = new WrapPanel();
            if (submissionPage > 0) pager.Children.Add(FeatureButton("← Previous", () => { submissionPage--; return OpenModSubmissionsAsync(resetPage: false, manage: manage); }));
            if (items.Count > 20) pager.Children.Add(FeatureButton("Next →", () => { submissionPage++; return OpenModSubmissionsAsync(resetPage: false, manage: manage); }));
            FeatureContent.Children.Add(pager);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            placeholders.Children.Clear();
            if (Current()) featureStatus.Text = ex is OperationCanceledException ? "The request timed out. Refresh to try again." : ex.Message;
        }
    }

    private void BuildSubmissionForm(ModSubmissionState state, OrbitAccountService service, int generation)
    {
        var panel = new StackPanel();
        panel.Children.Add(FeatureText("One DLL. One review.", 21));
        panel.Children.Add(FeatureText("One file per account per UTC day · Managed .NET DLL · Maximum 20 MB", 12, true));
        var used = state.Today is { Status: not "uploading" } or { IsRemoved: true };
        if (used)
        {
            panel.Children.Clear();
            panel.Children.Add(FeatureText(state.Today?.IsRemoved == true ? "Today's submission was removed" : "Today's DLL has been submitted", 20));
            panel.Children.Add(FeatureText($"Your next upload slot opens {state.NextResetAt:MMM d, HH:mm} UTC. You can still view your submissions below.", 12, true));
            FeatureContent.Children.Add(FeatureCard(panel));
            return;
        }
        if (state.Today is not null)
            panel.Children.Add(FeatureText($"Today's upload slot is reserved. Retry with the same DLL. Next slot: {state.NextResetAt:MMM d, HH:mm} UTC.", 12, true));
        var title = OrbitInput("Mod name", panel, state.Today?.Title ?? "", 80);
        var version=OrbitInput("Release version",panel,state.Today?.Version??"1.0.0",24);
        var category=OrbitInput("Category",panel,state.Today?.Category??"Community",40);
        var description = OrbitInput("What does your mod do?", panel, state.Today?.Description ?? "", 2000);
        description.AcceptsReturn = true; description.Height = 86; description.TextWrapping = TextWrapping.Wrap; description.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var notes=OrbitInput("Release notes",panel,state.Today?.ChangeNotes??"",2000);
        ModSubmissionFile? file = null;
        var details = FeatureText("Attach the DLL you want the team to review.", 12, true);
        details.Name = "SubmissionFileDetails";
        var controls = new WrapPanel();
        var choose = FeatureButton("Attach .dll", async () =>
        {
            var picker = new OpenFileDialog { Title = "Choose your mod DLL", Filter = "Mod DLL|*.dll", Multiselect = false, CheckFileExists = true };
            if (picker.ShowDialog(this) != true) return;
            var selected = await ModSubmissionFile.ReadAsync(picker.FileName);
            if (generation != submissionViewGeneration) return;
            file = selected;
            details.Text = $"{file.Name} · {file.Bytes.Length / 1048576d:0.00} MB\nSHA-256: {file.Sha256}";
        });
        controls.Children.Add(choose);
        Button? send = null;
        send = FeatureButton("Submit for review", async () =>
        {
            if (file is null) throw new IOException("Attach your mod DLL first.");
            choose.IsEnabled = false;
            title.IsEnabled = false; description.IsEnabled = false;
            version.IsEnabled=false;category.IsEnabled=false;notes.IsEnabled=false;
            try
            {
                SetFeatureStatus("Uploading your DLL to the private review queue…");
                ModSubmission submitted;
                if(state.Today?.BaseRevisionId is not null)
                {
                    var comparison=await service.GetModRevisionComparisonAsync(state.Today.Id);
                    submitted=await service.ProposeModRevisionAsync(comparison.Previous??throw new IOException("Refresh the mod before resuming this release."),title.Text,description.Text,version.Text,category.Text,notes.Text,file);
                }
                else submitted = await service.SubmitModAsync(title.Text, description.Text, file,version.Text,category.Text,notes.Text);
                if (generation != submissionViewGeneration) return;
                await OpenModSubmissionsAsync();
                SetFeatureStatus(submitted.Status == "pending" ? "Submitted. Your file is waiting for review." : "This file has already been submitted. See its status below.");
            }
            finally
            {
                choose.IsEnabled = true; title.IsEnabled = true; description.IsEnabled = true;
                version.IsEnabled=true;category.IsEnabled=true;notes.IsEnabled=true;
            }
        }, true);
        controls.Children.Add(send);
        panel.Children.Add(controls); panel.Children.Add(details);
        FeatureContent.Children.Add(FeatureCard(panel));
    }

    private void BuildSubmissionCard(ModSubmission item, OrbitAccountService service, ModSubmissionState? state = null)
    {
        var body = new StackPanel();
        var header = new DockPanel();
        var status = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 5, 6, 5), Background = (Brush)FindResource("AccentSoft"), HorizontalAlignment = HorizontalAlignment.Right };
        status.Padding = new Thickness(0); status.Background = Brushes.Transparent;
        status.Child = FeatureText(item.IsRemoved ? "Removed" : item.Status switch { "pending" => "Awaiting review", "uploading" => "Upload incomplete", "approved" => item.PublishedAt is null ? "Approved · not published" : item.IsCurrent ? "Published in Discover" : "Previous approved revision", _ => "Rejected" }, 11);
        ((TextBlock)status.Child).Margin = new Thickness(0); DockPanel.SetDock(status, Dock.Right); header.Children.Add(status); header.Children.Add(FeatureText(item.Title, 19)); body.Children.Add(header);
        body.Children.Add(FeatureText($"{item.FileName} · {item.ByteSize / 1048576d:0.00} MB · {item.CreatedAt.UtcDateTime:MMM d, yyyy HH:mm} UTC", 12, true));
        body.Children.Add(FeatureText($"v{item.Version} · {item.Category} · Revision {item.RevisionNumber}"+(item.ChangeType=="details"?" · Details-only edit; DLL unchanged":" · DLL release"),12,true));
        if (!string.IsNullOrWhiteSpace(item.Description)) body.Children.Add(FeatureText(item.Description, 13, true));
        if (!string.IsNullOrWhiteSpace(item.ChangeNotes)) body.Children.Add(FeatureText("Change notes: "+item.ChangeNotes,12,true));
        if(item.IsCurrent && item.UserId.ToString("D")==service.Account?.Id)
        {
            if(item.OpenRevisionId is not null) body.Children.Add(FeatureText("A change to this mod is awaiting inspection or publication. Its current approved version is still live.",12,true));
            else
            {
                var editing=new WrapPanel();
                editing.Children.Add(FeatureButton("Edit details",()=>{OpenModRevisionEditor(item,false,state);return Task.CompletedTask;}));
                editing.Children.Add(FeatureButton("Release update",()=>{OpenModRevisionEditor(item,true,state);return Task.CompletedTask;}));
                body.Children.Add(editing);
            }
        }
        if (item.ReviewNotes.Length > 0) body.Children.Add(FeatureText("Review notes: " + item.ReviewNotes, 12, true));
        if (!item.IsRemoved && item.UserId.ToString("D") == service.Account?.Id)
            body.Children.Add(FeatureButton("Remove mod", async () =>
            {
                if (MessageBox.Show(this, $"Remove {item.Title} from Discover and withdraw its pending revisions?\n\nInstalled copies stay on users' computers. Submission history and today's DLL allowance are retained.",
                    "Remove your mod", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                await service.RemoveOwnedModAsync(item.ModId);
                discoverMods.RemoveAll(mod => mod.id == "submission-" + item.ModId.ToString("D"));
                BuildDiscoverFilters(); ApplyDiscoverFilters(false);
                await OpenModSubmissionsAsync();
                SetFeatureStatus("Mod removed from Discover. Its submission history is retained.");
            }));
        FeatureContent.Children.Add(FeatureCard(body));
    }

    private void OpenModRevisionEditor(ModSubmission current,bool release,ModSubmissionState? state)
    {
        Nav_Click(ProfileNav,new RoutedEventArgs());
        Accounts.Visibility=Visibility.Collapsed;FeaturePage.Visibility=Visibility.Visible;
        FeatureContent.Children.Clear();featurePage=release?"Release update":"Edit mod details";PageTitle.Text=featurePage;
        var generation=++submissionViewGeneration;
        FeatureContent.Children.Add(FeatureText(featurePage,28));
        FeatureContent.Children.Add(FeatureText("Every change needs inspection. The published listing and DLL stay unchanged until this revision is approved and published.",13,true));
        FeatureContent.Children.Add(FeatureButton("← Your mods",()=>OpenModSubmissionsAsync(manage: true)));
        featureStatus=FeatureText("",12,true);FeatureContent.Children.Add(featureStatus);
        var panel=new StackPanel();
        var title=OrbitInput("Mod name",panel,current.Title,80);
        var version=OrbitInput("Version",panel,release&&Version.TryParse(current.Version,out var parsed)?$"{parsed.Major}.{parsed.Minor}.{Math.Max(0,parsed.Build)+1}":current.Version,24);
        version.IsReadOnly=!release;
        var category=OrbitInput("Category",panel,current.Category,40);
        var description=OrbitInput("Description",panel,current.Description,2000);description.AcceptsReturn=true;description.Height=100;description.TextWrapping=TextWrapping.Wrap;
        var notes=OrbitInput("What changed?",panel,"",2000);notes.AcceptsReturn=true;notes.Height=64;notes.TextWrapping=TextWrapping.Wrap;
        ModSubmissionFile? file=null;
        if(release)
        {
            var fileLabel=FeatureText("Attach the DLL for this new version.",12,true);
            panel.Children.Add(FeatureButton("Attach updated .dll",async ()=>
            {
                var picker=new OpenFileDialog {Title="Choose updated mod DLL",Filter="Mod DLL|*.dll",Multiselect=false,CheckFileExists=true};
                if(picker.ShowDialog(this)!=true)return;
                var selected=await ModSubmissionFile.ReadAsync(picker.FileName);
                if(generation!=submissionViewGeneration)return;
                file=selected;fileLabel.Text=$"{file.Name} · {file.Bytes.Length/1048576d:0.00} MB\nSHA-256: {file.Sha256}";
            }));
            panel.Children.Add(fileLabel);
        }
        else panel.Children.Add(FeatureText("This changes listing details only and reuses the approved DLL. It does not use your daily DLL allowance.",12,true));
        var submit=FeatureButton("Send changes for inspection",async ()=>
        {
            if(release&&file is null)throw new IOException("Attach the updated DLL first.");
            panel.IsEnabled=false;
            try
            {
                SetFeatureStatus("Sending revision for inspection…");
                await RequireOrbitAccount().ProposeModRevisionAsync(current,title.Text,description.Text,version.Text,category.Text,notes.Text,file);
                if(generation==submissionViewGeneration)
                {
                    await OpenModSubmissionsAsync();SetFeatureStatus("Revision submitted for inspection. Your current version is still live.");
                }
            }
            finally{panel.IsEnabled=true;}
        },true);
        panel.Children.Add(submit);
        if(release&&state?.Today is not null)
        {
            submit.IsEnabled=false;
            panel.Children.Add(FeatureText($"Today's DLL allowance is already used. You can release a new DLL after {state.NextResetAt:MMM d, HH:mm} UTC; details-only edits remain available.",12,true));
        }
        FeatureContent.Children.Add(FeatureCard(panel));AnimateActivePage();
    }
}
