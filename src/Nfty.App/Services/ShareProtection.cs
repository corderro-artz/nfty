using Nfty.Core.Publish.Community;

namespace Nfty.App.Services;

/// <summary>
/// Which Release tags of the account's <c>nfty-public</c> the scan must never call extras or offer for deletion.
/// </summary>
/// <remarks>
/// While an update's pull request is open the gallery still lists v1 but <c>nfty-public.json</c> already names v2, so v1 is
/// the LIVE share and must be kept; a second update before the merge adds v3. Three things name a tag that has to stay: the
/// gallery's published list (the account's own entries), every pull request this machine opened and has not seen the end
/// of (its new tag, the one it supersedes, the one it replaces), and nothing else. A refused update's record is closed and
/// stops protecting its new tag, but the published list still names the live one.
/// </remarks>
public static class ShareProtection
{
    /// <summary>The tags to keep.</summary>
    /// <param name="index">The published list as last seen, or null when none is known.</param>
    /// <param name="pending">The pull requests this machine opened.</param>
    /// <param name="ownerId">The account's GitHub id.</param>
    /// <returns>Each tag once.</returns>
    public static IReadOnlyCollection<string> Tags(CommunityIndex? index, IReadOnlyList<PendingRecord> pending, long ownerId)
    {
        ArgumentNullException.ThrowIfNull(pending);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in index?.Entries ?? [])
            if (e.Owner.Id == ownerId && e.Release.Length > 0) tags.Add(e.Release);
        foreach (var p in pending)
        {
            if (p.OwnerId != ownerId || p.Closed) continue;
            foreach (var t in new[] { p.Tag, p.SupersededTag, p.ReplacedTag })
                if (!string.IsNullOrEmpty(t)) tags.Add(t);
        }
        return tags;
    }
}
