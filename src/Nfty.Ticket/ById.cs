namespace Nfty.Ticket;

/// <summary>Lookup helpers shared by every assembly that resolves ids to things.</summary>
public static class Lookup
{
    /// <summary>Indexes items by id, ordinally, tolerating duplicates.</summary>
    /// <remarks>Last one wins and nothing throws: a transiently invalid book (a duplicate id) is a
    /// normal state for the callers that must keep working mid-edit, and deciding that a duplicate
    /// is a mistake is the validator's job, not this lookup's. <c>ToDictionary</c> throws a bare
    /// <c>ArgumentException</c> on a duplicate; this is the one place that choice is made.</remarks>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="id">Reads an item's id.</param>
    /// <returns>The items by id.</returns>
    public static Dictionary<string, T> ById<T>(IEnumerable<T> items, Func<T, string> id)
    {
        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items) map[id(item)] = item;
        return map;
    }
}
