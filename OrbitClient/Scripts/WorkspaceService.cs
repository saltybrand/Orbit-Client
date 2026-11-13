using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace OrbitClient.Scripts;

public sealed record LocalMod(string Path, string Name, string Version, string Guid, bool Enabled,
  IReadOnlyList<string> Dependencies, string? Error);
public sealed record CheckResult(string Title, string Detail, bool Passed);
public sealed record SnapshotInfo(string Path, DateTime Created, long Bytes);
public sealed class ClientPreferences
{
    public string GamePath
    {
        get;
        set;
    } = @"C:\Program Files (x86)\Steam\steamapps\common\Gorilla Tag";
    public string Accent
    {
        get;
        set;
    } = "#9333EA";
    public string Background
    {
        get;
        set;
    } = "Midnight";
    public bool Compact
    {
        get;
        set;
    }
    public bool Stars
    {
        get;
        set;
    }
    public string Font
    {
        get;
        set;
    } = "Segoe UI";
    public double TextScale
    {
        get;
        set;
    } = 1;
    public double SidebarWidth
    {
        get;
        set;
    } = 200;
    public bool ShowNavLabels
    {
        get;
        set;
    } = true;
    public double CornerRadius
    {
        get;
        set;
    } = 12;
    public double SurfaceOpacity
    {
        get;
        set;
    } = .95;
    public double BackgroundOpacity
    {
        get;
        set;
    } = .35;
    public string ImageFit
    {
        get;
        set;
    } = "Fill";
    public int StarCount
    {
        get;
        set;
    } = 40;
    public double StarSpeed
    {
        get;
        set;
    } = 1;
    public bool ReducedMotion
    {
        get;
        set;
    }
    public string StartupPage
    {
        get;
        set;
    } = "Home";
    public bool RememberWindow
    {
        get;
        set;
    } = true;
    public double WindowWidth
    {
        get;
        set;
    } = 1220;
    public double WindowHeight
    {
        get;
        set;
    } = 760;
    public bool StartMaximized
    {
        get;
        set;
    }
    public bool ConfirmClose
    {
        get;
        set;
    }
    public bool CheckBeforeLaunch
    {
        get;
        set;
    }
    public int DownloadTimeout
    {
        get;
        set;
    } = 60;
    public int DiscoverPageSize
    {
        get;
        set;
    } = 8;
    public bool AutoSnapshot
    {
        get;
        set;
    } = true;
    public bool CloseAfterLaunch
    {
        get;
        set;
    }
    public bool CheckUpdates
    {
        get;
        set;
    } = true;
}

public sealed class WorkspaceService
{
    public string DataRoot
    {
        get;
    }
    public string PreferencesPath => System.IO.Path.Combine(DataRoot, "preferences.json");
    private static readonly string[] SnapshotRoots = ["BepInEx/plugins", "BepInEx/config"];

    public WorkspaceService(string? dataRoot = null) => DataRoot = dataRoot ?? System.IO.Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitClient");

