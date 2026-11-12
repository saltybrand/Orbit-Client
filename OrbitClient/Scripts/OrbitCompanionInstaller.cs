using System.IO;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OrbitClient.Scripts;

/// <summary>Installs the published companion without loading downloaded code.</summary>
public sealed class OrbitCompanionInstaller
{
    public const string DownloadSource = "orbit://assets/Mods/OrbitClient.dll";
    private const string PluginGuid = "com.Meep.gorillatag.OrbitClient";

    /// <summary>Uses the newer validated build from the release and asset host.</summary>
    public static async Task<byte[]> DownloadLatestAsync(Func<Task<byte[]>> download, byte[]? bundled = null)
    {
        if (bundled == null)
        {
            using var resource = typeof(OrbitCompanionInstaller).Assembly.GetManifestResourceStream("OrbitClient.BundledCompanion.dll");
            if (resource != null)
            {
                using var buffer = new MemoryStream();
                await resource.CopyToAsync(buffer).ConfigureAwait(false);
                bundled = buffer.ToArray();
            }
        }
        var bundledVersion = bundled == null ? null : ValidateCompanion(bundled);
        try
        {
            var published = await download().ConfigureAwait(false);
            var publishedVersion = ValidateCompanion(published);
            return bundledVersion != null && bundledVersion > publishedVersion ? bundled! : published;
        }
        catch (Exception ex) when (bundled != null && ex is IOException or HttpRequestException or OperationCanceledException)
        {
            Console.WriteLine("[Orbit Companion] Using the bundled companion: " + ex.Message);
            return bundled!;
        }
    }

    public async Task<bool> InstallAsync(string gamePath, Func<Task<byte[]>> download, Action? beforeWrite = null)
    {
        var root = WorkspaceService.VerifyGame(gamePath);
        WorkspaceService.RequireGameClosed();
        if (!BepInExInstaller.IsInstalled(root))
            throw new IOException("Install BepInEx in Settings before Orbit can install its companion automatically.");
        var destination = FindDestination(root);
        var bytes = await download().ConfigureAwait(false);
        var incomingVersion = ValidateCompanion(bytes);
        WorkspaceService.RequireGameClosed();
        WorkspaceService.EnsureNoLinks(destination);
        if (File.Exists(destination))
        {
            var installed = await File.ReadAllBytesAsync(destination).ConfigureAwait(false);
            if (bytes.AsSpan().SequenceEqual(installed)) return false;
            Version? installedVersion = null;
            try { installedVersion = ValidateCompanion(installed); }
            catch (IOException) { /* A damaged installation can be repaired. */ }
            if (installedVersion != null && installedVersion > incomingVersion) return false;
        }
        beforeWrite?.Invoke();
        WorkspaceService.RequireGameClosed();
        WorkspaceService.EnsureNoLinks(destination);
        // Recheck after download/snapshot so a new competing copy is not created.
        if (!string.Equals(destination, FindDestination(root), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The companion installation changed. Restart Orbit to try again.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        WorkspaceService.AtomicWrite(destination, bytes);
        return true;
    }

    private static string FindDestination(string root)
    {
        var plugins = Path.Combine(root, "BepInEx", "plugins");
        var matches = WorkspaceService.SafeFiles(plugins)
            .Where(p => Path.GetFileName(p).Equals("OrbitClient.dll", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(p).Equals("OrbitClient.dll.disabled", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) throw new IOException("Multiple OrbitClient DLLs found. Keep one companion in the plugins folder, then restart Orbit.");
        if (matches.Length == 1 && matches[0].EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Orbit companion is disabled. Enable it in Installed mods to receive automatic updates.");
        return matches.FirstOrDefault() ?? Path.Combine(plugins, "OrbitClient.dll");
    }

    private static Version ValidateCompanion(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 64 * 1024 * 1024) throw new IOException("Invalid companion download size.");
        try
        {
            using var pe = new PEReader(new MemoryStream(bytes, false));
            if (!pe.HasMetadata) throw new BadImageFormatException();
            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly || metadata.GetString(metadata.GetAssemblyDefinition().Name) != "OrbitClient")
                throw new BadImageFormatException();
            foreach (var handle in metadata.CustomAttributes)
            {
                var attribute = metadata.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
                var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                if (metadata.GetString(type.Namespace) != "BepInEx" || metadata.GetString(type.Name) != "BepInPlugin") continue;
                var value = metadata.GetBlobReader(attribute.Value);
                if (value.ReadUInt16() == 1 && value.ReadSerializedString() == PluginGuid) return metadata.GetAssemblyDefinition().Version;
            }
            throw new BadImageFormatException();
        }
        catch (BadImageFormatException ex)
        { throw new IOException("The download is not the Orbit Gorilla Tag companion. The installed mod was kept.", ex); }
    }
}
