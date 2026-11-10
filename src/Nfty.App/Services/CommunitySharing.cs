using System.Globalization;
using Nfty.Core.Publish.Community;

namespace Nfty.App.Services;

/// <summary>What the share card knows about the Set that the run's result does not carry, kept with the pending record so
/// the page can draw a tile for a share the gallery does not list yet.</summary>
/// <param name="Name">The listing's name.</param>
/// <param name="Assets">How many assets the Set holds.</param>
/// <param name="Width">The canvas width.</param>
/// <param name="Height">The canvas height.</param>
/// <param name="Models">Whether the Set carries 3D models.</param>
/// <param name="IncludesCookBook">Whether the CookBook ships inside.</param>
/// <param name="SetBytes">The packed Set's size.</param>
/// <param name="ListedAtSubmit">Whether the gallery already lists this share (an update), so its existing tile stands for it.</param>
public sealed record ShareRunInfo(string Name, int Assets, int Width, int Height, bool Models, bool IncludesCookBook,
    long SetBytes, bool ListedAtSubmit);

/// <summary>GitHub, as the share run, the unshare and the page's pending check use it. A seam so tests script every outcome and
/// nothing reaches the network.</summary>
public interface IShareBackend
{
    /// <summary>Shares, updates or swaps.</summary>
    /// <param name="request">What to share.</param>
    /// <param name="pendingSupersededTag">A superseded Release whose replacing pull request is still open, or null.</param>
    /// <param name="progress">Progress, in order.</param>
    /// <param name="ct">Stops the run and rolls it back.</param>
    /// <returns>The pull request and what to do once it merges.</returns>
    Task<ShareResult> ShareAsync(ShareRequest request, string? pendingSupersededTag, IProgress<ShareProgress>? progress, CancellationToken ct);

    /// <summary>Opens a pull request deleting one of the account's entries.</summary>
    /// <param name="slug">The share.</param>
    /// <param name="ct">Stops the run and rolls it back.</param>
    /// <returns>The pull request number.</returns>
    Task<int> UnshareAsync(string slug, CancellationToken ct);

    /// <summary>A gallery pull request's state.</summary>
    /// <param name="number">The pull request.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GitHubPull> GetPullAsync(int number, CancellationToken ct);

    /// <summary>The text of a pull request's comments, where the gallery's checks answer.</summary>
    /// <param name="number">The pull request.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<string>> GetCommentsAsync(int number, CancellationToken ct);

    /// <summary>Deletes what a merged pull request made obsolete.</summary>
    /// <param name="pending">What the run returned.</param>
    /// <param name="ct">Cancellation.</param>
    Task FinishAsync(PendingShare pending, CancellationToken ct);

    /// <summary>The address of a gallery pull request.</summary>
    /// <param name="number">The pull request.</param>
    Uri PullUrl(int number);
}

/// <summary>The real backend: the connected account's client and <see cref="CommunityShare"/>.</summary>
public sealed class GitHubShareBackend : IShareBackend
{
    private readonly IGitHubAccount _account;
    private readonly CommunityEndpoints _endpoints;
    private readonly string _tempRoot;

    /// <summary>Makes the backend.</summary>
    /// <param name="account">The GitHub connection; its client is asked for on every call, so a reconnect is picked up.</param>
    /// <param name="endpoints">Where the gallery lives; the shipped one when null.</param>
    /// <param name="tempRoot">Where the run packs the Set; the system temporary folder when null.</param>
    public GitHubShareBackend(IGitHubAccount account, CommunityEndpoints? endpoints = null, string? tempRoot = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        _account = account;
        _endpoints = endpoints ?? CommunityEndpoints.Default;
        _tempRoot = tempRoot ?? Path.GetTempPath();
    }

    private GitHubShareClient Client() => _account.Client() ?? throw new CommunityException(ShareRules.NotConnected);

    /// <summary>The account as the calls that do not read its age need it: id and login.</summary>
    private async Task<GitHubUser> UserAsync(CancellationToken ct)
    {
        long id = await _account.EnsureUserIdAsync(ct).ConfigureAwait(false) ?? throw new CommunityException(ShareRules.NotConnected);
        return new GitHubUser(id, _account.Login ?? throw new CommunityException(ShareRules.NotConnected), DateTimeOffset.MinValue);
    }

