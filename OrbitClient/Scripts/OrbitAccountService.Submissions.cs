using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace OrbitClient.Scripts;

public sealed record ModSubmission(Guid Id, Guid UserId, string Title, string Description, string FileName, long ByteSize,
    string Sha256, string Status, DateTimeOffset CreatedAt, string ReviewNotes, DateTimeOffset? PublishedAt = null)
{
    public Guid ModId { get; init; } = Id;
    public Guid BinaryId { get; init; } = Id;
    public Guid? BaseRevisionId { get; init; }
    public Guid? OpenRevisionId { get; init; }
    public int RevisionNumber { get; init; } = 1;
    public string Version { get; init; } = "1.0.0";
    public string Category { get; init; } = "Community";
    public string ChangeType { get; init; } = "release";
    public string ChangeNotes { get; init; } = "";
    public string AuthorName { get; init; } = "Community";
    public bool IsCurrent { get; init; }
    public bool IsRemoved { get; init; }
    public string ObjectPath => $"{UserId:D}/{BinaryId:D}/mod.dll";
    public string PublicObjectPath => $"{BinaryId:D}/{ModId:D}.dll";
    private static Guid? OptionalId(JsonElement row,string key) => row.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.String ? value.GetGuid() : null;
    internal static ModSubmission Parse(JsonElement row)
    {
        var item = new ModSubmission(row.GetProperty("id").GetGuid(), row.GetProperty("user_id").GetGuid(),
            row.GetProperty("title").GetString() ?? "", row.GetProperty("description").GetString() ?? "",
            row.GetProperty("file_name").GetString() ?? "", row.GetProperty("byte_size").GetInt64(),
            row.GetProperty("sha256").GetString() ?? "", row.GetProperty("status").GetString() ?? "",
            row.GetProperty("created_at").GetDateTimeOffset(), row.GetProperty("review_notes").GetString() ?? "",
            row.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String ? published.GetDateTimeOffset() : null)
        {
            ModId=OptionalId(row,"mod_id")??row.GetProperty("id").GetGuid(),
            BinaryId=OptionalId(row,"binary_submission_id")??row.GetProperty("id").GetGuid(),
            BaseRevisionId=OptionalId(row,"base_revision_id"),OpenRevisionId=OptionalId(row,"open_revision_id"),
            RevisionNumber=row.TryGetProperty("revision_number",out var revision)?revision.GetInt32():1,
            Version=row.TryGetProperty("version",out var version)?version.GetString()??"1.0.0":"1.0.0",
            Category=row.TryGetProperty("category",out var category)?category.GetString()??"Community":"Community",
            ChangeType=row.TryGetProperty("change_type",out var kind)?kind.GetString()??"release":"release",
            ChangeNotes=row.TryGetProperty("change_notes",out var notes)?notes.GetString()??"":"",
            AuthorName=row.TryGetProperty("author_name",out var author)?author.GetString()??"Community":"Community",
            IsCurrent=row.TryGetProperty("is_current",out var current)&&current.ValueKind==JsonValueKind.True,
            IsRemoved=row.TryGetProperty("is_removed",out var removed)&&removed.ValueKind==JsonValueKind.True
        };
        if (!ModSubmissionFile.ValidName(item.FileName) || item.ByteSize is < 1 or > ModSubmissionFile.MaximumBytes
            || !System.Text.RegularExpressions.Regex.IsMatch(item.Sha256, "^[a-f0-9]{64}$")
            || item.Status is not ("uploading" or "pending" or "approved" or "rejected")) throw new IOException("Invalid submission details returned by the service.");
        return item;
    }
}
public sealed record ModSubmissionState(bool Reviewer, DateTimeOffset NextResetAt, ModSubmission? Today);

public sealed partial class OrbitAccountService
{
    private readonly HttpClient submissionFiles;
    private const string SubmissionBucket = "orbit-mod-submissions";

