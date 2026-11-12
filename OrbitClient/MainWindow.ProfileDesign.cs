using OrbitClient.Scripts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitClient;

public partial class MainWindow
{
    private TextBlock ProfileLabel(string text, double size = 13, bool muted = false)
    {
        var label = FeatureText(text, size, muted);
        label.Margin = new Thickness(0, 0, 0, 6);
        return label;
    }

    private Border ProfilePanel(FrameworkElement content, Thickness? margin = null) => new()
    {
        Style = (Style)FindResource("Card"), Padding = new Thickness(14), Child = content,
        Margin = margin ?? new Thickness(0, 0, 0, 10)
    };

    // The same layout stacks its cards when text scaling or a narrow window needs more room.
    private Grid ProfileColumns(FrameworkElement left, FrameworkElement right, double leftWeight = 1.65)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftWeight, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(left); grid.Children.Add(right);
        void Layout()
        {
            var stack = grid.ActualWidth < 680;
            Grid.SetColumn(right, stack ? 0 : 1);
            Grid.SetRow(right, stack ? 1 : 0);
            Grid.SetColumnSpan(left, stack ? 2 : 1);
            Grid.SetColumnSpan(right, stack ? 2 : 1);
            left.Margin = new Thickness(0, 0, stack ? 0 : 16, 0);
        }
        grid.SizeChanged += (_, _) => Layout();
        Layout();
        return grid;
    }

    private void ComposeProfilePage(StackPanel content)
    {
        OrbitAccountHost.MaxWidth = 1160;
        if (orbitAccount?.Account is not null)
        {
            var title = ProfileLabel("Your profile", 26); title.FontWeight = FontWeights.Bold;
            OrbitAccountHost.Children.Add(title);
            OrbitAccountHost.Children.Add(ProfileLabel("Update your profile and manage your account.", 13, true));
            OrbitAccountHost.Children.Add(content);
            return;
        }

        var intro = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        intro.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/OrbitClient;component/Assets/Orbit-removebg-preview.png")), Width = 76, Height = 76, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) });
        var headline = ProfileLabel("Your Orbit account", 28); headline.FontWeight = FontWeights.Bold; intro.Children.Add(headline);
        intro.Children.Add(ProfileLabel("Save your capes and manage your submitted mods.", 15, true));
        foreach (var (icon, title, detail) in new[] { ("person", "Make it personal", "Choose your username, picture, and bio."), ("palette", "Keep your collection", "Find your capes in your dressing room."), ("health_and_safety", "Stay in control", "Manage your profile and account sessions.") })
        {
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var symbol = MaterialSymbols.Icon(icon, 21); ((System.Windows.Shapes.Path)symbol).SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentLight"); row.Children.Add(symbol);
            var words = new StackPanel(); var name = ProfileLabel(title, 13); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0, 0, 0, 3); words.Children.Add(name); words.Children.Add(ProfileLabel(detail, 11, true)); Grid.SetColumn(words, 1); row.Children.Add(words); intro.Children.Add(row);
        }
        var introduction = ProfilePanel(intro);
        introduction.Padding = new Thickness(18, 20, 18, 20);
        var auth = new StackPanel();
        var modes = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        modes.ColumnDefinitions.Add(new ColumnDefinition()); modes.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var register in new[] { false, true })
        {
            var mode = OrbitButton(register ? "Create account" : "Sign in", () => { orbitRegister = register; BuildOrbitAccount(); return Task.CompletedTask; }, orbitRegister == register);
            mode.Height = 36; mode.Margin = new Thickness(register ? 4 : 0, 0, register ? 0 : 4, 0);
            mode.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(IsEnabled)) { Source = orbitForm });
            Grid.SetColumn(mode, register ? 1 : 0); modes.Children.Add(mode);
        }
        auth.Children.Add(modes);
        var heading = ProfileLabel(orbitRegister ? "Make yourself at home." : "Welcome back.", 24); heading.FontWeight = FontWeights.SemiBold; auth.Children.Add(heading);
        auth.Children.Add(ProfileLabel(orbitRegister ? "Create your Orbit account to get started." : "Sign in to pick up where you left off.", 12, true));
        auth.Children.Add(content);
        OrbitAccountHost.Children.Add(ProfileColumns(introduction, ProfilePanel(auth), .95));
    }

    private void BuildSignedInProfile(OrbitAccount account)
    {
        var hero = new Grid { MinHeight = 88 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) }); hero.ColumnDefinitions.Add(new ColumnDefinition());
        var avatar = ProfileAvatar(64); avatar.VerticalAlignment = VerticalAlignment.Center; avatar.HorizontalAlignment = HorizontalAlignment.Left; hero.Children.Add(avatar);
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var liveName = ProfileLabel(ProfileName(), 24); liveName.FontWeight = FontWeights.Bold; liveName.Name = "ProfilePreviewName"; identity.Children.Add(liveName);
        var liveBio = ProfileLabel(string.IsNullOrWhiteSpace(orbitProfile?.Bio) ? "A fresh profile, ready to make your own." : orbitProfile.Bio, 12, true); liveBio.MaxHeight = 46; liveBio.TextTrimming = TextTrimming.CharacterEllipsis; identity.Children.Add(liveBio);
        Grid.SetColumn(identity, 1); hero.Children.Add(identity);
        var banner = ProfilePanel(hero); orbitForm.Children.Add(banner);

        var editor = new StackPanel();
        var editTitle = ProfileLabel("Edit your profile", 21); editTitle.FontWeight = FontWeights.SemiBold; editor.Children.Add(editTitle);
        editor.Children.Add(ProfileLabel("Update your username, picture, and bio.", 12, true));
        var pictureActions = new WrapPanel { Margin = new Thickness(0, 6, 0, 8) };
        pictureActions.Children.Add(OrbitButton("Change picture", ChangeOrbitPictureAsync));
        pictureActions.Children.Add(OrbitButton("Use default picture", async () => { await RequireOrbitAccount().RemoveAvatarAsync(); orbitAvatar = null; await LoadOrbitAvatarAsync(); BuildOrbitAccount("Profile picture reset."); }));
        editor.Children.Add(pictureActions);
        var username = OrbitInput("Username", editor, orbitDraftUsername ?? orbitProfile?.Username ?? "", 24); username.Name = "ProfileUsername";
        editor.Children.Add(ProfileLabel("3–24 letters, numbers, or underscores. Shown in lowercase.", 11, true));
        var bio = OrbitInput("About you", editor, orbitDraftBio ?? orbitProfile?.Bio ?? "", 500); bio.Name = "ProfileBio"; bio.AcceptsReturn = true; bio.Height = 76; bio.TextWrapping = TextWrapping.Wrap; bio.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var draftStatus = ProfileLabel("", 11, true); draftStatus.Name = "ProfileDraftStatus"; editor.Children.Add(draftStatus);
        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var save = OrbitButton(orbitProfile is null ? "Create profile" : "Save changes", async () =>
        {
            orbitProfile = await RequireOrbitAccount().SaveProfileAsync(username.Text.Trim().ToLowerInvariant(), bio.Text, orbitProfile is null);
            orbitDraftUsername = null; orbitDraftBio = null; BuildOrbitAccount("Your profile is up to date.");
        }, true);
        save.Name = "ProfileSave"; actions.Children.Add(save);
        actions.Children.Add(OrbitButton("Reset changes", () => { username.Text = orbitProfile?.Username ?? ""; bio.Text = orbitProfile?.Bio ?? ""; orbitStatus.Text = "Changes reset."; return Task.CompletedTask; }));
        editor.Children.Add(actions);
        void UpdateDraft()
        {
            orbitDraftUsername = username.Text; orbitDraftBio = bio.Text;
            var normalized = username.Text.Trim().ToLowerInvariant();
            var valid = System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[a-z0-9_]{3,24}$");
            var changed = orbitProfile is null || normalized != orbitProfile.Username || bio.Text != orbitProfile.Bio;
            save.IsEnabled = valid && changed;
            draftStatus.Text = !valid ? "Choose a valid username to save your profile." : $"{bio.Text.Length}/500 characters  ·  " + (changed ? "Unsaved changes" : "All changes saved");
            liveName.Text = valid ? "@" + normalized : ProfileName();
            liveBio.Text = string.IsNullOrWhiteSpace(bio.Text) ? "A fresh profile, ready to make your own." : bio.Text;
        }
        username.TextChanged += (_, _) => UpdateDraft(); bio.TextChanged += (_, _) => UpdateDraft(); UpdateDraft();

        var aside = new StackPanel();
        var details = new StackPanel(); var detailsTitle = ProfileLabel("Account", 18); detailsTitle.FontWeight = FontWeights.SemiBold; details.Children.Add(detailsTitle);
        details.Children.Add(ProfileLabel("Email address", 10, true)); details.Children.Add(ProfileLabel(account.Email, 13)); details.Children.Add(ProfileLabel("Used to sign in to your account.", 11, true));
        details.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(53, 39, 63)), Margin = new Thickness(0, 6, 0, 10) });
        details.Children.Add(ProfileLabel("Signed in", 12));
        details.Children.Add(OrbitButton("Reload profile", async () => { if (!ConfirmDiscardProfile()) return; await LoadOrbitProfileAsync(); orbitDraftUsername = null; orbitDraftBio = null; BuildOrbitAccount("Profile loaded."); }));
        aside.Children.Add(ProfilePanel(details));
        var submissions = new StackPanel();
        var submissionsTitle = ProfileLabel("Share your mods", 18); submissionsTitle.FontWeight = FontWeights.SemiBold; submissions.Children.Add(submissionsTitle);
        submissions.Children.Add(ProfileLabel("Submit one DLL per UTC day and follow its review status.", 12, true));
        submissions.Children.Add(OrbitButton("Mod submissions", () => OpenModSubmissionsAsync()));
        aside.Children.Add(ProfilePanel(submissions));
        var collection = new StackPanel(); var collectionTitle = ProfileLabel("Your next look", 18); collectionTitle.FontWeight = FontWeights.SemiBold; collection.Children.Add(collectionTitle); collection.Children.Add(ProfileLabel("Browse capes and manage your collection.", 12, true));
        collection.Children.Add(OrbitButton("Open dressing room  →", () => { var nav = NavPanel.Children.OfType<Button>().Single(button => Equals(button.Tag, "Capes")); Nav_Click(nav, new RoutedEventArgs()); return Task.CompletedTask; })); aside.Children.Add(ProfilePanel(collection));
        var session = new StackPanel(); var sessionTitle = ProfileLabel("Sessions", 18); sessionTitle.FontWeight = FontWeights.SemiBold; session.Children.Add(sessionTitle); session.Children.Add(ProfileLabel("Decide where you stay signed in.", 12, true));
        session.Children.Add(OrbitButton("Sign out", async () => { if (ConfirmDiscardProfile()) await SignOutOrbit(false); }));
        session.Children.Add(OrbitButton("Sign out all devices", async () => { if (MessageBox.Show(this, "Sign out of Orbit on every device?", "Sign out", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes && ConfirmDiscardProfile()) await SignOutOrbit(true); }));
        aside.Children.Add(ProfilePanel(session));
        orbitForm.Children.Add(ProfileColumns(ProfilePanel(editor), aside));
    }
}
