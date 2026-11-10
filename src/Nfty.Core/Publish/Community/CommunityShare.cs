using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nfty.Core.Formats;
using Nfty.Core.Output;

namespace Nfty.Core.Publish.Community;

/// <summary>What the person asked to share.</summary>
/// <param name="SetPath">The cooked Set: a folder or a <c>.set</c>.</param>
/// <param name="IncludeCookBook">Ship the source CookBook inside the Set.</param>
/// <param name="CookBookPath">The <c>.cbk</c> to ship when <paramref name="IncludeCookBook"/> is set.</param>
/// <param name="Models">Ship the 3D models.</param>
/// <param name="Name">The share's display name.</param>
/// <param name="Description">A short description.</param>
/// <param name="Tags">Gallery tags.</param>
/// <param name="ReplaceSlug">The share to swap out when the account is at the limit, or null.</param>
/// <param name="ReplaceSharedVersion">The person ticked "Replace the shared version" (needed to update a share).</param>
public sealed record ShareRequest(
    string SetPath, bool IncludeCookBook, string? CookBookPath, bool Models,
    string Name, string Description, IReadOnlyList<string> Tags, string? ReplaceSlug, bool ReplaceSharedVersion);

/// <summary>Where a share run is.</summary>
public enum ShareStage
{
    /// <summary>Reading the person's <c>nfty-public</c>.</summary>
    Scanning,
    /// <summary>Packing the Set.</summary>
    Exporting,
    /// <summary>Checking the packed Set.</summary>
    Checking,
    /// <summary>Building the preview picture.</summary>
    Preview,
    /// <summary>Making the Release and uploading its files.</summary>
    Uploading,
    /// <summary>Writing <c>nfty-public.json</c> and <c>README.md</c>.</summary>
    Listing,
    /// <summary>Opening the pull request.</summary>
    Submitting,
}

/// <summary>A progress report.</summary>
/// <param name="Stage">What is running.</param>
/// <param name="Fraction">0..1 within the stage.</param>
public sealed record ShareProgress(ShareStage Stage, double Fraction);

/// <summary>
/// What is left to do once the gallery merges a pull request: delete the Release (and tag) it made obsolete.
/// </summary>
/// <param name="PullNumber">The pull request.</param>
/// <param name="Slug">The share it concerns.</param>
/// <param name="SupersededTag">The tag of the version an update replaced, or null.</param>
/// <param name="ReplacedTag">The tag of the share a swap or an unshare removed, or null.</param>
public sealed record PendingShare(int PullNumber, string Slug, string? SupersededTag, string? ReplacedTag);

/// <summary>A submitted share.</summary>
/// <param name="PullNumber">The gallery pull request.</param>
/// <param name="PullUrl">Where to read it.</param>
/// <param name="Slug">The share's slug.</param>
/// <param name="Tag">The Release tag that holds its files.</param>
/// <param name="Pending">What <see cref="CommunityShare.FinishAsync"/> does once it merges.</param>
public sealed record ShareResult(int PullNumber, Uri PullUrl, string Slug, string Tag, PendingShare Pending);

/// <summary>
/// The share run (spec 7.2): scan, pack, check, preview, Release, upload, manifest, pull request, and the undo of
/// whatever a failed or canceled run made. UI-free.
/// </summary>
/// <remarks>
/// <para><b>Callers serialize share and remove runs per account.</b> Tags and branches are deleted by name, and the
/// run numbers its Release from the repository it just read, so two runs at once could delete each other's work.</para>
/// <para><b>The HttpClient behind the <see cref="GitHubShareClient"/> must have <c>Timeout = InfiniteTimeSpan</c></b>:
/// a 200 MB upload outlasts the default 100 seconds. The client bounds response bodies itself and the caller's token
/// stops the run.</para>
/// <para><b>Rollback takes back only what this run created</b> (the Release it made, its tag, a branch it made, the repo
/// if it made it) and restores <c>nfty-public.json</c> and <c>README.md</c> to what they held. The replaced share is
/// never touched. Cleanup ignores the caller's token, is bounded to 30 seconds, and never hides the original failure.
/// A repository can only be deleted with a scope sharing does not ask for, so that step is best effort; the run
/// therefore leaves a freshly made repository holding an empty <c>nfty-public.json</c>, one nfty still recognizes.</para>
/// </remarks>
public static class CommunityShare
{
    private const string Repo = PublicRepoScan.RepoName;
    private static readonly TimeSpan CleanupBound = TimeSpan.FromSeconds(30);
    private const int ForkPolls = 10;

