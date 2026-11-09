using System.Text.Json.Serialization;
using Nfty.Core.Model;

namespace Nfty.Core.Publish.Community;

/// <summary>The community gallery's schema range. Same contract as the archive manifests: every field added after the floor is optional.</summary>
public static class CommunitySchema
{
    /// <summary>The oldest schema this build reads.</summary>
    public const int Oldest = 1;
    /// <summary>The newest schema this build reads and writes.</summary>
    public const int Current = 1;

    /// <summary>Refuses a version outside <see cref="Oldest"/>..<see cref="Current"/> by name, never misparsing it.</summary>
    /// <param name="version">The declared schema version.</param>
    /// <exception cref="Nfty.Core.Formats.UnsupportedSchemaVersionException">The version is newer, or older than the floor.</exception>
    public static void Require(int version)
    {
        if (version >= Oldest && version <= Current) return;
        throw new Nfty.Core.Formats.UnsupportedSchemaVersionException(version, Current,
            version > Current
                ? ShareRules.IndexTooNew
                : $"The community gallery list declares schemaVersion {version}, which this nfty no longer reads.");
    }
}

/// <summary>The GitHub account that shares an entry.</summary>
/// <param name="Id">GitHub's numeric account id, which survives a rename.</param>
/// <param name="Login">The account name at the time of sharing.</param>
public sealed record CommunityOwner(long Id, string Login);

/// <summary>A shared Set's canvas.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public sealed record CommunityCanvas(int Width, int Height);

/// <summary>A file the entry points at.</summary>
/// <param name="Url">Where it is downloaded from.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Sha256">Its SHA-256, lowercase hex.</param>
public sealed record CommunityFile(string Url, long Size, string Sha256);

/// <summary>One shared collection as the gallery lists it.</summary>
/// <param name="SchemaVersion">The schema this entry was written against.</param>
/// <param name="Slug">The share's name within its owner's account.</param>
/// <param name="Owner">Who shares it.</param>
/// <param name="Repo">The owner's repository holding the files.</param>
/// <param name="Release">The release tag holding the files.</param>
/// <param name="Name">The share's display name.</param>
/// <param name="Collection">The collection name the Set publishes.</param>
/// <param name="Description">A short description.</param>
/// <param name="Tags">Gallery tags.</param>
/// <param name="Canvas">The canvas size.</param>
/// <param name="Assets">How many assets the Set holds.</param>
/// <param name="Models">Whether the Set carries 3D models.</param>
/// <param name="IncludesCookBook">Whether the source CookBook is inside.</param>
/// <param name="CookbookSha256">The source CookBook's hash, when known.</param>
/// <param name="Set">The packed Set.</param>
/// <param name="Preview">The preview picture.</param>
/// <param name="Fingerprint">Hash of what the Set shows, used to spot the same Set shared twice.</param>
/// <param name="NftyVersion">The nfty version that made the share.</param>
/// <param name="Created">When it was first shared.</param>
/// <param name="Updated">When it was last updated.</param>
public sealed record CommunityEntry(
    int SchemaVersion, string Slug, CommunityOwner Owner, string Repo, string Release,
    string Name, string Collection, string Description, IReadOnlyList<string> Tags,
    CommunityCanvas Canvas, int Assets, bool Models, bool IncludesCookBook, string? CookbookSha256,
    CommunityFile Set, CommunityFile Preview, string Fingerprint, string NftyVersion,
    DateTimeOffset Created, DateTimeOffset Updated) : ISchemaVersioned
{
    /// <summary>The entry's identity in the gallery: owner id and slug.</summary>
    [JsonIgnore]
    public string Id => $"{Owner.Id}/{Slug}";

    /// <summary>True when every shared field is equal; <see cref="Updated"/> is ignored and <see cref="Tags"/> compare as a sequence.</summary>
    /// <param name="a">One entry.</param>
    /// <param name="b">The other.</param>
    public static bool SameShare(CommunityEntry a, CommunityEntry b) =>
        a.SchemaVersion == b.SchemaVersion && a.Slug == b.Slug && a.Owner == b.Owner && a.Repo == b.Repo
        && a.Release == b.Release && a.Name == b.Name && a.Collection == b.Collection
        && a.Description == b.Description && a.Tags.SequenceEqual(b.Tags) && a.Canvas == b.Canvas
        && a.Assets == b.Assets && a.Models == b.Models && a.IncludesCookBook == b.IncludesCookBook
        && a.CookbookSha256 == b.CookbookSha256 && a.Set == b.Set && a.Preview == b.Preview
        && a.Fingerprint == b.Fingerprint && a.NftyVersion == b.NftyVersion && a.Created == b.Created;
}

/// <summary>Duplicate rules for a new share (spec 4.4a).</summary>
public static class CommunityDuplicates
{
    /// <summary>The sentence that refuses <paramref name="candidate"/> as a duplicate of something listed, or null.</summary>
    /// <param name="candidate">The entry about to be added or updated.</param>
    /// <param name="listed">Everything already in the gallery.</param>
    public static string? Problem(CommunityEntry candidate, IEnumerable<CommunityEntry> listed)
    {
        string? ownOtherSlug = null, ownSameSlug = null;
        foreach (var e in listed)
        {
            bool sameSet = e.Fingerprint == candidate.Fingerprint;
            if (e.Owner.Id != candidate.Owner.Id)
            {
                if (sameSet || e.Set.Sha256 == candidate.Set.Sha256) return ShareRules.AlreadyListed(e.Owner.Login);
            }
            else if (sameSet)
            {
                if (e.Slug == candidate.Slug) ownSameSlug = ShareRules.NothingChanged(e.Name, e.Updated);
                else ownOtherSlug ??= ShareRules.AlreadySharedAs(e.Name);
            }
        }
        return ownSameSlug ?? ownOtherSlug;
    }
}

/// <summary>Date rules for a share arriving as a pull request.</summary>
public static class CommunityDates
{
    /// <summary>The sentence that refuses the entry's dates, or null.</summary>
    /// <param name="candidate">The entry being added or updated.</param>
    /// <param name="previous">The entry it replaces, if any.</param>
    /// <param name="prCreated">When the pull request was opened.</param>
    public static string? Problem(CommunityEntry candidate, CommunityEntry? previous, DateTimeOffset prCreated)
    {
        if (previous is not null && (candidate.Created != previous.Created || candidate.Updated <= previous.Updated))
            return ShareRules.DatesMismatch;
        if (candidate.Updated > prCreated + TimeSpan.FromHours(1)) return ShareRules.DatesMismatch;
        return null;
    }
}
