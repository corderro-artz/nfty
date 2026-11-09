using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Core.Publish.Community;

/// <summary>The gallery's published list: every entry, generated from the owners' repositories.</summary>
/// <param name="SchemaVersion">The schema this list was written against.</param>
/// <param name="Generated">When the list was built.</param>
/// <param name="Entries">The entries.</param>
public sealed record CommunityIndex(int SchemaVersion, DateTimeOffset Generated, IReadOnlyList<CommunityEntry> Entries)
    : ISchemaVersioned
{
    /// <summary>Reads a list, refusing a newer schema by name.</summary>
    /// <param name="stream">The JSON.</param>
    public static CommunityIndex Read(Stream stream)
    {
        var index = JsonSerializer.Deserialize<CommunityIndex>(stream, Json.Options)
            ?? throw new InvalidDataException("The community list is empty.");
        CommunitySchema.Require(index.SchemaVersion);
        foreach (var e in index.Entries) CommunitySchema.Require(e.SchemaVersion);
        return index;
    }

    /// <summary>Writes a list with entries sorted ordinally: owner id ascending, then slug.</summary>
    /// <param name="index">The list.</param>
    public static byte[] Write(CommunityIndex index)
    {
        var sorted = index.Entries
            .OrderBy(e => e.Owner.Id)
            .ThenBy(e => e.Slug, StringComparer.Ordinal)
            .ToList();
        return JsonSerializer.SerializeToUtf8Bytes(index with { Entries = sorted }, Json.Options);
    }
}

/// <summary>The <c>nfty-public.json</c> an owner's repository carries: what that account shares.</summary>
/// <param name="SchemaVersion">The schema this file was written against.</param>
/// <param name="Shares">The account's entries.</param>
public sealed record PublicRepoManifest(int SchemaVersion, IReadOnlyList<CommunityEntry> Shares) : ISchemaVersioned
{
    /// <summary>Reads the file, refusing a newer schema by name.</summary>
    /// <param name="stream">The JSON.</param>
    public static PublicRepoManifest Read(Stream stream)
    {
        var m = JsonSerializer.Deserialize<PublicRepoManifest>(stream, Json.Options)
            ?? throw new InvalidDataException("nfty-public.json is empty.");
        CommunitySchema.Require(m.SchemaVersion);
        foreach (var e in m.Shares) CommunitySchema.Require(e.SchemaVersion);
        return m;
    }

    /// <summary>Writes the file in the order given.</summary>
    /// <param name="manifest">The manifest.</param>
    public static byte[] Write(PublicRepoManifest manifest) =>
        JsonSerializer.SerializeToUtf8Bytes(manifest, Json.Options);
}