    /// <summary>The export the card plans and the run performs: the Asset pack, one archive, never sealed.</summary>
    /// <param name="request">What the person asked for.</param>
    /// <returns>The export options the share uses.</returns>
    public static ExportOptions Options(ShareRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExportOptions.For(ExportPreset.AssetPack) with
        {
            Shape = ExportShape.Archive,
            Sealed = false,
            IncludeCookBook = request.IncludeCookBook,
            Models = request.Models,
        };
    }

    // ---- share -------------------------------------------------------------------------------------------------

    /// <summary>Shares, updates or swaps; see the type's remarks for what the caller owes.</summary>
    /// <param name="client">GitHub.</param>
    /// <param name="user">The signed-in account.</param>
    /// <param name="request">What to share.</param>
    /// <param name="endpoints">Where the gallery lives.</param>
    /// <param name="tempRoot">A folder to work in; the run cleans up what it makes there.</param>
    /// <param name="progress">Progress, in order.</param>
    /// <param name="ct">Stops the run (and rolls it back).</param>
    /// <param name="protectedTags">Release tags the scan must never call extras: what the gallery lists for the account and what any
    /// open pull request names (<see cref="PublicRepoScan.FetchAsync"/>). Empty only when nothing is listed or pending.</param>
    /// <param name="clock">The time stamped on the entry; tests inject one.</param>
    /// <param name="delay">The wait between polls of a new fork; tests inject one.</param>
    /// <returns>The pull request and what to do once it merges.</returns>
    /// <exception cref="CommunityException">Anything that keeps the share out, in <see cref="ShareRules"/>' words.</exception>
    public static async Task<ShareResult> RunAsync(
        GitHubShareClient client, GitHubUser user, ShareRequest request, CommunityEndpoints endpoints, string tempRoot,
        IProgress<ShareProgress>? progress, CancellationToken ct, IReadOnlyCollection<string> protectedTags,
        Func<DateTimeOffset>? clock = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(protectedTags);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempRoot);
        ValidateWords(request);
        clock ??= () => DateTimeOffset.UtcNow;
        delay ??= (span, token) => Task.Delay(span, token);

