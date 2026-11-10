using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;

namespace Nfty.App.Services;

/// <summary>What the share card is told about the Set it was opened from.</summary>
/// <param name="SetDirectory">Where the Set's files are (for a packed <c>.set</c>, the folder it was unpacked into).</param>
/// <param name="Manifest">The Set's <c>set.json</c>.</param>
/// <param name="FirstImagePath">The first asset's picture, whose size is the canvas; null when the Set has none.</param>
/// <param name="BookCandidates">CookBooks the app can name, which are CHECKED against the hash the Set recorded.</param>
public sealed record ShareSource(string SetDirectory, SetManifest Manifest, string? FirstImagePath,
    IReadOnlyList<string> BookCandidates);

/// <summary>What packing the Set the way a share would, and checking the result, found.</summary>
/// <param name="Facts">What <see cref="ShareCheck"/> learned. When it holds problems its other members are defaults.</param>
/// <param name="Width">The canvas width, read from the first picture (so a too-big canvas can still be named).</param>
/// <param name="Height">The canvas height.</param>
/// <param name="Bytes">The size of the packed <c>.set</c>, which is what the size limit is judged on.</param>
public sealed record ShareCheckResult(ShareFacts Facts, int Width, int Height, long Bytes);

/// <summary>Packs a Set as a share would and checks it, off the UI thread. A seam so the card can be tested with any outcome.</summary>
public interface IShareChecker
{
    /// <summary>Packs and checks.</summary>
    /// <param name="source">The Set.</param>
    /// <param name="options">What the share would carry.</param>
    /// <param name="bookPath">The CookBook to carry, or null.</param>
    /// <param name="ct">Stops the work.</param>
    /// <returns>What was found; never throws for a Set that cannot be shared, which is a problem in the facts.</returns>
    Task<ShareCheckResult> CheckAsync(ShareSource source, ExportOptions options, string? bookPath, CancellationToken ct);
}

/// <summary>The real checker: the same pack the share run makes, into a temporary folder that is gone afterwards.</summary>
public sealed class ShareChecker : IShareChecker
{
    private readonly IParallelismSetting? _threads;
    private readonly string _root;

    /// <summary>Makes a checker.</summary>
    /// <param name="threads">The worker-thread setting, read per check.</param>
    /// <param name="tempRoot">Where the temporary packs go; null uses the system temporary folder.</param>
    public ShareChecker(IParallelismSetting? threads = null, string? tempRoot = null)
    {
        _threads = threads;
        _root = tempRoot ?? Path.GetTempPath();
    }

    /// <inheritdoc />
    public async Task<ShareCheckResult> CheckAsync(ShareSource source, ExportOptions options, string? bookPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        int width = 0, height = 0;
        if (source.FirstImagePath is { } first)
        {
            try
            {
                var info = ImageFormats.Identify(first);
                width = info.Width;
                height = info.Height;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                           or SixLabors.ImageSharp.UnknownImageFormatException
                                           or SixLabors.ImageSharp.InvalidImageContentException) { }
        }

        string dir = Path.Combine(_root, "nfty-share-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var exported = await SetExporter.ExportAsync(source.SetDirectory, dir, options, bookPath, null, null, ct,
                _threads?.Current).ConfigureAwait(false);
            var facts = await ShareCheck.CheckAsync(exported.Path, ct).ConfigureAwait(false);
            return new ShareCheckResult(facts, width, height, new FileInfo(exported.Path).Length);
        }
        catch (Exception ex) when (ex is ArgumentException or CorruptSetException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or SealedSetException)
        {
            // The reader's words are for a terminal; a card says one thing.
            return new ShareCheckResult(
                new ShareFacts([ShareRules.NotASet], 0, 0, 0, false, false, null, "", "", ""), width, height, 0);
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>The user's <c>nfty-public</c> repository, as the share card reads and cleans it. A seam so tests record what is deleted.</summary>
public interface IShareRepo
{
    /// <summary>Reads the repository; null when the user has none yet.</summary>
    /// <param name="login">The account.</param>
    /// <param name="ct">Stops the read.</param>
    /// <returns>The snapshot, or null.</returns>
    /// <exception cref="CommunityException">GitHub refused or cannot be reached; the message is a <see cref="ShareRules"/> sentence.</exception>
    Task<RepoSnapshot?> FetchAsync(string login, CancellationToken ct);

    /// <summary>Deletes the given extras from GitHub, which nfty cannot undo.</summary>
    /// <param name="login">The account.</param>
    /// <param name="extras">What to delete.</param>
    /// <param name="ct">Stops the work.</param>
    Task RemoveAsync(string login, IReadOnlyList<RepoExtra> extras, CancellationToken ct);
}

/// <summary>The real repository seam, over the connected account's client.</summary>
public sealed class GitHubShareRepo : IShareRepo
{
    private readonly IGitHubAccount _account;
    private readonly ICommunityCache? _cache;

    /// <summary>Makes the seam.</summary>
    /// <param name="account">The GitHub connection; its client is asked for on every call, so a reconnect is picked up.</param>
    /// <param name="cache">The published list and the pending pull requests, which say which Releases are live or about to be
    /// (<see cref="ShareProtection"/>); null protects nothing, for a caller with nothing pending.</param>
    public GitHubShareRepo(IGitHubAccount account, ICommunityCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
        _cache = cache;
    }

    private GitHubShareClient Client() =>
        _account.Client() ?? throw new CommunityException(ShareRules.NotConnected);

    // The live share of an open update is still the gallery's, so the card's scan and its Remove must both keep it.
    private async Task<IReadOnlyCollection<string>> ProtectedAsync(CancellationToken ct)
    {
        if (_cache is null) return [];
        long owner = await _account.EnsureUserIdAsync(ct).ConfigureAwait(false) ?? throw new CommunityException(ShareRules.NotConnected);
        return ShareProtection.Tags(_cache.Cached?.Index, _cache.Pending, owner);
    }

    /// <inheritdoc />
    public async Task<RepoSnapshot?> FetchAsync(string login, CancellationToken ct) =>
        await PublicRepoScan.FetchAsync(Client(), login, await ProtectedAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task RemoveAsync(string login, IReadOnlyList<RepoExtra> extras, CancellationToken ct) =>
        await PublicRepoScan.RemoveAsync(Client(), login, extras, await ProtectedAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
}
