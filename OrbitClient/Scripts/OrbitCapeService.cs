using OrbitClient.Capes;
using OrbitClient.Core;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OrbitClient.Scripts;

public sealed class OrbitCapeService : IDisposable
{
    private readonly IOrbitCapeAuthority authority;
    private readonly Func<string, string, Task> send;
    private readonly string selectionDirectory;
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private int selectionRevision, previewRevision;
    private string ownerKey = "";
    public CapeCatalog Catalog { get; private set; } = new();
    public string SelectedId { get; private set; } = "";
    public bool SignedIn => authority.OwnerKey.Length > 0;
    public bool IsVerified { get; private set; }

    public OrbitCapeService(IOrbitCapeAuthority authority, string dataRoot, Func<string, string, Task> send)
    {
        this.authority = authority;
        this.send = send;
        selectionDirectory = Path.Combine(dataRoot, "cape-selections");
    }

    public async Task SyncAsync()
    {
        var revision = Volatile.Read(ref selectionRevision);
        var preview = Volatile.Read(ref previewRevision);
        await operation.WaitAsync(lifetime.Token);
        try
        {
            await LoadCoreAsync();
            if (SelectedId.Length > 0 && !Catalog.Items.Any(c => c.Id == SelectedId && c.Owned))
            { SaveSelection(""); await send("cape_remove", ""); }
            Catalog.SelectedId = SelectedId;
            await send("capes_catalog", JsonSerializer.Serialize(Catalog));
            if (revision == Volatile.Read(ref selectionRevision) && preview == Volatile.Read(ref previewRevision) && SelectedId.Length > 0)
            {
                var image = await GetImageCoreAsync(SelectedId, true, "");
                if (revision == Volatile.Read(ref selectionRevision) && preview == Volatile.Read(ref previewRevision))
                    await send("cape_image", JsonSerializer.Serialize(image));
            }
        }
        catch { await InvalidateAsync(); throw; }
        finally { operation.Release(); }
    }

