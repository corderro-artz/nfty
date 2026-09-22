namespace Nfty.Ticket;

/// <summary>The Menu schema range this build can read and write — separate from
/// <see cref="Nfty.Core.Model.Schema"/> because a Menu is not a CookBook manifest and evolves on
/// its own line.</summary>
public static class MenuSchema
{
    /// <summary>The schema version this build WRITES. It reads this and every earlier one — see
    /// <see cref="IsSupported"/>.</summary>
    public const int Current = 1;

    /// <summary>The oldest schema this build can read. Nothing predates 1, so a lower number is a
    /// malformed Menu rather than an old one.</summary>
    public const int Oldest = 1;

    /// <summary>Whether <paramref name="version"/> is readable by a build whose newest schema is
    /// <paramref name="current"/>.
    ///
    /// <paramref name="current"/> is a parameter rather than being read from <see cref="Current"/>
    /// so the RANGE itself is testable. With Current == 1 the range 1..Current contains exactly one
    /// value, which makes "accepts older versions" indistinguishable from "demands exact equality" —
    /// a test written against the constant passes either way and proves nothing. Passing a
    /// hypothetical current lets the rule be pinned now, before there is a version 2 to try it on.</summary>
    /// <param name="version">The version a Menu declares.</param>
    /// <param name="current">The newest version the reader supports.</param>
    /// <returns>Whether the reader can understand that version.</returns>
    public static bool IsSupported(int version, int current) => version >= Oldest && version <= current;
}
