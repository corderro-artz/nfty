using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Nfty.Core.Formats;
using Nfty.Core.Publish.Community;

namespace Nfty.App.Services;

/// <summary>The community list as the app last saw it.</summary>
/// <param name="Index">The list, or null when none has ever been fetched and the network failed.</param>
/// <param name="FetchedAt">When the list was last confirmed current.</param>
/// <param name="Notice">A sentence to show above the list (offline, list too new, list damaged), or null.</param>
public sealed record CommunityListing(CommunityIndex? Index, DateTimeOffset FetchedAt, string? Notice);

/// <summary>Browsing's local state: the published list, its preview pictures and downloaded Sets. Browsing
/// is token-free, so this owns a plain <see cref="HttpClient"/> that never carries an Authorization header.</summary>
public interface ICommunityCache
{
    /// <summary>Fetches the list (conditionally) and returns the best listing available.</summary>
    /// <param name="ct">Stops the fetch.</param>
    Task<CommunityListing> RefreshAsync(CancellationToken ct);

    /// <summary>The cached listing, or null when nothing is cached.</summary>
    CommunityListing? Cached { get; }

    /// <summary>The gallery's fixed tag list (its <c>tags.json</c>): what was last fetched or cached, empty until then.</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>The local path of a verified preview picture, fetching it when needed; null when it cannot be had.</summary>
    /// <param name="file">The preview the entry points at.</param>
    /// <param name="ct">Stops the fetch.</param>
    Task<string?> PreviewAsync(CommunityFile file, CancellationToken ct);

    /// <summary>The folder downloaded Sets are kept in.</summary>
    string SetsDir { get; }
}

/// <inheritdoc cref="ICommunityCache"/>
public sealed partial class CommunityCache : ICommunityCache
{
    /// <summary>Previews are trimmed, least recently used first, to stay within this many bytes (200 MB).</summary>
    public const long DefaultPreviewBudget = 209_715_200;

    /// <summary>The largest list body that is read (16 MB).</summary>
    public const long MaxIndexBytes = 16L * 1024 * 1024;

    /// <summary>The largest tag list body that is read (64 KB).</summary>
    public const long MaxTagsBytes = 64L * 1024;

    private static readonly TimeSpan Stall = TimeSpan.FromSeconds(30);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex LowerHex64();

    private readonly string _root;
    private readonly HttpClient _http;
    private readonly Uri _indexUrl;
    private readonly long _budget;
    private readonly Func<DateTimeOffset> _clock;
    private CommunityListing? _cached;
    private bool _loaded;
    private IReadOnlyList<string>? _tags;