    public ClientPreferences LoadPreferences() => File.Exists(PreferencesPath) ?
      JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(PreferencesPath)) ?? new() : new();

    public void SavePreferences(ClientPreferences preferences)
    {
        Directory.CreateDirectory(DataRoot);
        AtomicWrite(PreferencesPath, JsonSerializer.SerializeToUtf8Bytes(preferences));
    }

    public static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static string VerifyGame(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath)) throw new IOException("Choose your Gorilla Tag folder in Settings.");
        var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(gamePath.Trim()));
        if (!File.Exists(System.IO.Path.Combine(root, "Gorilla Tag.exe")))
            throw new IOException("Gorilla Tag.exe was not found. Select the game installation in Settings.");
        EnsureNoLinks(root);
        return root;
    }

    public static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("Gorilla Tag");
        try
        {
            return processes.Length != 0;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    public static void RequireGameClosed()
    {
        if (IsGameRunning()) throw new IOException("Close Gorilla Tag before changing mods or restoring a snapshot.");
    }

    public static void EnsureNoLinks(string path)
    {
        for (var item = new DirectoryInfo(System.IO.Path.GetFullPath(path)); item != null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked folders are not supported for mod and snapshot operations: " + item.FullName);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Linked files are not supported: " + path);
    }

    public static IEnumerable<string> SafeFiles(string root)
    {
        EnsureNoLinks(root);
        if (!Directory.Exists(root)) yield
        break;
        foreach (var file in Directory.EnumerateFiles(root))
        {
            EnsureNoLinks(file);
            yield
            return file;
        }
        foreach (var directory in Directory.EnumerateDirectories(root))
            foreach (var file in SafeFiles(directory)) yield
            return file;
    }

    public List<LocalMod> ReadMods(string gamePath)
    {
        var root = VerifyGame(gamePath);
        return SafeFiles(System.IO.Path.Combine(root, "BepInEx", "plugins"))
          .Where(x => x.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase))
          .Select(ReadMod).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static LocalMod ReadMod(string path) => ReadMod(path, null);

    public static LocalMod ReadMod(string path, Stream? content)
    {
        var name = System.IO.Path.GetFileName(path).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase);
        string version = "Unknown", guid = "";
        var dependencies = new List<string>();
        string? error = null;
        try
        {
            using
            var stream = content ?? File.OpenRead(path);
            using
            var pe = new PEReader(stream);
            if (!pe.HasMetadata) throw new BadImageFormatException("Not a managed mod assembly.");
            var reader = pe.GetMetadataReader();
            version = reader.GetAssemblyDefinition().Version.ToString();
            foreach (var handle in reader.CustomAttributes)
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference) continue;
                var type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                if (reader.GetString(type.Namespace) != "BepInEx") continue;
                var kind = reader.GetString(type.Name);
                if (kind is not ("BepInPlugin"
                    or "BepInDependency")) continue;
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) continue;
                var id = blob.ReadSerializedString() ?? "";
                if (kind == "BepInPlugin")
                {
                    guid = id;
                    name = blob.ReadSerializedString() ?? name;
                    version = blob.ReadSerializedString() ?? version;
                }
                else
                {
                    if (blob.RemainingBytes == 6 && blob.ReadInt32() == 2) continue;
                    dependencies.Add(id);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException)
        {
            error = ex.Message;
        }
        return new(path, name, version, guid, !path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase), dependencies, error);
    }

    public void ToggleMod(string gamePath, LocalMod mod)
    {
        RequireGameClosed();
        var root = VerifyGame(gamePath);
        var plugins = System.IO.Path.Combine(root, "BepInEx", "plugins") + System.IO.Path.DirectorySeparatorChar;
        var source = System.IO.Path.GetFullPath(mod.Path);
        if (!source.StartsWith(plugins, StringComparison.OrdinalIgnoreCase)) throw new IOException("Mod is outside this installation.");
        EnsureNoLinks(source);
        var target = mod.Enabled ? source + ".disabled" : source[..^9];
        if (File.Exists(target)) throw new IOException("Both enabled and disabled copies exist. Resolve the duplicate in the mods folder.");
        File.Move(source, target);
    }

    public List<CheckResult> Diagnose(string gamePath)
    {
        var results = new List<CheckResult>();
        string root;
        try
        {
            root = VerifyGame(gamePath);
            results.Add(new("Game installation", root, true));
        }
        catch (IOException ex)
        {
            return [new("Game installation", ex.Message, false)];
        }
        foreach (var (file, fix) in new[] {
      ("BepInEx/core/BepInEx.dll", "Install or repair BepInEx using the mod loader's installation instructions."),
      ("winhttp.dll", "Repair the BepInEx loader in the game folder."),
      ("doorstop_config.ini", "Restore the BepInEx doorstop configuration.")
    })
        {
            var exists = File.Exists(System.IO.Path.Combine(root, file));
            results.Add(new(System.IO.Path.GetFileName(file), exists ? "Found." : fix, exists));
        }
        var mods = ReadMods(root);
        var enabled = mods.Where(x => x.Enabled).ToList();
        foreach (var mod in enabled)
        {
            if (mod.Error != null) results.Add(new(mod.Name, mod.Error + " Reinstall this file from its author.", false));
            foreach (var dependency in mod.Dependencies.Where(id => !enabled.Any(x => x.Guid == id)))
                results.Add(new(mod.Name, "Required plugin not found: " + dependency + ". Install or enable it, then restart the game.", false));
        }
        foreach (var duplicates in enabled.Where(x => x.Guid.Length > 0).GroupBy(x => x.Guid).Where(x => x.Count() > 1))
            results.Add(new("Duplicate plugin", duplicates.Key + ": remove extra copies from your game's BepInEx/plugins folder.", false));
        results.Add(new("Plugin inventory", $"{enabled.Count} enabled / {mods.Count} files. Dependency versions and runtime compatibility still depend on each mod.", true));
        var log = System.IO.Path.Combine(root, "BepInEx", "LogOutput.log");
        if (File.Exists(log))
        {
            using
            var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length > 256 * 1024) stream.Seek(-256 * 1024, SeekOrigin.End);
            using
            var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n').Where(x => x.Contains("error", StringComparison.OrdinalIgnoreCase) ||
              x.Contains("exception", StringComparison.OrdinalIgnoreCase)).TakeLast(8).ToList();
            results.Add(new("Recent startup log", lines.Count == 0 ? "No error lines in the latest 256 KB." :
              $"Log dated {File.GetLastWriteTime(log):g}. These may be from an earlier session.\n" + string.Join("\n", lines) +
              "\nReview the named plugin in your game's BepInEx/plugins folder before launching again.", lines.Count == 0));
        }
        else results.Add(new("Startup log", "No BepInEx log yet. Launch the game once; if no log appears, repair the loader.", false));
        return results;
    }

    private string SnapshotFolder(string root)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
          System.Text.Encoding.UTF8.GetBytes(System.IO.Path.GetFullPath(root).TrimEnd('\\').ToUpperInvariant())))[..16];
        return System.IO.Path.Combine(DataRoot, "Snapshots", key);
    }

    public List<SnapshotInfo> ListSnapshots(string root)
    {
        var folder = SnapshotFolder(root);
        return Directory.Exists(folder) ? Directory.GetFiles(folder, "*.zip").Select(x => new FileInfo(x))
          .OrderByDescending(x => x.CreationTimeUtc).Select(x => new SnapshotInfo(x.FullName, x.CreationTime, x.Length)).ToList() : [];
    }

    public string CreateSnapshot(string gamePath)
    {
        RequireGameClosed();
        var root = VerifyGame(gamePath);
        var folder = SnapshotFolder(root);
        Directory.CreateDirectory(folder);
        var final = System.IO.Path.Combine(folder, $"Orbit-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.zip");
        var temp = final + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (var relative in SnapshotRoots)
                    foreach (var file in SafeFiles(System.IO.Path.Combine(root, relative)))
                        archive.CreateEntryFromFile(file, System.IO.Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Fastest);
                if (File.Exists(PreferencesPath)) archive.CreateEntryFromFile(PreferencesPath, "Orbit/preferences.json");
                using
                var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                writer.Write(JsonSerializer.Serialize(new SnapshotManifest(1, root)));
            }
            File.Move(temp, final);
            return final;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public void RestoreSnapshot(string gamePath, string snapshot)
    {
        RequireGameClosed();
        var root = VerifyGame(gamePath);
        if (!ListSnapshots(root).Any(x => string.Equals(x.Path, snapshot, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Choose a snapshot belonging to this installation.");
        using
        var archive = ZipFile.OpenRead(snapshot);
        using
        var manifestStream = archive.GetEntry("manifest.json")?.Open() ??
          throw new IOException("Snapshot manifest is missing.");
        var manifest = JsonSerializer.Deserialize<SnapshotManifest>(manifestStream);
        if (manifest?.Format != 1 || !string.Equals(manifest.GamePath, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Snapshot belongs to another installation or format.");
        var stage = System.IO.Path.Combine(root, ".orbit-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var swaps = new List<(string Target, string Old, bool HadOriginal)>();
        try
        {
            long total = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName == "manifest.json") continue;
                var name = entry.FullName.Replace('\\', '/');
                if (!SnapshotRoots.Any(x => name.StartsWith(x + "/", StringComparison.Ordinal)) && name != "Orbit/preferences.json")
                    throw new IOException("Snapshot contains an unexpected file.");
                if (name.Split('/').Any(x => x is ".."
                    or "."
                    or "" || x.Contains(':')) || !seen.Add(name))
                    throw new IOException("Snapshot contains an invalid or duplicate path.");
                total += entry.Length;
                if (total > 8L * 1024 * 1024 * 1024) throw new IOException("Snapshot exceeds the 8 GB restore limit.");
                var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(stage, name));
                if (!destination.StartsWith(stage + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Invalid snapshot path.");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination);
            }
            var savedPreferences = System.IO.Path.Combine(stage, "Orbit", "preferences.json");
            ClientPreferences? preferences = File.Exists(savedPreferences) ?
              JsonSerializer.Deserialize<ClientPreferences>(File.ReadAllText(savedPreferences)) : null;
            CreateSnapshot(root);
            RequireGameClosed();
            foreach (var relative in SnapshotRoots)
            {
                var target = System.IO.Path.Combine(root, relative);
                EnsureNoLinks(target);
                var original = target + ".orbit-previous-" + Guid.NewGuid().ToString("N");
                var hadOriginal = Directory.Exists(target);
                if (hadOriginal) Directory.Move(target, original);
                swaps.Add((target, original, hadOriginal));
                var staged = System.IO.Path.Combine(stage, relative);
                Directory.CreateDirectory(staged);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                Directory.Move(staged, target);
            }
            if (preferences != null)
            {
                preferences.GamePath = root;
                SavePreferences(preferences);
            }
        }
        catch
        {
            foreach (var swap in swaps.AsEnumerable().Reverse())
            {
                if (Directory.Exists(swap.Target)) Directory.Move(swap.Target, swap.Target + ".orbit-failed-" + Guid.NewGuid().ToString("N"));
                if (swap.HadOriginal) Directory.Move(swap.Old, swap.Target);
            }
            throw;
        }
        finally
        {

            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }

    public sealed record SnapshotManifest(int Format, string GamePath);
}
