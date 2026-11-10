using System.Text.RegularExpressions;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Core.Publish.Community;

/// <summary>The gallery's fixed tag list, published as <c>tags.json</c> beside <c>index.json</c>.</summary>
/// <param name="SchemaVersion">The schema this list was written against.</param>
/// <param name="Tags">The tags, in the order the gallery wants them shown.</param>
public sealed partial record CommunityTags(int SchemaVersion, IReadOnlyList<string> Tags) : ISchemaVersioned
{
    /// <summary>The most tags a list may carry.</summary>
    public const int MaxTags = 32;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,23}$")]
    private static partial Regex TagShape();

    /// <summary>Reads the list, refusing a newer schema by name and anything malformed as damaged.</summary>
    /// <param name="stream">The JSON.</param>
    /// <exception cref="UnsupportedSchemaVersionException">The schema is newer than this build reads.</exception>
    /// <exception cref="InvalidDataException">The file is damaged: bad JSON, a bad or repeated tag, too many tags.</exception>
    /// <returns>The tag list read.</returns>
    public static CommunityTags Read(Stream stream)
    {
        var tags = CommunitySchema.Load<CommunityTags>(stream, "tags");
        if (tags.Tags is null || tags.Tags.Count > MaxTags
            || tags.Tags.Any(t => t is null || !TagShape().IsMatch(t))
            || tags.Tags.Distinct(StringComparer.Ordinal).Count() != tags.Tags.Count)
            throw new InvalidDataException(ShareRules.ListDamaged);
        return tags;
    }
}
