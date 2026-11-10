using System.Text.RegularExpressions;
using Nfty.Core.Formats;

namespace Nfty.Core.Publish.Community;

/// <summary>Everything the scan reads about a user's <c>nfty-public</c> repository.</summary>
/// <param name="Repo">The repository.</param>
/// <param name="Tree">The default branch's tree, recursive.</param>
/// <param name="Branches">Every branch name.</param>
/// <param name="Releases">Every release with its files.</param>
/// <param name="Tags">Every tag name.</param>
/// <param name="Manifest">The repository's <c>nfty-public.json</c>, or null when it is missing or not one nfty wrote.</param>
/// <param name="ProtectedTags">Release tags the gallery or an open pull request still depends on, which are never extras
/// even when <c>nfty-public.json</c> does not list them (the live version while an update is open, the new one while it is).</param>
public sealed record RepoSnapshot(
    GitHubRepo Repo, IReadOnlyList<GitHubTreeItem> Tree, IReadOnlyList<string> Branches,
    IReadOnlyList<GitHubRelease> Releases, IReadOnlyList<string> Tags,
    PublicRepoManifest? Manifest, IReadOnlyCollection<string> ProtectedTags);

/// <summary>What kind of thing the repository holds that the gallery does not allow.</summary>
public enum RepoExtraKind
{
    /// <summary>A file or folder on the default branch (a folder is one item, named by its top-level path).</summary>
    File,
    /// <summary>A branch other than the default one.</summary>
    Branch,
    /// <summary>A Release no share lists.</summary>
    Release,
    /// <summary>A file attached to an allowed Release that is not <c>&lt;slug&gt;.set</c> or <c>preview.png</c>.</summary>
    Asset,
    /// <summary>A tag that belongs to no allowed Release.</summary>
    Tag,
}

/// <summary>One thing the repository holds that the gallery does not allow.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Name">The path, branch name, tag, or asset file name.</param>
/// <param name="Size">Its size in bytes when known (a folder or Release: the sum of its files).</param>
/// <param name="Id">The Release id (a Release) or asset id (an asset); null for the rest.</param>
/// <param name="Removable">False for what nfty cannot delete through GitHub's API (a submodule); it is still listed.</param>
public sealed record RepoExtra(RepoExtraKind Kind, string Name, long? Size, long? Id, bool Removable = true);

/// <summary>The scan's answer.</summary>
/// <param name="Extras">Everything not allowed, for display and for <see cref="PublicRepoScan.RemoveAsync"/>.</param>
/// <param name="Blocker">The sentence that stops sharing outright (not ours, private, history too large), or null. Extras are still listed.</param>
public sealed record RepoScan(IReadOnlyList<RepoExtra> Extras, string? Blocker);

/// <summary>
/// The scan before every share (spec 3.3). <see cref="Scan"/> is pure over a fetched <see cref="RepoSnapshot"/> and never
/// throws; <see cref="RemoveAsync"/> deletes exactly the extras it is given, and only ones a fresh scan still names.
/// </summary>
public static partial class PublicRepoScan
{
    /// <summary>The repository's name.</summary>
    public const string RepoName = "nfty-public";
    /// <summary>The manifest file at its root.</summary>
    public const string ManifestFile = "nfty-public.json";
    /// <summary>The only other file allowed at its root.</summary>
    public const string ReadmeFile = "README.md";
    /// <summary>The preview asset every Release holds.</summary>
    public const string PreviewAsset = "preview.png";

    /// <summary>The most protected tags one scan honors: three shares, each with a live and a pending version, with room to spare.</summary>
    public const int MaxProtectedTags = ShareRules.MaxShares * 4;

    [GeneratedRegex(@"-v\d+$")]
    private static partial Regex VersionSuffix();

    /// <summary>Whether a Release tag has the shape <c>&lt;slug&gt;-v&lt;N&gt;</c> every share's tag has.</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="slug">The share's slug.</param>
    /// <returns>True when it is exactly that.</returns>
    internal static bool IsVersionTag(string tag, string slug) =>
        tag.StartsWith(slug + "-v", StringComparison.Ordinal) && tag.Length > slug.Length + 2
        && tag.AsSpan(slug.Length + 2).IndexOfAnyExceptInRange('0', '9') < 0;