    /// <summary>Creates the cache under <paramref name="root"/>.</summary>
    /// <param name="root">The <c>community</c> folder. Tests MUST pass a temp directory.</param>
    /// <param name="http">The client to use; null makes a plain one. It must not carry credentials.</param>
    /// <param name="indexUrl">The list's address; null is the shipped gallery.</param>
    /// <param name="previewBudget">How many bytes of previews are kept.</param>
    /// <param name="clock">The time source.</param>
    public CommunityCache(string root, HttpClient? http = null, Uri? indexUrl = null,
        long previewBudget = DefaultPreviewBudget, Func<DateTimeOffset>? clock = null)
    {
        _root = root;
        _http = http ?? new HttpClient();
        _indexUrl = indexUrl ?? CommunityEndpoints.Default.IndexUrl;
        _budget = previewBudget;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The <c>community</c> folder inside a state store, or a temp folder when state is in memory.</summary>
    /// <param name="store">The app's state store.</param>
    public static string RootFor(IStateStore store) =>
        Path.Combine(store.Resolution.Directory ?? Path.Combine(Path.GetTempPath(), StateStore.FolderName), "community");

    private string IndexPath => Path.Combine(_root, "index.json");
    private string EtagPath => Path.Combine(_root, "index.etag");
    private Uri TagsUrl => new(_indexUrl, "tags.json");
    private string TagsPath => Path.Combine(_root, "tags.json");
    private string TagsEtagPath => Path.Combine(_root, "tags.etag");
    private string PreviewDir => Path.Combine(_root, "previews");

    /// <inheritdoc />
    public string SetsDir => Path.Combine(_root, "sets");

    /// <inheritdoc />
    public CommunityListing? Cached
    {
        get
        {
            if (!_loaded) { _cached = LoadCached(); _loaded = true; }
            return _cached;
        }
    }

    private CommunityListing? LoadCached()
    {
        try
        {
            if (!File.Exists(IndexPath)) return null;
            using var fs = File.OpenRead(IndexPath);
            var index = CommunityIndex.Read(fs);
            return new CommunityListing(index, new DateTimeOffset(File.GetLastWriteTimeUtc(IndexPath), TimeSpan.Zero), null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                      or UnsupportedSchemaVersionException)
        { return null; }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Tags => _tags ??= LoadTags();

    private IReadOnlyList<string> LoadTags()
    {
        try
        {
            if (!File.Exists(TagsPath)) return [];
            using var fs = File.OpenRead(TagsPath);
            return CommunityTags.Read(fs).Tags;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                      or UnsupportedSchemaVersionException)
        { return []; }
    }

    /// <inheritdoc />
    public async Task<CommunityListing> RefreshAsync(CancellationToken ct)
    {
        var listing = await RefreshIndexAsync(ct).ConfigureAwait(false);
        await RefreshTagsAsync(ct).ConfigureAwait(false);
        return listing;
    }

    /// <summary>Fetches <c>tags.json</c> beside the index, token-free and conditionally. Any failure keeps the cached list and is not reported: tags are a convenience, never a reason the list fails.</summary>
    private async Task RefreshTagsAsync(CancellationToken ct)
    {
        try
        {
            var current = Tags;
            using var req = new HttpRequestMessage(HttpMethod.Get, TagsUrl);
            if (current.Count > 0 && ReadEtag(TagsEtagPath) is { } etag
                && System.Net.Http.Headers.EntityTagHeaderValue.TryParse(etag, out var tag))
                req.Headers.IfNoneMatch.Add(tag);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return;   // includes 304: the cached list stands
            if (resp.Content.Headers.ContentLength is > MaxTagsBytes) return;
            var body = await ReadCappedAsync(resp, MaxTagsBytes, ct).ConfigureAwait(false);
            if (body is null) return;

            CommunityTags parsed;
            try { parsed = CommunityTags.Read(new MemoryStream(body)); }
            catch (Exception e) when (e is InvalidDataException or UnsupportedSchemaVersionException) { return; }

            Directory.CreateDirectory(_root);
            WriteAtomic(TagsPath, body);
            var newTag = resp.Headers.ETag?.ToString();
            if (newTag is not null) WriteAtomic(TagsEtagPath, System.Text.Encoding.UTF8.GetBytes(newTag));
            else { try { File.Delete(TagsEtagPath); } catch (IOException) { } }
            _tags = parsed.Tags;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException
                                      or TimeoutException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
        }
    }

    private async Task<CommunityListing> RefreshIndexAsync(CancellationToken ct)
    {
        var cached = Cached;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _indexUrl);
            if (cached is not null && ReadEtag() is { } etag
                && System.Net.Http.Headers.EntityTagHeaderValue.TryParse(etag, out var tag))
                req.Headers.IfNoneMatch.Add(tag);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotModified && cached is not null)
            {
                var now = _clock();
                try { File.SetLastWriteTimeUtc(IndexPath, now.UtcDateTime); } catch (IOException) { }
                return _cached = cached with { FetchedAt = now, Notice = null };
            }
            if (!resp.IsSuccessStatusCode) return Failed(cached);

            if (resp.Content.Headers.ContentLength is > MaxIndexBytes) return Failed(cached);
            var body = await ReadCappedAsync(resp, MaxIndexBytes, ct).ConfigureAwait(false);
            if (body is null) return Failed(cached);

            CommunityIndex index;
            try { index = CommunityIndex.Read(new MemoryStream(body)); }
            catch (UnsupportedSchemaVersionException) { return Kept(cached, ShareRules.IndexTooNew); }
            catch (InvalidDataException) { return Kept(cached, ShareRules.ListDamaged); }

            Directory.CreateDirectory(_root);
            WriteAtomic(IndexPath, body);
            var newTag = resp.Headers.ETag?.ToString();
            if (newTag is not null) WriteAtomic(EtagPath, System.Text.Encoding.UTF8.GetBytes(newTag));
            else { try { File.Delete(EtagPath); } catch (IOException) { } }
            var at = _clock();
            try { File.SetLastWriteTimeUtc(IndexPath, at.UtcDateTime); } catch (IOException) { }
            return _cached = new CommunityListing(index, at, null);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException
                                      or TimeoutException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return Failed(cached);
        }
    }

