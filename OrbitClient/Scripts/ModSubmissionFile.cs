using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace OrbitClient.Scripts;

public sealed record ModSubmissionFile(string Name, byte[] Bytes, string Sha256)
{
    public const int MaximumBytes = 20 * 1024 * 1024;
    public static bool ValidName(string name) => Regex.IsMatch(name, @"^[a-zA-Z0-9][a-zA-Z0-9._ -]{0,119}\.dll$", RegexOptions.IgnoreCase)
        && !name.Contains("..", StringComparison.Ordinal);

    public static ModSubmissionFile FromBytes(string name, byte[] bytes)
    {
        if (!ValidName(name)) throw new IOException("Choose a .dll with a simple filename: letters, numbers, spaces, dots, underscores, or hyphens.");
        if (bytes.Length is < 1 or > MaximumBytes) throw new IOException("Choose a DLL under 20 MB.");
        try
        {
            using var reader = new PEReader(new MemoryStream(bytes, writable: false));
            if (!reader.PEHeaders.IsDll || !reader.HasMetadata) throw new BadImageFormatException();
            if (!System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader).IsAssembly) throw new BadImageFormatException();
            // Only read PE metadata. Never load the submitted assembly into the process.
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        { throw new IOException("Choose a valid managed .NET mod DLL."); }
        return new(name, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    public static async Task<ModSubmissionFile> ReadAsync(string path, CancellationToken token = default)
    {
        if (!ValidName(Path.GetFileName(path))) throw new IOException("Choose a .dll with a simple filename.");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, useAsync: true);
        if (file.Length is < 1 or > MaximumBytes) throw new IOException("Choose a DLL under 20 MB.");
        using var response = new System.Net.Http.HttpResponseMessage { Content = new System.Net.Http.StreamContent(file) };
        return FromBytes(Path.GetFileName(path), await OrbitHttp.ReadBoundedAsync(response, MaximumBytes, token).ConfigureAwait(false));
    }
}
