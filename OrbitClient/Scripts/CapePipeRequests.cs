using OrbitClient.Capes;
using System.IO;
using System.Text.Json;

namespace OrbitClient.Scripts;

/// <summary>Parses only requests; clients never submit ownership or image grants.</summary>
public static class CapePipeRequests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static CapeRequest Selection(string json)
    {
        using var document = Read(json, 1024, "Id", "RequestId");
        var request = document.RootElement.Deserialize<CapeRequest>(Options) ?? throw new IOException("Invalid cape request.");
        if (!new CapeDefinition { Id = request.Id }.HasValidId() || !Guid.TryParseExact(request.RequestId, "N", out _))
            throw new IOException("Invalid cape request.");
        return request;
    }

    public static CapePresenceRequest Presence(string json, bool remote)
    {
        using var document = Read(json, 2048, "Context", "Actor", "Token", "Revision");
        var request = document.RootElement.Deserialize<CapePresenceRequest>(Options) ?? throw new IOException("Invalid cape presence request.");
        if (!request.HasValidBinding() || (request.Revision?.Length ?? 0) > 512
            || (remote ? !Guid.TryParseExact(request.Token, "D", out _) : !string.IsNullOrEmpty(request.Token) || !string.IsNullOrEmpty(request.Revision)))
            throw new IOException("Invalid cape presence request.");
        return request;
    }

    public static void Empty(string data)
    {
        if (!string.IsNullOrEmpty(data)) throw new IOException("This cape command cannot include data.");
    }

    private static JsonDocument Read(string json, int maximum, params string[] allowed)
    {
        if (json.Length > maximum) throw new IOException("Cape request too large.");
        var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new IOException("Invalid cape request.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!allowed.Contains(property.Name, StringComparer.OrdinalIgnoreCase) || !names.Add(property.Name))
                    throw new IOException("Unrecognized or duplicate cape request field.");
            return document;
        }
        catch { document.Dispose(); throw; }
    }
}
