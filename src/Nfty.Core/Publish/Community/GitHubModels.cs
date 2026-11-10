namespace Nfty.Core.Publish.Community;

/// <summary>A share or gallery step that failed; the message is always a <see cref="ShareRules"/> sentence, safe to show.</summary>
/// <param name="message">The sentence.</param>
public class CommunityException(string message) : Exception(message);

/// <summary>GitHub is limiting requests; a batch that reads many files stops on this instead of skipping on.</summary>
internal sealed class RateLimitedException(string message) : CommunityException(message);

/// <summary>Where the community gallery lives. Only the API and uploads hosts ever receive the person's token.</summary>
/// <param name="GalleryRepo">The gallery repository, <c>owner/name</c>.</param>
/// <param name="IndexUrl">The published list (served from Pages; fetched without a token).</param>
/// <param name="ApiBase">The GitHub REST API root.</param>
/// <param name="UploadsBase">The GitHub release-upload root.</param>
/// <param name="ClientId">The OAuth app's public client id.</param>
/// <param name="ActionsBot">The login whose pull request comments are the gallery's checks; any other commenter is ignored.</param>
public sealed record CommunityEndpoints(string GalleryRepo, Uri IndexUrl, Uri ApiBase, Uri UploadsBase, string ClientId,
    string ActionsBot = "github-actions[bot]")
{
    /// <summary>The shipped gallery: <c>vaporsoft-dev/nfty-gallery</c>, signed in through the vaporsoft-dev OAuth app
    /// (device flow, no client secret — the id is public by design).</summary>
    public static CommunityEndpoints Default { get; } = new(
        "vaporsoft-dev/nfty-gallery",
        new Uri("https://vaporsoft-dev.github.io/nfty-gallery/index.json"),
        new Uri("https://api.github.com/"),
        new Uri("https://uploads.github.com/"),
        "Ov23li8dFCHIMEhlN6SG");
}

/// <summary>The signed-in GitHub account.</summary>
/// <param name="Id">GitHub's numeric account id.</param>
/// <param name="Login">The account name.</param>
/// <param name="CreatedAt">When the account was made.</param>
public sealed record GitHubUser(long Id, string Login, DateTimeOffset CreatedAt);

/// <summary>A GitHub repository.</summary>
/// <param name="FullName"><c>owner/name</c>.</param>
/// <param name="Private">Whether it is private.</param>
/// <param name="DefaultBranch">Its default branch.</param>
/// <param name="SizeKb">Its size in KB as GitHub reports it.</param>
public sealed record GitHubRepo(string FullName, bool Private, string DefaultBranch, long SizeKb);

/// <summary>One entry of a repository tree.</summary>
/// <param name="Path">Path inside the repository.</param>
/// <param name="Type"><c>blob</c> or <c>tree</c> (or <c>commit</c> for a submodule).</param>
/// <param name="Sha">The object's SHA.</param>
/// <param name="Size">Size in bytes for a blob.</param>
public sealed record GitHubTreeItem(string Path, string Type, string Sha, long? Size);

/// <summary>A file attached to a release.</summary>
/// <param name="Id">Asset id.</param>
/// <param name="Name">File name.</param>
/// <param name="Size">Size in bytes.</param>
public sealed record GitHubAsset(long Id, string Name, long Size);

/// <summary>A GitHub release.</summary>
/// <param name="Id">Release id.</param>
/// <param name="Tag">Its tag.</param>
/// <param name="Assets">Files attached to it.</param>
/// <param name="Owner">The repository owner (filled in by the client that read it).</param>
/// <param name="Repo">The repository name (filled in by the client that read it).</param>
public sealed record GitHubRelease(long Id, string Tag, IReadOnlyList<GitHubAsset> Assets, string Owner = "", string Repo = "");

/// <summary>A pull request's state.</summary>
/// <param name="State"><c>open</c> or <c>closed</c>.</param>
/// <param name="Merged">Whether it was merged.</param>
public sealed record GitHubPull(string State, bool Merged);