    private CommunityListing Failed(CommunityListing? cached) => cached is null
        ? new CommunityListing(null, _clock(), ShareRules.Unreachable)
        : cached with { Notice = ShareRules.Offline(cached.FetchedAt.ToLocalTime().DateTime) };

    private CommunityListing Kept(CommunityListing? cached, string notice) => cached is null
        ? new CommunityListing(null, _clock(), notice)
        : cached with { Notice = notice };

    private string? ReadEtag(string? path = null)
    {
        path ??= EtagPath;
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (IOException) { return null; }
    }

    /// <summary>Reads at most <paramref name="max"/> bytes, giving up when nothing arrives for 30 seconds.</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage resp, long max, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var ms = new MemoryStream();
        var buf = new byte[81920];
        while (true)
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stall.CancelAfter(Stall);
            int n;
            try { n = await stream.ReadAsync(buf, stall.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException(); }
            if (n == 0) return ms.ToArray();
            if (ms.Length + n > max) return null;
            ms.Write(buf, 0, n);
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch { try { File.Delete(temp); } catch (IOException) { } throw; }
    }

    /// <inheritdoc />
    public async Task<string?> PreviewAsync(CommunityFile file, CancellationToken ct)
    {
        // The hash comes from the network and becomes a file name: only 64 lowercase hex may pass.
        if (file.Sha256 is null || !LowerHex64().IsMatch(file.Sha256)) return null;
        if (file.Size <= 0 || file.Size > ShareRules.MaxPreviewBytes) return null;
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;

        var path = Path.Combine(PreviewDir, file.Sha256 + ".png");
        try
        {
            if (File.Exists(path))
            {
                if (await HashAsync(path, ct).ConfigureAwait(false) == file.Sha256)
                {
                    File.SetLastWriteTimeUtc(path, _clock().UtcDateTime);
                    return path;
                }
                File.Delete(path);
            }

            using var resp = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            if (resp.Content.Headers.ContentLength is { } len && len > file.Size) return null;
            var body = await ReadCappedAsync(resp, file.Size, ct).ConfigureAwait(false);
            if (body is null || body.Length != file.Size
                || Convert.ToHexStringLower(SHA256.HashData(body)) != file.Sha256) return null;

            Directory.CreateDirectory(PreviewDir);
            WriteAtomic(path, body);
            File.SetLastWriteTimeUtc(path, _clock().UtcDateTime);
            Trim(path);
            return path;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException
                                      or TimeoutException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
    }

    /// <summary>Deletes least recently used previews (the file's write time is its use stamp: touched on every hit, because last-access times are off or lazy on common systems) until the folder is within budget; the one just written stays.</summary>
    private void Trim(string keep)
    {
        var files = new DirectoryInfo(PreviewDir).GetFiles("*.png")
            .OrderBy(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).ToList();
        long total = files.Sum(f => f.Length);
        foreach (var f in files)
        {
            if (total <= _budget) break;
            if (string.Equals(f.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
            var len = f.Length;
            try { f.Delete(); total -= len; } catch (IOException) { }
        }
    }
}
