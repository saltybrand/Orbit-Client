using System.Text.Json;
using OrbitClient.Appearance;

namespace OrbitClient;

public partial class MainWindow
{
    private string lastSentCompanionTheme = "";

    private void SendCompanionTheme(bool force = false)
    {
        if (!pipeServer.IsConnected) return;
        var theme = new ThemeMessage
        {
            Accent = preferences.Accent.ToUpperInvariant(),
            Background = Array.IndexOf(ThemeMessage.Backgrounds, preferences.Background) >= 0 ? preferences.Background : "Midnight"
        };
        if (!theme.IsValid()) return;
        var data = JsonSerializer.Serialize(theme);
        if (!force && data == lastSentCompanionTheme) return;
        lastSentCompanionTheme = data;
        _ = pipeServer.SendAsync("theme_update", data);
    }

    private void ReceiveCompanionTheme(string data)
    {
        ThemeMessage? theme;
        try { theme = JsonSerializer.Deserialize<ThemeMessage>(data); }
        catch (JsonException) { return; }
        if (theme == null || !theme.IsValid()) return;
        preferences.Accent = theme.Accent.ToUpperInvariant();
        preferences.Background = theme.Background;
        ApplyAppearance();
        SaveFeaturePreferences();
        if (featurePage == "Appearance") OpenFeaturePage("Appearance");
    }
}
