using System.Text.Json.Serialization;
using Nfty.Core.Publish.Community;

namespace Nfty.App.Services;

/// <summary>
/// A gallery pull request this machine opened and has not seen the end of: a share waiting to be merged, or the removal
/// of one. Kept in <c>pending.json</c> so the Community page can show the collection as pending, finish a merged one
/// (delete the Release it superseded) and say why a closed one was turned away.
/// </summary>
/// <param name="Kind"><see cref="ShareKind"/> or <see cref="UnshareKind"/>.</param>
/// <param name="PullNumber">The gallery pull request.</param>
/// <param name="PullUrl">Where to read it.</param>
/// <param name="Slug">The share it concerns.</param>
/// <param name="OwnerId">The account's GitHub id, which with the slug is the entry's id.</param>
/// <param name="Login">The account's login.</param>
/// <param name="Name">The listing's name, for the pending tile.</param>
/// <param name="Assets">How many assets the Set holds.</param>
/// <param name="Width">The canvas width.</param>
/// <param name="Height">The canvas height.</param>
/// <param name="Models">Whether the Set carries 3D models.</param>
/// <param name="IncludesCookBook">Whether the CookBook ships inside.</param>
/// <param name="SetBytes">The packed Set's size.</param>
/// <param name="SupersededTag">The Release tag an update replaced, or null.</param>
/// <param name="ReplacedTag">The Release tag a swap or an unshare removed, or null.</param>
/// <param name="ListedAtSubmit">Whether the gallery already listed this share when it was submitted (an update), so no tile of its own is needed.</param>
/// <param name="Finished">Whether the merge was seen and the old Release dealt with.</param>
/// <param name="SubmittedAt">When it was opened.</param>
/// <param name="Closed">Whether the pull request was closed without merging.</param>
/// <param name="ClosedReasons">The gallery's reasons, one line each, when <paramref name="Closed"/>.</param>
public sealed record PendingRecord(
    string Kind, int PullNumber, string PullUrl, string Slug, long OwnerId, string Login, string Name,
    int Assets, int Width, int Height, bool Models, bool IncludesCookBook, long SetBytes,
    string? SupersededTag, string? ReplacedTag, bool ListedAtSubmit, bool Finished, DateTimeOffset SubmittedAt,
    bool Closed, IReadOnlyList<string> ClosedReasons)
{
    /// <summary>The kind of a share.</summary>
    public const string ShareKind = "share";

    /// <summary>The kind of a removal.</summary>
    public const string UnshareKind = "unshare";

    /// <summary>The entry's id in the gallery.</summary>
    [JsonIgnore]
    public string Id => $"{OwnerId}/{Slug}";

    /// <summary>Whether this is a removal.</summary>
    [JsonIgnore]
    public bool IsUnshare => Kind == UnshareKind;

    /// <summary>The pull request as an address, or null when the record holds one nfty would not open.</summary>
    [JsonIgnore]
    public Uri? PullUri => Uri.TryCreate(PullUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u : null;

    /// <summary>What <see cref="CommunityShare.FinishAsync"/> is given once the pull request merges.</summary>
    [JsonIgnore]
    public Nfty.Core.Publish.Community.PendingShare Finishing => new(PullNumber, Slug, SupersededTag, ReplacedTag);

    /// <summary>An entry to draw a pending tile from when the gallery does not list the share yet.</summary>
    /// <returns>A stand-in with no downloadable files.</returns>
    public CommunityEntry ToEntry() => new(
        CommunitySchema.Current, Slug, new CommunityOwner(OwnerId, Login), $"{Login}/nfty-public", "", Name, "", "", [],
        new CommunityCanvas(Width, Height), Assets, Models, IncludesCookBook, null,
        new CommunityFile("", SetBytes, ""), new CommunityFile("", 0, ""), "", "", SubmittedAt, SubmittedAt);
}
