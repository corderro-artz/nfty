namespace Nfty.Core.Publish.Community;

/// <summary>What the Community search box asks for: every tag, every owner and every word must match.</summary>
/// <param name="Tags">The <c>#tag</c> terms, lowercased, without the hash.</param>
/// <param name="Owners">The <c>@user</c> terms, without the at sign.</param>
/// <param name="Words">The plain words, matched against a collection's name or its collection name.</param>
public sealed record CommunityQuery(IReadOnlyList<string> Tags, IReadOnlyList<string> Owners, IReadOnlyList<string> Words)
{
    /// <summary>Whether the query asks for nothing, so it matches every entry.</summary>
    public bool IsEmpty => Tags.Count == 0 && Owners.Count == 0 && Words.Count == 0;
}

/// <summary>The one search box: <c>#tag</c>, <c>@user</c> and plain words, combined with AND. The text is the only
/// source of truth, so clicking a tag chip, a tile's owner or a tag link just edits the text with <see cref="Toggle"/>.</summary>
public static class CommunitySearch
{
    /// <summary>Splits the box's text on whitespace into tags, owners and words. A lone <c>#</c> or <c>@</c> is ignored.</summary>
    /// <param name="text">What the user typed; null reads as empty.</param>
    public static CommunityQuery Parse(string? text)
    {
        var tags = new List<string>();
        var owners = new List<string>();
        var words = new List<string>();
        foreach (var raw in Split(text))
        {
            if (raw[0] == '#')
            {
                if (raw.Length > 1) tags.Add(raw[1..].ToLowerInvariant());
            }
            else if (raw[0] == '@')
            {
                if (raw.Length > 1) owners.Add(raw[1..]);
            }
            else words.Add(raw);
        }
        return new CommunityQuery(tags, owners, words);
    }

    /// <summary>Whether an entry satisfies every term of the query. An empty query matches everything.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="query">The parsed search.</param>
    public static bool Matches(CommunityEntry entry, CommunityQuery query)
    {
        foreach (var t in query.Tags)
            if (!entry.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)) return false;
        foreach (var o in query.Owners)
            if (!string.Equals(entry.Owner.Login, o, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var w in query.Words)
            if (!entry.Collection.Contains(w, StringComparison.InvariantCultureIgnoreCase)
                && !entry.Name.Contains(w, StringComparison.InvariantCultureIgnoreCase)) return false;
        return true;
    }

    /// <summary>Adds <paramref name="token"/> to the text when it is not there, removes it when it is, and leaves single spaces.</summary>
    /// <param name="text">The box's text.</param>
    /// <param name="token">The token, such as <c>#pets</c> or <c>@alice</c>. Compared ignoring case.</param>
    public static string Toggle(string? text, string token)
    {
        var parts = Split(text).ToList();
        int removed = parts.RemoveAll(p => string.Equals(p, token, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) parts.Add(token);
        return string.Join(' ', parts);
    }

    /// <summary>Whether the text carries <paramref name="token"/> as one of its terms (ignoring case).</summary>
    /// <param name="text">The box's text.</param>
    /// <param name="token">The token.</param>
    public static bool Has(string? text, string token) =>
        Split(text).Any(p => string.Equals(p, token, StringComparison.OrdinalIgnoreCase));

    private static string[] Split(string? text) =>
        (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