    /// <inheritdoc />
    public async Task<ShareResult> ShareAsync(ShareRequest request, string? pendingSupersededTag, IProgress<ShareProgress>? progress,
        CancellationToken ct)
    {
        var client = Client();
        // The share run checks the account's age, so it reads the whole account rather than the cached id.
        var user = await client.GetViewerAsync(ct).ConfigureAwait(false);
        return await CommunityShare.RunAsync(client, user, request, _endpoints, _tempRoot, progress, ct, pendingSupersededTag)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> UnshareAsync(string slug, CancellationToken ct) =>
        await CommunityShare.UnshareAsync(Client(), await UserAsync(ct).ConfigureAwait(false), slug, _endpoints, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<GitHubPull> GetPullAsync(int number, CancellationToken ct) => Client().GetPullAsync(_endpoints.GalleryRepo, number, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> GetCommentsAsync(int number, CancellationToken ct) =>
        Client().GetPullCommentsAsync(_endpoints.GalleryRepo, number, ct);

    /// <inheritdoc />
    public async Task FinishAsync(PendingShare pending, CancellationToken ct) =>
        await CommunityShare.FinishAsync(Client(), await UserAsync(ct).ConfigureAwait(false), pending, ct, _endpoints).ConfigureAwait(false);

    /// <inheritdoc />
    public Uri PullUrl(int number) =>
        new(string.Create(CultureInfo.InvariantCulture, $"https://github.com/{_endpoints.GalleryRepo}/pull/{number}"));
}

/// <summary>
/// The app's one door to sharing and unsharing, and the keeper of what is still pending.
/// </summary>
/// <remarks>
/// <b>Share and unshare runs are serialized app-wide</b> (one <see cref="SemaphoreSlim"/>): <see cref="CommunityShare"/> deletes tags
/// and branches by name and numbers its Release from what it just read, so two runs at once could delete each other's work. A second
/// run waits its turn (and can be stopped while it waits); the page's pending check never waits, it skips and tries again at the next
/// refresh.
/// </remarks>
public interface ICommunitySharing
{
    /// <summary>The shares and removals still waiting for the gallery.</summary>
    IReadOnlyList<PendingRecord> Pending { get; }

    /// <summary>Raised after <see cref="Pending"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Shares, updates or swaps, and remembers the pull request as pending.</summary>
    /// <param name="request">What to share.</param>
    /// <param name="info">What the card knows about the Set, for the pending tile.</param>
    /// <param name="progress">Progress, in order.</param>
    /// <param name="ct">Stops the run (and rolls it back).</param>
    /// <returns>The pull request and what to do once it merges.</returns>
    Task<ShareResult> ShareAsync(ShareRequest request, ShareRunInfo info, IProgress<ShareProgress>? progress, CancellationToken ct);

    /// <summary>Opens a pull request that takes one of the account's entries out of the gallery, and remembers it as pending.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="ct">Stops the run (and rolls it back).</param>
    /// <returns>The pending record, whose address is the pull request.</returns>
    Task<PendingRecord> UnshareAsync(CommunityEntry entry, CancellationToken ct);

    /// <summary>Looks at every pending pull request: finishes the merged ones (once), notes why a closed one was turned away, and drops
    /// what the list now shows. Never throws for GitHub being unreachable; whatever fails is tried again next time.</summary>
    /// <param name="index">The gallery's current list, or null when there is none.</param>
    /// <param name="ct">Cancellation.</param>
    Task CheckPendingAsync(CommunityIndex? index, CancellationToken ct);

    /// <summary>Forgets a pending record, which is how a closed one is dismissed.</summary>
    /// <param name="record">The record.</param>
    void Dismiss(PendingRecord record);
}

/// <inheritdoc cref="ICommunitySharing"/>
public sealed class CommunitySharing : ICommunitySharing
{
    private const int MaxReasonLines = 8;
    private const int MaxReasonChars = 300;

    private readonly IShareBackend _backend;
    private readonly ICommunityCache _cache;
    private readonly IGitHubAccount _account;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Makes the service.</summary>
    /// <param name="backend">GitHub.</param>
    /// <param name="cache">Where <c>pending.json</c> lives.</param>
    /// <param name="account">The GitHub connection, whose login and id the records carry.</param>
    /// <param name="clock">The time stamped on a record; tests inject one.</param>
    public CommunitySharing(IShareBackend backend, ICommunityCache cache, IGitHubAccount account, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(account);
        _backend = backend;
        _cache = cache;
        _account = account;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public IReadOnlyList<PendingRecord> Pending => _cache.Pending;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public async Task<ShareResult> ShareAsync(ShareRequest request, ShareRunInfo info, IProgress<ShareProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(info);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            long owner = await _account.EnsureUserIdAsync(ct).ConfigureAwait(false) ?? throw new CommunityException(ShareRules.NotConnected);
            string login = _account.Login ?? throw new CommunityException(ShareRules.NotConnected);
            // An update whose predecessor's Release is still waiting on an earlier pull request: the scan must allow it.
            string? waiting = Pending.Where(p => !p.IsUnshare && !p.Finished && !p.Closed && p.SupersededTag is not null)
                .Select(p => p.SupersededTag).FirstOrDefault();

            var result = await _backend.ShareAsync(request, waiting, progress, ct).ConfigureAwait(false);

            Add(new PendingRecord(PendingRecord.ShareKind, result.PullNumber, result.PullUrl.ToString(), result.Slug, owner, login,
                info.Name, info.Assets, info.Width, info.Height, info.Models, info.IncludesCookBook, info.SetBytes,
                result.Pending.SupersededTag, result.Pending.ReplacedTag, info.ListedAtSubmit, false, _clock(), false, []));
            return result;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<PendingRecord> UnshareAsync(CommunityEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string login = _account.Login ?? throw new CommunityException(ShareRules.NotConnected);
            int number = await _backend.UnshareAsync(entry.Slug, ct).ConfigureAwait(false);
            var record = new PendingRecord(PendingRecord.UnshareKind, number, _backend.PullUrl(number).ToString(), entry.Slug,
                entry.Owner.Id, login, entry.Name, entry.Assets, entry.Canvas.Width, entry.Canvas.Height, entry.Models,
                entry.IncludesCookBook, entry.Set.Size, null, entry.Release.Length > 0 ? entry.Release : null, true, false, _clock(), false, []);
            Add(record);
            return record;
        }
        finally { _gate.Release(); }
    }

    private void Add(PendingRecord record)
    {
        Save(Pending.Where(p => p.PullNumber != record.PullNumber).Append(record).ToList());
    }

    private void Save(List<PendingRecord> records)
    {
        _cache.SavePending(records);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async Task CheckPendingAsync(CommunityIndex? index, CancellationToken ct)
    {
        if (Pending.Count == 0) return;
        // A share or removal is running: its own tags and branches are in play, so the check waits for the next refresh.
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            var ids = new HashSet<string>(index?.Entries.Select(e => e.Id) ?? [], StringComparer.Ordinal);
            var next = new List<PendingRecord>();
            bool changed = false;
            foreach (var record in Pending.ToList())
            {
                var updated = await CheckOneAsync(record, ids, index is not null, ct).ConfigureAwait(false);
                if (updated is null) { changed = true; continue; }
                changed |= !ReferenceEquals(updated, record);
                next.Add(updated);
            }
            if (changed) Save(next);
        }
        finally { _gate.Release(); }
    }

    /// <summary>One record's next state; null when it is done with. GitHub trouble keeps the record as it is.</summary>
    private async Task<PendingRecord?> CheckOneAsync(PendingRecord record, HashSet<string> listed, bool haveList, CancellationToken ct)
    {
        // Another account's pull request is not this account's to finish.
        if (!string.Equals(record.Login, _account.Login, StringComparison.OrdinalIgnoreCase) || record.Closed) return record;
        try
        {
            if (!record.Finished)
            {
                var pull = await _backend.GetPullAsync(record.PullNumber, ct).ConfigureAwait(false);
                if (pull.Merged)
                {
                    await _backend.FinishAsync(record.Finishing, ct).ConfigureAwait(false);
                    record = record with { Finished = true };
                }
                else if (pull.State == "closed")
                {
                    var comments = await _backend.GetCommentsAsync(record.PullNumber, ct).ConfigureAwait(false);
                    return record with { Closed = true, ClosedReasons = ReasonLines(comments) };
                }
                else return record;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // GitHub being slow, refusing or gone never blocks browsing; this is tried again at the next refresh.
            return record;
        }

        // Merged and finished: the tile goes when the list agrees (a new share is listed, a removed one is not), or at once for an
        // update, whose old tile is already the right one.
        if (!haveList) return record;
        bool done = record.IsUnshare ? !listed.Contains(record.Id) : record.ListedAtSubmit || listed.Contains(record.Id);
        return done ? null : record;
    }

    /// <summary>The gallery's comment as reasons: one line each, markdown bullets and markers taken off.</summary>
    internal static IReadOnlyList<string> ReasonLines(IEnumerable<string> comments)
    {
        var lines = new List<string>();
        foreach (var comment in comments)
        {
            foreach (var raw in comment.Split('\n'))
            {
                var line = raw.Trim().TrimStart('-', '*', '•', '+', '>', ' ', '\t');
                // A numbered list ("1. ", "2) ").
                int i = 0;
                while (i < line.Length && char.IsAsciiDigit(line[i])) i++;
                if (i > 0 && i < line.Length - 1 && line[i] is '.' or ')' && line[i + 1] == ' ') line = line[(i + 1)..].TrimStart();
                line = line.Replace("**", "").Replace("`", "").Trim();
                if (line.Length == 0 || line.StartsWith("<!--", StringComparison.Ordinal)) continue;
                if (line.Length > MaxReasonChars) line = line[..(MaxReasonChars - 1)] + "…";
                if (!lines.Contains(line, StringComparer.Ordinal)) lines.Add(line);
                if (lines.Count == MaxReasonLines) return lines;
            }
        }
        return lines;
    }

    /// <inheritdoc />
    public void Dismiss(PendingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Save(Pending.Where(p => p.PullNumber != record.PullNumber).ToList());
    }
}
