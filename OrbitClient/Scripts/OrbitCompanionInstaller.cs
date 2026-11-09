using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OrbitClient.Scripts;

/// <summary>Installs the published companion without loading downloaded code.</summary>
public sealed class OrbitCompanionInstaller
{
    public const string DownloadSource = "orbit://assets/Mods/OrbitClient.dll";
    private const string PluginGuid = "com.Meep.gorillatag.OrbitClient";

    public async Task<bool> InstallAsync(string gamePath, Func<Task<byte[]>> download, Action? beforeWrite = null)
    {
        var root = WorkspaceService.VerifyGame(gamePath);
        WorkspaceService.RequireGameClosed();
        if (!BepInExInstaller.IsInstalled(root))
            throw new IOException("Install BepInEx in Settings before Orbit can install its companion automatically.");
        var destination = FindDestination(root);
        var bytes = await download().ConfigureAwait(false);
        ValidateCompanion(bytes);
        WorkspaceService.RequireGameClosed();
        WorkspaceService.EnsureNoLinks(destination);
        if (File.Exists(destination))
        {
            var installed = await File.ReadAllBytesAsync(destination).ConfigureAwait(false);
            if (bytes.AsSpan().SequenceEqual(installed)) return false;
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

    private static void ValidateCompanion(byte[] bytes)
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
                if (value.ReadUInt16() == 1 && value.ReadSerializedString() == PluginGuid) return;
            }
            throw new BadImageFormatException();
        }
        catch (BadImageFormatException ex)
        { throw new IOException("The download is not the Orbit Gorilla Tag companion. The installed mod was kept.", ex); }
    }
}