    /// <summary>Applies the 3.3 table to a snapshot.</summary>
    /// <param name="snapshot">What was read from the repository.</param>
    public static RepoScan Scan(RepoSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var extras = new List<RepoExtra>();

        // Tree: README.md and nfty-public.json at the root, nothing else. A folder is reported once by its top-level name.
        var folders = new SortedDictionary<string, (long? Size, bool Removable)>(StringComparer.Ordinal);
        foreach (var item in snapshot.Tree)
        {
            if (item.Type == "blob" && (item.Path == ReadmeFile || item.Path == ManifestFile)) continue;
            int slash = item.Path.IndexOf('/', StringComparison.Ordinal);
            string top = slash < 0 ? item.Path : item.Path[..slash];
            if (!folders.TryGetValue(top, out var seen)) seen = (null, true);
            // Only blobs carry a size; a folder's size is the sum of its blobs'. A submodule (type commit,
            // here or inside a folder) can't be deleted through the contents API.
            folders[top] = (item.Type == "blob" && item.Size is { } s ? (seen.Size ?? 0) + s : seen.Size,
                seen.Removable && item.Type is "blob" or "tree");
        }
        foreach (var (name, v) in folders) extras.Add(new RepoExtra(RepoExtraKind.File, name, v.Size, null, v.Removable));

        foreach (var branch in snapshot.Branches.Distinct(StringComparer.Ordinal).OrderBy(b => b, StringComparer.Ordinal))
            if (branch != snapshot.Repo.DefaultBranch) extras.Add(new RepoExtra(RepoExtraKind.Branch, branch, null, null));

        // Releases: one per listed share (at most MaxShares, manifest order), plus the superseded one while its PR is open.
        var shares = snapshot.Manifest?.Shares ?? [];
        var listed = shares.Select(s => s.Release).Distinct(StringComparer.Ordinal).Take(ShareRules.MaxShares).ToList();
        var allowedTags = new HashSet<string>(listed, StringComparer.Ordinal);
        foreach (var kept in (snapshot.ProtectedTags ?? []).Distinct(StringComparer.Ordinal).Take(MaxProtectedTags)) allowedTags.Add(kept);

        // A share whose Release tag is not <slug>-v<N> is a manifest nfty did not write; what its files are is unknowable.
        var damaged = shares.Where(s => !IsVersionTag(s.Release, s.Slug)).Select(s => s.Release).ToHashSet(StringComparer.Ordinal);

        // Two Releases on one allowed tag: which is the live share's can't be told, so neither is offered for removal.
        var duplicated = snapshot.Releases.Where(r => allowedTags.Contains(r.Tag)).GroupBy(r => r.Tag, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var keptTags = new HashSet<string>(StringComparer.Ordinal);
        var assetExtras = new List<RepoExtra>();
        var releaseExtras = new List<RepoExtra>();
        foreach (var release in snapshot.Releases)
        {
            if (duplicated.Contains(release.Tag)) { keptTags.Add(release.Tag); continue; }
            if (!allowedTags.Contains(release.Tag) || !keptTags.Add(release.Tag))
            {
                long? total = release.Assets.Count == 0 ? null : release.Assets.Sum(a => a.Size);
                releaseExtras.Add(new RepoExtra(RepoExtraKind.Release, release.Tag, total, release.Id));
                continue;
            }
            if (damaged.Contains(release.Tag)) continue;
            string slug = shares.FirstOrDefault(s => s.Release == release.Tag)?.Slug ?? VersionSuffix().Replace(release.Tag, "");
            foreach (var asset in release.Assets)
                if (asset.Name != slug + ".set" && asset.Name != PreviewAsset)
                    assetExtras.Add(new RepoExtra(RepoExtraKind.Asset, asset.Name, asset.Size, asset.Id));
        }
        extras.AddRange(releaseExtras.OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Id));
        extras.AddRange(assetExtras.OrderBy(e => e.Name, StringComparer.Ordinal).ThenBy(e => e.Id));

        // Tags: only those of allowed Releases.
        foreach (var tag in snapshot.Tags.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal))
            if (!keptTags.Contains(tag)) extras.Add(new RepoExtra(RepoExtraKind.Tag, tag, null, null));