    private async Task LoadCoreAsync()
    {
        var expectedOwner = authority.OwnerKey;
        var items = await authority.ReadCatalogAsync(lifetime.Token);
        if (authority.OwnerKey != expectedOwner) throw new IOException("Your account changed. Refresh your capes.");
        if (items.Count > 64) throw new IOException("The cape catalog is too large.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cape in items)
        {
            if (cape == null || !cape.Enabled || !cape.HasValidId() || !cape.HasValidFit() || !cape.HasPrivateImagePath()
                || string.IsNullOrWhiteSpace(cape.Title) || cape.Title.Length > 120 || (cape.Description?.Length ?? 0) > 1000
                || cape.UnlockType is not ("free" or "admin" or "key") || !ids.Add(cape.Id))
                throw new IOException("The server returned an invalid cape catalog.");
            if (expectedOwner.Length == 0) cape.Owned = false;
        }
        if (ownerKey != expectedOwner)
        {
            ownerKey = expectedOwner;
            SelectedId = ReadSelection();
            await send("cape_remove", "");
        }
        Catalog = new CapeCatalog { Items = items, SelectedId = SelectedId };
        IsVerified = true;
    }

    public async Task ClaimFreeAsync(string id)
    {
        await operation.WaitAsync(lifetime.Token);
        try
        {
            await LoadCoreAsync();
            var cape = Catalog.Items.FirstOrDefault(c => c.Id == id) ?? throw new IOException("Cape unavailable.");
            if (!SignedIn || cape.UnlockType != "free") throw new IOException("This cape cannot be claimed from the free shop.");
            await authority.ClaimFreeAsync(id, lifetime.Token);
            await LoadCoreAsync();
            if (!Catalog.Items.Any(c => c.Id == id && c.Owned)) throw new IOException("The server did not confirm ownership.");
            await send("capes_catalog", JsonSerializer.Serialize(Catalog));
        }
        finally { operation.Release(); }
    }

    public async Task PreviewAsync(CapeRequest request)
    {
        var revision = Interlocked.Increment(ref previewRevision);
        await operation.WaitAsync(lifetime.Token);
        try
        {
            if (revision != Volatile.Read(ref previewRevision)) return;
            var image = await GetImageCoreAsync(request.Id, false, request.RequestId);
            if (revision == Volatile.Read(ref previewRevision)) await send("cape_image", JsonSerializer.Serialize(image));
        }
        catch { await InvalidateAsync(); throw; }
        finally { operation.Release(); }
    }

    public async Task<string> RedeemAsync(string key)
    {
        await operation.WaitAsync(lifetime.Token);
        try
        {
            var expectedOwner = authority.OwnerKey;
            if (expectedOwner.Length == 0) throw new IOException("Sign in to redeem a cape key.");
            var id = await authority.RedeemAsync(key, lifetime.Token);
            if (authority.OwnerKey != expectedOwner) throw new IOException("Your account changed while redeeming the key.");
            await LoadCoreAsync();
            if (!Catalog.Items.Any(c => c.Id == id && c.Owned)) throw new IOException("Redemption completed but the collection could not be refreshed. Refresh your dressing room.");
            await send("capes_catalog", JsonSerializer.Serialize(Catalog));
            return id;
        }
        finally { operation.Release(); }
    }

    public async Task SelectAsync(string id, string requestId = "")
    {
        var revision = Interlocked.Increment(ref selectionRevision);
        Interlocked.Increment(ref previewRevision);
        await operation.WaitAsync(lifetime.Token);
        try
        {
            var image = await GetImageCoreAsync(id, true, requestId);
            if (revision != Volatile.Read(ref selectionRevision)) return;
            SaveSelection(id);
            await send("cape_image", JsonSerializer.Serialize(image));
        }
        catch { await InvalidateAsync(); throw; }
        finally { operation.Release(); }
    }

    public async Task ClearAsync(bool notifyMod = true)
    {
        var revision = Interlocked.Increment(ref selectionRevision);
        Interlocked.Increment(ref previewRevision);
        await operation.WaitAsync(lifetime.Token);
        try { if (revision == Volatile.Read(ref selectionRevision)) { SaveSelection(""); if (notifyMod) await send("cape_remove", ""); } }
        finally { operation.Release(); }
    }

    public async Task<byte[]> PreviewBytesAsync(string id)
    {
        await operation.WaitAsync(lifetime.Token);
        try { return Convert.FromBase64String((await GetImageCoreAsync(id, false, "")).ImageBase64); }
        finally { operation.Release(); }
    }

    private async Task<CapeImageMessage> GetImageCoreAsync(string id, bool equip, string requestId)
    {
        // Always ask the authority again. UI flags, caches and saved IDs are never grants.
        await LoadCoreAsync();
        var cape = Catalog.Items.FirstOrDefault(c => c.Id == id && c.Owned)
            ?? throw new IOException("You do not own this cape, or it is no longer available.");
        var expectedOwner = authority.OwnerKey;
        if (expectedOwner.Length == 0) throw new IOException("Sign in to use capes.");
        var bytes = await authority.DownloadOwnedAsync(id, lifetime.Token);
        if (authority.OwnerKey != expectedOwner) throw new IOException("Your account changed while loading a cape.");
        if (bytes.Length > 4 * 1024 * 1024 || !CapeImageRules.TryGetDimensions(bytes, out _, out _)) throw new IOException("Invalid cape image.");
        return new CapeImageMessage { Cape = cape, ImageBase64 = Convert.ToBase64String(bytes), Equip = equip, RequestId = requestId };
    }

    private string SelectionFile => Path.Combine(selectionDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey))) + ".json");
    private string ReadSelection()
    {
        try { return ownerKey.Length > 0 && File.Exists(SelectionFile) && new FileInfo(SelectionFile).Length <= 512
            ? JsonSerializer.Deserialize<string>(File.ReadAllText(SelectionFile)) ?? "" : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return ""; }
    }
    private void SaveSelection(string id)
    {
        if (ownerKey.Length > 0) { Directory.CreateDirectory(selectionDirectory); WorkspaceService.AtomicWrite(SelectionFile, JsonSerializer.SerializeToUtf8Bytes(id)); }
        SelectedId = id; Catalog.SelectedId = id;
    }
    private async Task InvalidateAsync()
    {
        IsVerified = false;
        Catalog = new();
        await send("cape_remove", "");
    }
    public Task AccountChangedAsync()
    {
        Interlocked.Increment(ref selectionRevision); Interlocked.Increment(ref previewRevision);
        ownerKey = ""; SelectedId = ""; Catalog = new(); IsVerified = false;
        return send("cape_remove", "");
    }
    public void Dispose() => lifetime.Cancel();
}