    private async Task<JsonElement> SubmissionRequestAsync(string path, object? body = null, bool read = false, bool allowEmpty = false)
    {
        var token = await TokenAsync().ConfigureAwait(false);
        using var request = new HttpRequestMessage(read ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (!read) request.Content = JsonContent.Create(body ?? new { });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
        RejectRevokedAvatarSession(response);
        if (!response.IsSuccessStatusCode)
        {
            var code = "";
            var message = "";
            try
            {
                using var error = JsonDocument.Parse(await OrbitHttp.ReadBoundedAsync(response, 65536, lifetime.Token).ConfigureAwait(false));
                code = Text(error.RootElement, "code");
                message = Text(error.RootElement, "message");
            }
            catch (JsonException) { }
            throw new IOException(code switch
            {
                "P1001" => "One DLL per account per UTC day. Retry the same file today, or wait until midnight UTC for a new file.",
                "P1002" => "Check the mod title, description, filename, and file size.",
                "P1003" => "Sign in with a confirmed Orbit account to submit a mod.",
                "P1004" => "This submission or review action is not available to your account.",
                "P1005" => "The upload is not complete or its size does not match. Retry with the same DLL.",
                "P1006" => "This mod already has a revision awaiting inspection, or this revision has already been reviewed. Refresh to see its status.",
                "P1007" => "The published version changed. Refresh your mods before editing or reviewing this change.",
                "P1008" => "Choose a release version newer than the published version.",
                "P1009" => "No listing changes were made. Edit the name, description, or category before submitting.",
                "P1010" => "This mod has been removed. Refresh your listings.",
                _ => path.StartsWith("functions/v1/orbit-publish-mod", StringComparison.Ordinal)
                    ? message.Length > 0 ? message[..Math.Min(message.Length, 512)] : "Publication failed. Refresh the review queue and retry."
                    : "Mod submissions are unavailable. Check your connection and try again later."
            });
        }
        var payload = await OrbitHttp.ReadBoundedAsync(response, 1024 * 1024, lifetime.Token).ConfigureAwait(false);
        if (allowEmpty && payload.Length == 0) return default;
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }

    public async Task<ModSubmissionState> GetModSubmissionStateAsync()
    {
        var row = await SubmissionRequestAsync("rest/v1/rpc/orbit_mod_submission_state").ConfigureAwait(false);
        var today = row.GetProperty("today");
        return new(row.GetProperty("reviewer").GetBoolean(), row.GetProperty("next_reset_at").GetDateTimeOffset(),
            today.ValueKind == JsonValueKind.Null ? null : ModSubmission.Parse(today));
    }

    public async Task<IReadOnlyList<ModSubmission>> GetModSubmissionsAsync(bool reviewQueue = false, int page = 0, bool ownedMods = false)
    {
        if (page is < 0 or > 100000) throw new IOException("Invalid submission page.");
        var rows = await SubmissionRequestAsync("rest/v1/rpc/orbit_list_mod_submissions",new {review_queue=reviewQueue,requested_page=page,owned_mods=ownedMods}).ConfigureAwait(false);
        return rows.EnumerateArray().Select(ModSubmission.Parse).ToArray();
    }

    public Task<ModSubmission> SubmitModAsync(string title, string description, ModSubmissionFile file, string version="1.0.0",string category="Community",string notes="") =>
        ProposeModRevisionAsync(null,title,description,version,category,notes,file);

    public async Task RemoveOwnedModAsync(Guid modId) =>
        _ = await SubmissionRequestAsync("rest/v1/rpc/orbit_remove_owned_mod", new { requested_mod_id = modId }, allowEmpty: true).ConfigureAwait(false);

    public async Task<ModSubmission> ProposeModRevisionAsync(ModSubmission? current, string title, string description, string version, string category, string notes, ModSubmissionFile? file = null)
    {
        if (title.Trim().Length is < 1 or > 80 || description.Length > 2000) throw new IOException("Enter a title up to 80 characters and a description up to 2,000 characters.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(version,@"^[0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}(\.[0-9]{1,4})?$") || category.Trim().Length is <1 or >40 || notes.Length>2000)
            throw new IOException("Use a version such as 1.0.0, a category up to 40 characters, and change notes up to 2,000 characters.");
        if (current is null && file is null) throw new IOException("Attach a DLL for your first release.");
        // Revalidate the byte snapshot before reserving the daily slot.
        if(file is not null) file = ModSubmissionFile.FromBytes(file.Name, file.Bytes);
        var owner = Account?.Id ?? throw new IOException("Sign in first.");
        var row = await SubmissionRequestAsync("rest/v1/rpc/orbit_propose_mod_revision", new
        {
            requested_mod_id=current?.ModId,expected_base_id=current?.Id,requested_version=version,requested_category=category.Trim(),requested_notes=notes,details_only=file is null,
            requested_title = title.Trim(), requested_description = description, requested_file_name = file?.Name,
            requested_size = file?.Bytes.Length, requested_sha256 = file?.Sha256
        }).ConfigureAwait(false);
        var item = ModSubmission.Parse(row);
        if (item.UserId.ToString("D") != owner || (current is not null && item.ModId!=current.ModId)) throw new IOException("The revision owner could not be verified.");
        if(file is null) return item;
        if (item.Sha256 != file.Sha256 || item.ByteSize != file.Bytes.Length) throw new IOException("The upload reservation could not be verified.");
        if (item.Status != "uploading") return item; // Lost success response: do not upload a second object.
        var token = await TokenAsync().ConfigureAwait(false);
        if (Account?.Id != owner) throw new IOException("Your account changed. Sign in and retry.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "storage/v1/object/" + SubmissionBucket + "/" + item.ObjectPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("x-upsert", "false");
        request.Content = new ByteArrayContent(file.Bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await submissionFiles.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
        RejectRevokedAvatarSession(response);
        // Storage may return 400 with Duplicate code, or HTTP 409 after a lost successful response.
        var exists = response.StatusCode == HttpStatusCode.Conflict;
        if (!response.IsSuccessStatusCode && !exists && response.StatusCode == HttpStatusCode.BadRequest)
        {
            try { using var error = JsonDocument.Parse(await OrbitHttp.ReadBoundedAsync(response, 65536, lifetime.Token).ConfigureAwait(false)); exists = Text(error.RootElement, "error") is "Duplicate" or "ResourceAlreadyExists" || Text(error.RootElement, "code") is "Duplicate" or "ResourceAlreadyExists"; }
            catch (JsonException) { }
        }
        if (!response.IsSuccessStatusCode && !exists) throw new IOException("The DLL could not be uploaded. Retry the same file; the server keeps your daily reservation.");
        if (Account?.Id != owner) throw new IOException("Your account changed. Sign in and refresh submissions.");
        return ModSubmission.Parse(await SubmissionRequestAsync("rest/v1/rpc/orbit_finish_mod_submission", new { requested_id = item.Id }).ConfigureAwait(false));
    }

    public async Task<byte[]> DownloadModForReviewAsync(ModSubmission item)
    {
        var token = await TokenAsync().ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "storage/v1/object/authenticated/" + SubmissionBucket + "/" + item.ObjectPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await submissionFiles.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
        RejectRevokedAvatarSession(response);
        if (!response.IsSuccessStatusCode) throw new IOException("The review file is unavailable or reviewer access was denied.");
        var bytes = await OrbitHttp.ReadBoundedAsync(response, ModSubmissionFile.MaximumBytes, lifetime.Token).ConfigureAwait(false);
        if (bytes.Length != item.ByteSize || Convert.ToHexStringLower(SHA256.HashData(bytes)) != item.Sha256)
            throw new IOException("The downloaded file does not match the submitted size or SHA-256. Do not approve it.");
        return bytes;
    }

    public async Task<ModSubmission> ReviewModSubmissionAsync(Guid id, bool approve, string notes) =>
        ModSubmission.Parse(await SubmissionRequestAsync("rest/v1/rpc/orbit_review_mod_submission",
            new { requested_id = id, decision = approve ? "approved" : "rejected", notes }).ConfigureAwait(false));

    public async Task<ModSubmission> PublishModSubmissionAsync(ModSubmission item)
    {
        if (item.Status != "approved") throw new IOException("Approve the submission before publishing it to Discover.");
        // The server derives reviewer identity and verifies both copies. No client approval flags.
        return ModSubmission.Parse(await SubmissionRequestAsync("functions/v1/orbit-publish-mod", new { requested_id = item.Id }).ConfigureAwait(false));
    }

    public async Task<(ModSubmission? Previous,ModSubmission Proposed)> GetModRevisionComparisonAsync(Guid id)
    {
        var value=await SubmissionRequestAsync("rest/v1/rpc/orbit_mod_revision_comparison",new {requested_id=id}).ConfigureAwait(false);
        var previous=value.GetProperty("previous");
        return (previous.ValueKind==JsonValueKind.Null?null:ModSubmission.Parse(previous),ModSubmission.Parse(value.GetProperty("proposed")));
    }
}