        string login = user.Login;
        var undo = new Undo();
        string work = Path.Combine(tempRoot, "share-" + Guid.NewGuid().ToString("N"));
        void Report(ShareStage stage, double fraction) => progress?.Report(new ShareProgress(stage, Math.Clamp(fraction, 0, 1)));
        try
        {
            Directory.CreateDirectory(work);

            // 0. The scan: anything not allowed stops the run before a byte is uploaded.
            Report(ShareStage.Scanning, 0);
            var snapshot = await PublicRepoScan.FetchAsync(client, login, protectedTags, ct).ConfigureAwait(false);
            if (snapshot is not null)
            {
                var scan = PublicRepoScan.Scan(snapshot);
                if (scan.Blocker is { } blocker) throw new CommunityException(blocker);
                if (scan.Extras.Count > 0) throw new CommunityException(ShareRules.RepoExtras);
            }
            var shares = snapshot?.Manifest?.Shares ?? [];

            // 1. Pack.
            Report(ShareStage.Exporting, 0);
            var exportProgress = new Inline<ExportProgress>(p => Report(ShareStage.Exporting, p.Fraction));
            var exported = await SetExporter.ExportAsync(request.SetPath, Path.Combine(work, "export"), Options(request),
                request.IncludeCookBook ? request.CookBookPath : null, null, exportProgress, ct).ConfigureAwait(false);

            // 2. Check locally: a refusal costs no network.
            Report(ShareStage.Checking, 0);
            var facts = await ShareCheck.CheckAsync(exported.Path, ct).ConfigureAwait(false);
            if (facts.Problems.Count > 0) throw new CommunityException(facts.Problems[0]);

            string slug = ShareSlug.Of(facts.Collection);
            var same = shares.FirstOrDefault(s => s.Slug == slug);
            CommunityEntry? replaced = null;
            if (same is not null)
            {
                if (!string.Equals(same.Collection, facts.Collection, StringComparison.Ordinal))
                    throw new CommunityException(ShareRules.SlugTaken(same.Name));
                if (!request.ReplaceSharedVersion) throw new CommunityException(ShareRules.ReplaceNotChosen);
            }
            else if (request.ReplaceSlug is not null)
            {
                replaced = shares.FirstOrDefault(s => s.Slug == request.ReplaceSlug)
                    ?? throw new CommunityException(ShareRules.RepoChanged);
            }
            else if (shares.Count >= ShareRules.MaxShares)
            {
                throw new CommunityException(ShareRules.AtShareLimit);
            }

            // 3. Preview.
            Report(ShareStage.Preview, 0);
            string previewPath = Path.Combine(work, "preview.png");
            using (var set = await SetReader.ReadAsync(exported.Path, ct).ConfigureAwait(false))
                await CommunityPreview.WriteAsync(set.Items, previewPath, ct).ConfigureAwait(false);
            if (CommunityPreview.Check(previewPath, facts.Width, facts.Height) is { Count: > 0 } badPreview)
                throw new CommunityException(badPreview[0]);

            // The tag: one past the highest version this slug already has in the repository.
            int n = 1 + NextVersion(snapshot?.Releases, slug);
            string tag = $"{slug}-v{n}";
            string baseUrl = $"https://github.com/{login}/{Repo}/releases/download/{tag}/";
            long setSize = new FileInfo(exported.Path).Length;
            long previewSize = new FileInfo(previewPath).Length;
            string previewSha;
            using (var fs = File.OpenRead(previewPath))
                previewSha = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));

            // 4. Duplicates, against the published list and the gallery's open pull requests (untrusted: used for this only).
            var index = await ReadIndexAsync(client, ct).ConfigureAwait(false);
            // The history the gallery checks dates against is the PUBLISHED list's: a share nfty-public lists but the gallery
            // never took (refused, or removed since) has none, so a fresh share starts its dates over.
            var previous = index.Entries.FirstOrDefault(e => e.Owner.Id == user.Id && e.Slug == slug);
            var now = clock();
            var candidate = new CommunityEntry(
                CommunitySchema.Current, slug, new CommunityOwner(user.Id, login), $"{login}/{Repo}", tag,
                request.Name, facts.Collection, request.Description, [.. request.Tags],
                new CommunityCanvas(facts.Width, facts.Height), facts.Assets, facts.Models, facts.IncludesCookBook,
                facts.CookbookSha256,
                new CommunityFile(baseUrl + slug + ".set", setSize, facts.FileSha256),
                new CommunityFile(baseUrl + PublicRepoScan.PreviewAsset, previewSize, previewSha),
                facts.Fingerprint, NftyVersion(), previous?.Created ?? now, now);
            var open = await client.ListOpenPullEntriesAsync(ct).ConfigureAwait(false);
            if (CommunityDuplicates.Problem(candidate, index.Entries.Concat(open)) is { } duplicate)
                throw new CommunityException(duplicate);

            // 5. The repository: create on the first share, otherwise use the one the scan just read.
            Report(ShareStage.Uploading, 0);
            bool created = snapshot is null;
            GitHubRepo repo;
            if (created)
            {
                repo = await client.CreateRepoAsync(Repo, "Collections shared from nfty", ct).ConfigureAwait(false);
                undo.Add(token => client.DeleteRepoAsync(login, Repo, token));
            }
            else
            {
                repo = snapshot!.Repo;
            }
            string branch = repo.DefaultBranch;

            // 6. The Release and its two files.
            GitHubRelease release;
            long total = setSize + previewSize, sentBefore = 0;
            undo.Add(token => TakeBackRelease(client, login, tag, token));
            release = await client.CreateReleaseAsync(login, Repo, tag, $"{request.Name} v{n}", ct).ConfigureAwait(false);
            foreach (var (name, path, size) in new[] { (slug + ".set", exported.Path, setSize), (PublicRepoScan.PreviewAsset, previewPath, previewSize) })
            {
                long before = sentBefore;
                using var content = File.OpenRead(path);
                await client.UploadAssetAsync(release, name, content, size,
                    new Inline<long>(sent => Report(ShareStage.Uploading, (double)(before + sent) / total)), ct).ConfigureAwait(false);
                sentBefore += size;
            }
            ct.ThrowIfCancellationRequested();

            // 7. nfty-public.json and README.md, with the new share in and a swapped one out.
            Report(ShareStage.Listing, 0);
            var next = shares.Where(s => s.Slug != replaced?.Slug).ToList();
            int at = next.FindIndex(s => s.Slug == slug);
            if (at >= 0) next[at] = candidate; else next.Add(candidate);
            var emptyManifest = PublicRepoManifest.Write(new PublicRepoManifest(CommunitySchema.Current, []));
            await WriteFileAsync(client, login, branch, PublicRepoScan.ManifestFile,
                PublicRepoManifest.Write(new PublicRepoManifest(CommunitySchema.Current, next)),
                created ? emptyManifest : null, undo, ct).ConfigureAwait(false);
            await WriteFileAsync(client, login, branch, PublicRepoScan.ReadmeFile,
                Encoding.UTF8.GetBytes(Readme(next)), null, undo, ct).ConfigureAwait(false);

            // 8. Fork, branch, entry file (and the swapped share's deletion), pull request.
            Report(ShareStage.Submitting, 0);
            var (forkOwner, forkName, _) = await ForkAsync(client, login, endpoints, delay, ct).ConfigureAwait(false);
            string gallery = endpoints.GalleryRepo;
            string prBranch = $"share/{slug}-v{n}";
            await MakeBranchAsync(client, endpoints, forkOwner, forkName, prBranch, undo, ct).ConfigureAwait(false);

            string entryPath = EntryPath(user.Id, slug);
            var onBranch = await client.GetFileAsync(forkOwner, forkName, entryPath, prBranch, ct).ConfigureAwait(false);
            await client.PutFileAsync(forkOwner, forkName, entryPath,
                JsonSerializer.SerializeToUtf8Bytes(candidate, Json.Options),
                same is not null ? $"Update {request.Name}" : $"Add {request.Name}", onBranch?.Sha, prBranch, ct).ConfigureAwait(false);
            if (replaced is not null)
            {
                string oldPath = EntryPath(user.Id, replaced.Slug);
                if (await client.GetFileAsync(forkOwner, forkName, oldPath, prBranch, ct).ConfigureAwait(false) is { } old)
                    await client.DeleteFileAsync(forkOwner, forkName, oldPath, old.Sha, $"Remove {replaced.Name}", prBranch, ct).ConfigureAwait(false);
            }

            string title = (same is not null ? "Update " : "Add ") + request.Name;
            string body = $"{(same is not null ? "Update" : "Share")} \"{request.Name}\" ({facts.Assets} assets, {facts.Width} x {facts.Height}) from @{login}."
                + (replaced is not null ? $" Replaces \"{replaced.Name}\"." : "");
            int number = await client.OpenPullAsync(gallery, $"{forkOwner}:{prBranch}", title, body, ct).ConfigureAwait(false);
            Report(ShareStage.Submitting, 1);
            return new ShareResult(number, PullUrl(gallery, number), slug, tag,
                new PendingShare(number, slug, same?.Release, replaced?.Release));
        }
        catch
        {
            await undo.RunAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            TryDelete(work);
        }
    }

    // ---- after the merge -----------------------------------------------------------------------------------------

    /// <summary>
    /// Deletes the Release and tag a merged pull request made obsolete. Does nothing while the pull request is not
    /// merged, and never touches a tag some share in <c>nfty-public.json</c> still lists.
    /// </summary>
    /// <param name="client">GitHub.</param>
    /// <param name="user">The signed-in account.</param>
    /// <param name="pending">What the run returned.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="endpoints">Where the gallery lives; the shipped one when omitted.</param>
    public static async Task FinishAsync(GitHubShareClient client, GitHubUser user, PendingShare pending, CancellationToken ct,
        CommunityEndpoints? endpoints = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(pending);
        var tags = new[] { pending.SupersededTag, pending.ReplacedTag }.Where(t => t is not null).Select(t => t!)
            .Distinct(StringComparer.Ordinal).ToList();
        if (tags.Count == 0) return;

        var pull = await client.GetPullAsync((endpoints ?? CommunityEndpoints.Default).GalleryRepo, pending.PullNumber, ct).ConfigureAwait(false);
        if (!pull.Merged) return;

        var repo = await client.GetRepoAsync(user.Login, Repo, ct).ConfigureAwait(false);
        if (repo is null) return;
        var live = new HashSet<string>(StringComparer.Ordinal);
        if (await client.GetFileAsync(user.Login, Repo, PublicRepoScan.ManifestFile, repo.DefaultBranch, ct).ConfigureAwait(false) is { } file)
        {
            try { foreach (var s in PublicRepoManifest.Read(new MemoryStream(file.Content)).Shares) live.Add(s.Release); }
            catch (InvalidDataException) { return; }
            catch (UnsupportedSchemaVersionException) { return; }
        }

        var releases = await client.ListReleasesAsync(user.Login, Repo, ct).ConfigureAwait(false);
        foreach (var tag in tags.Where(t => !live.Contains(t)))
        {
            foreach (var release in releases.Where(r => r.Tag == tag))
                await client.DeleteReleaseAsync(user.Login, Repo, release.Id, ct).ConfigureAwait(false);
            if ((await client.ListTagsAsync(user.Login, Repo, ct).ConfigureAwait(false)).Contains(tag))
                await client.DeleteTagAsync(user.Login, Repo, tag, ct).ConfigureAwait(false);
        }
    }

    // ---- unshare -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Opens a pull request that deletes one of the account's entries, and takes the share out of
    /// <c>nfty-public.json</c> and <c>README.md</c>. The repository is never deleted, and the Release stays until the
    /// pull request merges (<see cref="FinishAsync"/> with <c>ReplacedTag</c> set to the share's tag).
    /// </summary>
    /// <param name="client">GitHub.</param>
    /// <param name="user">The signed-in account.</param>
    /// <param name="slug">The share to remove.</param>
    /// <param name="endpoints">Where the gallery lives.</param>
    /// <param name="ct">Cancellation (rolls back).</param>
    /// <param name="delay">The wait between polls of a new fork; tests inject one.</param>
    /// <returns>The pull request number.</returns>
    public static async Task<int> UnshareAsync(GitHubShareClient client, GitHubUser user, string slug, CommunityEndpoints endpoints,
        CancellationToken ct, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(endpoints);
        delay ??= (span, token) => Task.Delay(span, token);
        string login = user.Login;
        var undo = new Undo();
        try
        {
            var repo = await client.GetRepoAsync(login, Repo, ct).ConfigureAwait(false) ?? throw new CommunityException(ShareRules.RepoChanged);
            var file = await client.GetFileAsync(login, Repo, PublicRepoScan.ManifestFile, repo.DefaultBranch, ct).ConfigureAwait(false)
                ?? throw new CommunityException(ShareRules.RepoNotOurs);
            PublicRepoManifest manifest;
            try { manifest = PublicRepoManifest.Read(new MemoryStream(file.Content)); }
            catch (InvalidDataException) { throw new CommunityException(ShareRules.RepoNotOurs); }
            catch (UnsupportedSchemaVersionException ex)
            {
                throw new CommunityException(ex.Found > ex.Supported ? ShareRules.RepoNewerNfty : ShareRules.RepoNotOurs);
            }
            var share = manifest.Shares.FirstOrDefault(s => s.Slug == slug) ?? throw new CommunityException(ShareRules.RepoChanged);

            var (forkOwner, forkName, _) = await ForkAsync(client, login, endpoints, delay, ct).ConfigureAwait(false);
            string branch = $"unshare/{slug}";
            await MakeBranchAsync(client, endpoints, forkOwner, forkName, branch, undo, ct).ConfigureAwait(false);
            string path = EntryPath(user.Id, slug);
            var entry = await client.GetFileAsync(forkOwner, forkName, path, branch, ct).ConfigureAwait(false)
                ?? throw new CommunityException(ShareRules.RepoChanged);
            await client.DeleteFileAsync(forkOwner, forkName, path, entry.Sha, $"Remove {share.Name}", branch, ct).ConfigureAwait(false);

            var remaining = manifest.Shares.Where(s => s.Slug != slug).ToList();
            await WriteFileAsync(client, login, repo.DefaultBranch, PublicRepoScan.ManifestFile,
                PublicRepoManifest.Write(new PublicRepoManifest(CommunitySchema.Current, remaining)), null, undo, ct).ConfigureAwait(false);
            await WriteFileAsync(client, login, repo.DefaultBranch, PublicRepoScan.ReadmeFile,
                Encoding.UTF8.GetBytes(Readme(remaining)), null, undo, ct).ConfigureAwait(false);

            return await client.OpenPullAsync(endpoints.GalleryRepo, $"{forkOwner}:{branch}", $"Remove {share.Name}",
                $"Remove \"{share.Name}\" shared by @{login}.", ct).ConfigureAwait(false);
        }
        catch
        {
            await undo.RunAsync().ConfigureAwait(false);
            throw;
        }
    }

    // ---- pieces --------------------------------------------------------------------------------------------------

    private static void ValidateWords(ShareRequest r)
    {
        if (ListingProblem(r.Name, r.Description, r.Tags) is { } problem) throw new CommunityException(problem);
    }

    /// <summary>The first thing wrong with a listing's words, in <see cref="ShareRules"/>' sentences, or null when they are fine.</summary>
    /// <param name="name">The listing's name.</param>
    /// <param name="description">The listing's description, or null for none.</param>
    /// <param name="tags">The listing's tags, or null for none.</param>
    /// <returns>The sentence, or null.</returns>
    /// <remarks>One rule for the run and for the share card, so the card cannot enable Share for words the run refuses.</remarks>
    public static string? ListingProblem(string? name, string? description, IReadOnlyList<string>? tags)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > ShareRules.MaxNameLength) return ShareRules.NameLength;
        if ((description?.Length ?? 0) > ShareRules.MaxDescriptionLength) return ShareRules.DescriptionLength;
        if (HasLink(name) || HasLink(description ?? "")) return ShareRules.NoLinks;
        if ((tags?.Count ?? 0) > ShareRules.MaxTags) return ShareRules.TooManyTags;
        return null;
    }

    private static bool HasLink(string s) =>
        s.Contains("://", StringComparison.Ordinal) || s.Contains("www.", StringComparison.OrdinalIgnoreCase);

    private static int NextVersion(IReadOnlyList<GitHubRelease>? releases, string slug)
    {
        var pattern = new Regex("^" + Regex.Escape(slug) + @"-v(\d+)$", RegexOptions.CultureInvariant);
        int highest = 0;
        foreach (var r in releases ?? [])
            if (pattern.Match(r.Tag) is { Success: true } m && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int v))
                highest = Math.Max(highest, v);
        return highest;
    }

    private static async Task<CommunityIndex> ReadIndexAsync(GitHubShareClient client, CancellationToken ct)
    {
        try { return await client.GetIndexAsync(ct).ConfigureAwait(false); }
        catch (InvalidDataException) { throw new CommunityException(ShareRules.ListDamaged); }
        catch (UnsupportedSchemaVersionException ex)
        {
            throw new CommunityException(ex.Found > ex.Supported ? ShareRules.IndexTooNew : ShareRules.ListTooOld(ex.Found));
        }
    }

    private static string EntryPath(long ownerId, string slug) =>
        $"entries/{ownerId.ToString(CultureInfo.InvariantCulture)}/{slug}.json";

    private static Uri PullUrl(string galleryRepo, int number) =>
        new($"https://github.com/{galleryRepo}/pull/{number.ToString(CultureInfo.InvariantCulture)}");

    private static string NftyVersion() =>
        typeof(CommunityShare).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static async Task<(string Owner, string Name, GitHubRepo Repo)> ForkAsync(
        GitHubShareClient client, string login, CommunityEndpoints endpoints, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        var fork = await client.ForkAsync(endpoints.GalleryRepo, ct).ConfigureAwait(false);
        var parts = fork.FullName.Split('/');
        if (parts.Length != 2) throw new CommunityException(ShareRules.GitHubUnexpected);
        // A new fork may not be readable for a moment.
        for (int i = 0; i < ForkPolls; i++)
        {
            if (await client.GetRepoAsync(parts[0], parts[1], ct).ConfigureAwait(false) is { } ready) return (parts[0], parts[1], ready);
            await delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
        throw new CommunityException(ShareRules.TimedOut);
    }

    // A branch off the gallery's current default branch. A leftover of the same name (an earlier run's, in this run's
    // own share/ or unshare/ namespace) is deleted first so it can't carry stale files into the request.
    private static async Task MakeBranchAsync(GitHubShareClient client, CommunityEndpoints endpoints, string forkOwner, string forkName,
        string branch, Undo undo, CancellationToken ct)
    {
        var galleryParts = endpoints.GalleryRepo.Split('/');
        var gallery = await client.GetRepoAsync(galleryParts[0], galleryParts[1], ct).ConfigureAwait(false)
            ?? throw new CommunityException(ShareRules.GitHubRefused);
        string sha = await client.GetBranchShaAsync(galleryParts[0], galleryParts[1], gallery.DefaultBranch, ct).ConfigureAwait(false);
        if ((await client.ListBranchesAsync(forkOwner, forkName, ct).ConfigureAwait(false)).Contains(branch))
            await client.DeleteBranchAsync(forkOwner, forkName, branch, ct).ConfigureAwait(false);
        undo.Add(token => client.DeleteBranchAsync(forkOwner, forkName, branch, token));
        await client.CreateBranchAsync(forkOwner, forkName, branch, sha, ct).ConfigureAwait(false);
    }

    // Puts a file, registering its restore BEFORE the write (a write that throws may still have landed). `fallback` is what
    // the file should hold again when it did not exist before (null = deleted).
    private static async Task WriteFileAsync(GitHubShareClient client, string login, string branch, string path, byte[] content,
        byte[]? fallback, Undo undo, CancellationToken ct)
    {
        var current = await client.GetFileAsync(login, Repo, path, branch, ct).ConfigureAwait(false);
        byte[]? previous = current?.Content ?? fallback;
        undo.Add(token => RestoreFileAsync(client, login, branch, path, previous, token));
        await client.PutFileAsync(login, Repo, path, content, $"Update {path}", current?.Sha, branch, ct).ConfigureAwait(false);
    }

    private static async Task RestoreFileAsync(GitHubShareClient client, string login, string branch, string path, byte[]? previous, CancellationToken ct)
    {
        var now = await client.GetFileAsync(login, Repo, path, branch, ct).ConfigureAwait(false);
        if (previous is null)
        {
            if (now is { } gone) await client.DeleteFileAsync(login, Repo, path, gone.Sha, $"Restore {path}", branch, ct).ConfigureAwait(false);
        }
        else if (now is null || !now.Value.Content.AsSpan().SequenceEqual(previous))
        {
            await client.PutFileAsync(login, Repo, path, previous, $"Restore {path}", now?.Sha, branch, ct).ConfigureAwait(false);
        }
    }

    // The tag is new (one past every version the repository had), so any Release on it was made by this run, even
    // when the call that made it threw after GitHub had acted.
    private static async Task TakeBackRelease(GitHubShareClient client, string login, string tag, CancellationToken ct)
    {
        foreach (var r in (await client.ListReleasesAsync(login, Repo, ct).ConfigureAwait(false)).Where(r => r.Tag == tag))
            await client.DeleteReleaseAsync(login, Repo, r.Id, ct).ConfigureAwait(false);
        if ((await client.ListTagsAsync(login, Repo, ct).ConfigureAwait(false)).Contains(tag))
            await client.DeleteTagAsync(login, Repo, tag, ct).ConfigureAwait(false);
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The README nfty writes: every share's name, description and links, as plain markdown.</summary>
    internal static string Readme(IReadOnlyList<CommunityEntry> shares)
    {
        var sb = new StringBuilder("# Shared with nfty\n\nCollections shared from nfty to the community gallery.\n");
        foreach (var s in shares)
        {
            sb.Append("\n## ").Append(Md(s.Name)).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(s.Description)) sb.Append(Md(s.Description)).Append("\n\n");
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"{s.Assets} assets, {s.Canvas.Width} x {s.Canvas.Height}\n\n"));
            sb.Append("- [Preview](").Append(s.Preview.Url).Append(")\n");
            sb.Append("- [Download](").Append(s.Set.Url).Append(")\n");
        }
        return sb.ToString();
    }

    private static string Md(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c is '\r' or '\n') { sb.Append(' '); continue; }
            if ("\\`*_{}[]()#+-.!|<>~".Contains(c)) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private sealed class Inline<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    // Compensations run newest first, each on its own so one failing never stops the rest or replaces the original error.
    private sealed class Undo
    {
        private readonly List<Func<CancellationToken, Task>> _steps = [];

        public void Add(Func<CancellationToken, Task> step) => _steps.Add(step);

        public async Task RunAsync()
        {
            if (_steps.Count == 0) return;
            using var bound = new CancellationTokenSource(CleanupBound);
            for (int i = _steps.Count - 1; i >= 0; i--)
            {
                try { await _steps[i](bound.Token).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }
    }
}
