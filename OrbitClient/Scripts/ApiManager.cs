using System.IO;
using System.Text.Json;

namespace OrbitClient.Scripts;

internal sealed class ApiManager : IDisposable
{
    private readonly OrbitContentService content;
    public static bool isAlive;
    public ApiManager(OrbitAccountConfig config, string dataRoot) => content = new(config, dataRoot);
    public async Task ConnectToApi()
    {
        isAlive = false;
        isAlive = (await content.ReadAsync<JsonElement>("status", false)).Value.GetProperty("online").GetBoolean();
    }
    public async Task<string> GetJson(string path)
    {
        var key = path.Trim('/').ToLowerInvariant() switch
        {
            "news.json" => "news",
            "mods.json" => "mods",
            "update.json" => "updates",
            "status" or "isalive" => "status",
            "capes.json" => "capes",
            _ => throw new IOException("Unknown Orbit document.")
        };
        return (await content.ReadAsync<JsonElement>(key)).Value.GetRawText();
    }
    public void Dispose() => content.Dispose();
}
