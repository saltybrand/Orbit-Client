using System.IO;
using OrbitClient.DataModels;

namespace OrbitClient.Scripts;

/// <summary>Resolves a catalog DLL without changing existing files or enabling disabled mods.</summary>
public static class DiscoverInstallation
{
    public static string FileName(DiscoverMod mod)
    {
        if (!Uri.TryCreate(mod.download, UriKind.Absolute, out var uri)) throw new IOException("Invalid mod download.");
        var name = Path.GetFileName(uri.LocalPath);
        if (!ModSubmissionFile.ValidName(name)) throw new IOException("The catalog must provide a simple DLL filename.");
        return name;
    }

    public static string Destination(string root, DiscoverMod mod, byte[] bytes, string? existingPath = null)
    {
        OrbitContentService.VerifyModDownload(mod, bytes);
        ModSubmissionFile.FromBytes(FileName(mod), bytes);
        var plugins = Path.Combine(WorkspaceService.VerifyGame(root), "BepInEx", "plugins");
        var destination = Path.GetFullPath(existingPath ?? Path.Combine(plugins, FileName(mod)));
        if (!destination.StartsWith(plugins + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !(destination.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || destination.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Invalid mod destination.");
        var enabled = destination.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? destination[..^9] : destination;
        var copies = WorkspaceService.SafeFiles(plugins).Where(x =>
            string.Equals(Path.GetFileName(x).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase), Path.GetFileName(enabled), StringComparison.OrdinalIgnoreCase)).ToList();
        if (copies.Count > 1) throw new IOException("Duplicate DLL copies found. Keep one enabled or disabled copy in Mod Library before installing.");
        if (copies.Count == 1) destination = copies[0];
        WorkspaceService.EnsureNoLinks(destination);
        // Read the candidate's identity as metadata only; never load the assembly.
        using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes));
        var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        var assembly = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        var candidate = WorkspaceService.ReadMod("download.dll", new MemoryStream(bytes));
        foreach (var other in WorkspaceService.SafeFiles(plugins).Where(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.Equals(other, destination, StringComparison.OrdinalIgnoreCase)) continue;
            if (candidate.Guid.Length > 0 && WorkspaceService.ReadMod(other).Guid == candidate.Guid)
                throw new IOException("This plugin is already installed under another filename. Resolve the duplicate in Mod Library: " + Path.GetFileName(other));
            try
            {
                using var reader = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(other));
                if (!reader.HasMetadata) continue;
                var otherMetadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader);
                if (otherMetadata.GetString(otherMetadata.GetAssemblyDefinition().Name) == assembly)
                    throw new IOException("This DLL is already installed under another filename. Remove the duplicate in Mod Library before installing: " + Path.GetFileName(other));
            }
            catch (BadImageFormatException) { }
        }
        return destination;
    }
}
