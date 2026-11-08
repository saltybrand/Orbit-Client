using System;
using System.Globalization;

namespace OrbitClient.DataModels;

[Serializable]
public class NewsData
{
    public string title { get; set; } = "Orbit News";
    public string description { get; set; } = "";
    public string details { get; set; } = "";
    public string logo { get; set; } = "";
    public string image { get; set; } = "";
    public string date { get; set; } = "";
    public string category { get; set; } = "Updates";
    public int placement { get; set; }
    public DateTime PublishedDate => DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : DateTime.MinValue;
    public string DisplayDate => PublishedDate == DateTime.MinValue ? "Date not provided" : PublishedDate.ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
}