        string? blocker = snapshot.Manifest is null ? ShareRules.RepoNotOurs
            : snapshot.Repo.Private ? ShareRules.RepoPrivate
            : snapshot.Repo.SizeKb > ShareRules.MaxHistoryKb ? ShareRules.HistoryTooLarge(snapshot.Repo.SizeKb)
            : duplicated.Count > 0 ? ShareRules.RepoDuplicateRelease(snapshot.Releases.Select(r => r.Tag).First(duplicated.Contains))
            : damaged.Count > 0 ? ShareRules.RepoManifestDamaged
            : null;
        return new RepoScan(extras, blocker);
    }

    /// <summary>
    /// Reads everything the scan needs. Returns <see langword="null"/> when the repository does not exist (the first share
    /// creates it). A missing or damaged <c>nfty-public.json</c> is a null <see cref="RepoSnapshot.Manifest"/>; one written by a
    /// newer nfty throws a <see cref="CommunityException"/> saying so rather than being called foreign.
    /// </summary>
    /// <param name="client">The GitHub client.</param>
    /// <param name="login">The signed-in account, which owns the repository.</param>
    /// <param name="protectedTags">Release tags that are never extras: every one the gallery lists for the account, and every one an
    /// open pull request names (the live version while an update waits, and the new one). Pass an empty set only when nothing is pending.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<RepoSnapshot?> FetchAsync(GitHubShareClient client, string login,
        IReadOnlyCollection<string> protectedTags, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(protectedTags);
        var repo = await client.GetRepoAsync(login, RepoName, ct).ConfigureAwait(false);
        if (repo is null) return null;
        var tree = await client.GetTreeAsync(login, RepoName, repo.DefaultBranch, ct).ConfigureAwait(false);
        var branches = await client.ListBranchesAsync(login, RepoName, ct).ConfigureAwait(false);
        var releases = await client.ListReleasesAsync(login, RepoName, ct).ConfigureAwait(false);
        var tags = await client.ListTagsAsync(login, RepoName, ct).ConfigureAwait(false);
        var file = await client.GetFileAsync(login, RepoName, ManifestFile, repo.DefaultBranch, ct).ConfigureAwait(false);
        PublicRepoManifest? manifest = null;
        if (file is { } f)
        {
            try { manifest = PublicRepoManifest.Read(new MemoryStream(f.Content)); }
            catch (InvalidDataException) { manifest = null; }
            catch (UnsupportedSchemaVersionException ex)
            {
                throw new CommunityException(ex.Found > ex.Supported ? ShareRules.RepoNewerNfty : ShareRules.RepoNotOurs);
            }
        }
        return new RepoSnapshot(repo, tree, branches, releases, tags, manifest, protectedTags);
    }

    private static bool Present(RepoSnapshot s, RepoExtra e) => e.Kind switch
    {
        RepoExtraKind.File => s.Tree.Any(t => t.Path == e.Name || t.Path.StartsWith(e.Name + "/", StringComparison.Ordinal)),
        RepoExtraKind.Branch => s.Branches.Contains(e.Name),
        RepoExtraKind.Release => s.Releases.Any(r => r.Id == e.Id),
        RepoExtraKind.Asset => s.Releases.Any(r => r.Assets.Any(a => a.Id == e.Id)),
        RepoExtraKind.Tag => s.Tags.Contains(e.Name),
        _ => true,
    };

    /// <summary>
    /// Deletes exactly <paramref name="extras"/>: assets, then Releases, then tags, then files (each blob at its own sha),
    /// the extra branches last. Before deleting anything it re-reads the repository and refuses (nothing deleted) when the
    /// repository is not nfty's, or when any item is not an extra of that fresh scan, which is what keeps
    /// <c>README.md</c>, <c>nfty-public.json</c>, the default branch and every allowed Release and tag out of reach.
    /// </summary>
    /// <param name="client">The GitHub client.</param>
    /// <param name="login">The signed-in account, which owns the repository.</param>
    /// <param name="extras">What to delete, as the scan listed it.</param>
    /// <param name="protectedTags">The tags kept out of reach (see <see cref="FetchAsync"/>): the live version while an update waits, and the new one.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentException">An item is README.md, nfty-public.json or the default branch.</exception>
    /// <exception cref="CommunityException">The repository is not nfty's, changed since the scan, or an item can't be removed (a submodule).</exception>
    public static async Task RemoveAsync(GitHubShareClient client, string login, IReadOnlyList<RepoExtra> extras,
        IReadOnlyCollection<string> protectedTags, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(protectedTags);
        if (extras.Count == 0) return;

        foreach (var e in extras)
            if (e.Kind == RepoExtraKind.File && (e.Name == ReadmeFile || e.Name == ManifestFile))
                throw new ArgumentException("README.md and nfty-public.json are never removed.", nameof(extras));

        var snapshot = await FetchAsync(client, login, protectedTags, ct).ConfigureAwait(false)
            ?? throw new CommunityException(ShareRules.RepoNotOurs);
        if (snapshot.Manifest is null) throw new CommunityException(ShareRules.RepoNotOurs);
        foreach (var e in extras)
            if (e.Kind == RepoExtraKind.Branch && e.Name == snapshot.Repo.DefaultBranch)
                throw new ArgumentException("The default branch is never removed.", nameof(extras));

        // Every item is checked against a fresh scan BEFORE anything is deleted. Gone already: nothing to do. Still
        // there but no longer an extra (or never removable): the whole removal stops.
        var fresh = Scan(snapshot).Extras;
        var todo = new List<RepoExtra>();
        foreach (var e in extras)
        {
            var match = fresh.FirstOrDefault(f => f.Kind == e.Kind && f.Name == e.Name && f.Id == e.Id);
            if (match is null)
            {
                if (Present(snapshot, e)) throw new CommunityException(ShareRules.RepoChanged);
                continue;
            }
            if (!match.Removable) throw new CommunityException(ShareRules.RepoSubmodule(match.Name));
            todo.Add(match);
        }

        var wanted = todo.DistinctBy(e => (e.Kind, e.Name, e.Id)).ToList();
        string branch = snapshot.Repo.DefaultBranch;

        foreach (var e in wanted.Where(e => e.Kind == RepoExtraKind.Asset))
            await client.DeleteAssetAsync(login, RepoName, e.Id!.Value, ct).ConfigureAwait(false);
        foreach (var e in wanted.Where(e => e.Kind == RepoExtraKind.Release))
            await client.DeleteReleaseAsync(login, RepoName, e.Id!.Value, ct).ConfigureAwait(false);
        // Deleting a Release leaves its tag on GitHub, so tags are deleted explicitly.
        foreach (var e in wanted.Where(e => e.Kind == RepoExtraKind.Tag))
            await client.DeleteTagAsync(login, RepoName, e.Name, ct).ConfigureAwait(false);
        foreach (var e in wanted.Where(e => e.Kind == RepoExtraKind.File))
        {
            // The contents API deletes one blob at a time, at its sha; a folder is every blob under it.
            var blobs = snapshot.Tree
                .Where(t => t.Type == "blob" && (t.Path == e.Name || t.Path.StartsWith(e.Name + "/", StringComparison.Ordinal)))
                .OrderBy(t => t.Path, StringComparer.Ordinal);
            foreach (var blob in blobs)
                await client.DeleteFileAsync(login, RepoName, blob.Path, blob.Sha, "Remove a file the community gallery doesn't allow", branch, ct).ConfigureAwait(false);
        }
        foreach (var e in wanted.Where(e => e.Kind == RepoExtraKind.Branch))
            await client.DeleteBranchAsync(login, RepoName, e.Name, ct).ConfigureAwait(false);
    }
}
