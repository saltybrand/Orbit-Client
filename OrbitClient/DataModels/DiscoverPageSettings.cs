namespace OrbitClient.DataModels;

public sealed class DiscoverPageSettings
{
    public string title { get; set; } = "Discover mods";
    public string subtitle { get; set; } = "Find something new for your next session.";
    public string searchPlaceholder { get; set; } = "Search mods, descriptions, or authors…";
    public string categoryLabel { get; set; } = "Category";
    public string authorLabel { get; set; } = "Author";
    public string emptyTitle { get; set; } = "No matching mods";
    public string emptyDescription { get; set; } = "Try another search or clear your filters.";
    public string submitLabel { get; set; } = "Submit a mod";
    public string installLabel { get; set; } = "Install ↓";
    public string clearLabel { get; set; } = "Clear filters";
    public string refreshLabel { get; set; } = "Refresh ↻";
    public bool showCategories { get; set; } = true;
    public bool showAuthors { get; set; } = true;
    public bool showSubmit { get; set; } = true;
    public double sidebarWidth { get; set; } = 180;
    public double contentWidth { get; set; } = 1250;
    public double cardImageSize { get; set; } = 74;
    public string categories { get; set; } = "";
    public string authors { get; set; } = "";
    public string sortFeatured { get; set; } = "Sort: Featured ↕";
    public string sortNameAscending { get; set; } = "Sort: Name A–Z ↕";
    public string sortNameDescending { get; set; } = "Sort: Name Z–A ↕";
    public string sortAuthor { get; set; } = "Sort: Author ↕";
    public string viewLabel { get; set; } = "View:";
    public string pageFormat { get; set; } = "Page {page} of {pages}";
    public string resultsFormat { get; set; } = "{count} of {total} mods";
    public string previousLabel { get; set; } = "←";
    public string nextLabel { get; set; } = "→";
}
