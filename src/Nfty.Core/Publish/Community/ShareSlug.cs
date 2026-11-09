using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Nfty.Core.Publish.Community;

/// <summary>Turns a collection name into the slug that names a share's Release, files and index entry.</summary>
public static partial class ShareSlug
{
    private const int MaxLength = 40;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    private static partial Regex Valid();

    /// <summary>The slug for a name: lowercase letters and digits, runs of anything else as one dash, at most 40 characters.</summary>
    /// <param name="collectionName">The collection's published name.</param>
    /// <returns>A valid slug; a name with no ASCII letters or digits gets <c>set-</c> plus eight hex digits of its hash, stable for that name.</returns>
    public static string Of(string collectionName)
    {
        ArgumentNullException.ThrowIfNull(collectionName);
        var dashed = NonSlug().Replace(collectionName.ToLowerInvariant(), "-").Trim('-');
        if (dashed.Length > MaxLength) dashed = dashed[..MaxLength].Trim('-');
        if (dashed.Length > 0) return dashed;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(collectionName));
        return "set-" + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Whether a string is a slug: 1 to 40 of lowercase letters, digits and inner dashes.</summary>
    /// <param name="slug">The candidate.</param>
    /// <returns>True when valid.</returns>
    public static bool IsValid(string slug) => slug is not null && Valid().IsMatch(slug);
}
