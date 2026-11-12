using System.Windows;
using System.Windows.Controls;
using OrbitClient.DataModels;
using OrbitClient.Scripts;

namespace OrbitClient;

public partial class MainWindow
{
    private DiscoverPageSettings discoverSettings = new();

    private async Task LoadDiscoverPageSettings()
    {
        try
        {
            var result = await orbitContent.ReadAsync<List<DiscoverPageSettings>>("discover");
            discoverSettings = result.Value.FirstOrDefault() ?? new();
        }
        catch (Exception ex) { ErrorManager.LogError("Discover page settings: " + ex.Message); }
        var defaults = new DiscoverPageSettings();
        // A malformed remote value must not break the catalog.
        foreach (var property in typeof(DiscoverPageSettings).GetProperties().Where(p => p.PropertyType == typeof(string)))
            if (property.GetValue(discoverSettings) is not string text || string.IsNullOrWhiteSpace(text) && property.Name is not ("categories" or "authors"))
                property.SetValue(discoverSettings, property.GetValue(defaults));
        discoverSettings.sidebarWidth = double.IsFinite(discoverSettings.sidebarWidth) ? Math.Clamp(discoverSettings.sidebarWidth, 140, 300) : 180;
        discoverSettings.contentWidth = double.IsFinite(discoverSettings.contentWidth) ? Math.Clamp(discoverSettings.contentWidth, 800, 1800) : 1250;
        discoverSettings.cardImageSize = double.IsFinite(discoverSettings.cardImageSize) ? Math.Clamp(discoverSettings.cardImageSize, 48, 140) : 74;
        DiscoverHeading.Text = TextRhythm.Heading(discoverSettings.title);
        DiscoverDescription.Text = discoverSettings.subtitle;
        DiscoverPlaceholder.Text = discoverSettings.searchPlaceholder;
        DiscoverCategoryHeading.Text = discoverSettings.categoryLabel;
        DiscoverAuthorHeading.Text = discoverSettings.authorLabel;
        DiscoverEmptyHeading.Text = discoverSettings.emptyTitle;
        DiscoverEmptyDescription.Text = discoverSettings.emptyDescription;
        DiscoverSubmitButton.Content = discoverSettings.submitLabel;
        DiscoverSubmitButton.Visibility = discoverSettings.showSubmit ? Visibility.Visible : Visibility.Collapsed;
        DiscoverClearButton.Content = discoverSettings.clearLabel;
        MaterialSymbols.Label(DiscoverRefreshButton, discoverSettings.refreshLabel.TrimEnd(' ', '↻'), "refresh");
        MaterialSymbols.Label(DiscoverClearButton, discoverSettings.clearLabel, "restart_alt");
        DiscoverSidebarColumn.Width = new GridLength(Math.Clamp(discoverSettings.sidebarWidth, 140, 160));
        DiscoverContent.MaxWidth = discoverSettings.contentWidth;
        UpdateDiscoverChoices();
        DiscoverPreviousButton.Content = discoverSettings.previousLabel == "←" ? "Previous" : discoverSettings.previousLabel;
        DiscoverNextButton.Content = discoverSettings.nextLabel == "→" ? "Next" : discoverSettings.nextLabel;
    }

    private bool updatingDiscoverChoices;
    private void UpdateDiscoverChoices()
    {
        if (DiscoverSortBox is null || DiscoverPageSizeBox is null) return;
        updatingDiscoverChoices = true;
        try
        {
            DiscoverSortBox.Items.Clear();
            foreach (var text in DiscoverSortLabels())
            {
                var label = text.TrimEnd(' ', '↕');
                DiscoverSortBox.Items.Add(label.StartsWith("Sort: ", StringComparison.Ordinal) ? label[6..] : label);
            }
            DiscoverSortBox.SelectedIndex = discoverSort;
            DiscoverPageSizeBox.Items.Clear();
            foreach (var size in new[] { 8, 16, 24 }) DiscoverPageSizeBox.Items.Add($"{size} per page");
            DiscoverPageSizeBox.SelectedIndex = Array.IndexOf(new[] { 8, 16, 24 }, discoverPageSize);
        }
        finally { updatingDiscoverChoices = false; }
    }
    private void DiscoverSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingDiscoverChoices || DiscoverSortBox.SelectedIndex < 0) return;
        discoverSort = DiscoverSortBox.SelectedIndex;
        ApplyDiscoverFilters();
    }
    private void DiscoverPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingDiscoverChoices || DiscoverPageSizeBox.SelectedIndex < 0) return;
        preferences.DiscoverPageSize = discoverPageSize = new[] { 8, 16, 24 }[DiscoverPageSizeBox.SelectedIndex];
        ApplyDiscoverFilters();
        SaveFeaturePreferences();
    }

    private string[] DiscoverSortLabels() => [discoverSettings.sortFeatured, discoverSettings.sortNameAscending, discoverSettings.sortNameDescending, discoverSettings.sortAuthor];

    private static List<string> OrderDiscoverFilters(List<string> values, string order)
    {
        var preferred = order.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return values.OrderBy(value => { var index = preferred.FindIndex(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)); return index < 0 ? int.MaxValue : index; }).ToList();
    }
}
