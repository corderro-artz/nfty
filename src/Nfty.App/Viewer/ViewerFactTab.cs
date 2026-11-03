using System.Globalization;

namespace Nfty.App.Viewer;

/// <summary>One tab of the info rail: a group that has facts, its count, and whether it is the one
/// shown.</summary>
/// <param name="Group">The group.</param>
/// <param name="Count">How many facts it lists.</param>
/// <param name="IsSelected">Whether it is the tab the rail shows.</param>
public sealed record ViewerFactTab(ViewerFactGroup Group, int Count, bool IsSelected)
{
    /// <summary>The tab's caps name.</summary>
    public string Title => ViewerFact.GroupTitle(Group);

    /// <summary>The count badge's text.</summary>
    public string CountText => Count.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One group as the extended info panel lists it: a heading, a count and every fact.</summary>
/// <param name="Group">The group.</param>
/// <param name="Facts">Its facts, in the subject's order.</param>
public sealed record ViewerFactSection(ViewerFactGroup Group, IReadOnlyList<ViewerFact> Facts)
{
    /// <summary>The heading.</summary>
    public string Title => ViewerFact.GroupTitle(Group);

    /// <summary>The count badge's text.</summary>
    public string CountText => Facts.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Splits the facts into the extended panel's two columns: whole groups, never a group across
    /// both, balanced by how many LINES they take (a prose or a hash row is taller than an inline
    /// one), the left column filled first so a short subject reads from the left.
    /// </summary>
    /// <param name="sections">The groups, in tab order.</param>
    /// <returns>The left and right columns.</returns>
    public static (IReadOnlyList<ViewerFactSection> Left, IReadOnlyList<ViewerFactSection> Right) Balance(
        IReadOnlyList<ViewerFactSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count <= 1) return (sections, []);
        static int Weight(ViewerFactSection s) => 2 + s.Facts.Sum(f => f.IsInline ? 1 : 2);
        int total = sections.Sum(Weight);
        // The split point that leaves the two columns closest in height; ties to the earlier split.
        int best = 1, bestGap = int.MaxValue, running = 0;
        for (int i = 1; i < sections.Count; i++)
        {
            running += Weight(sections[i - 1]);
            int gap = Math.Abs(total - (2 * running));
            if (gap < bestGap) { bestGap = gap; best = i; }
        }
        return (sections.Take(best).ToList(), sections.Skip(best).ToList());
    }
}
