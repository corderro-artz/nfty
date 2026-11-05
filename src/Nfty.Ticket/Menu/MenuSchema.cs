namespace Nfty.Ticket;

/// <summary>The Menu schema range this build can read and write — separate from
/// <see cref="Nfty.Core.Model.Schema"/> because a Menu is not a CookBook manifest and evolves on
/// its own line.</summary>
public static class MenuSchema
{
    /// <summary>The schema version this build writes. It reads the inclusive range
    /// <see cref="Oldest"/> through this version.</summary>
    public const int Current = 2;

    /// <summary>The oldest schema this build reads. Schema 1 predates depth maps and the
    /// <c>partialAlpha</c> listing cache and is no longer supported. Versions outside the supported
    /// range are refused rather than silently misparsed.</summary>
    public const int Oldest = 2;

    /// <summary>Checks whether a version falls in the reader's inclusive supported range.</summary>
    /// <param name="version">The version a Menu declares.</param>
    /// <param name="current">The newest version the reader supports. Explicit so tests can verify
    /// a multi-version range even while <see cref="Oldest"/> equals <see cref="Current"/>.</param>
    /// <returns>True when <paramref name="version"/> is at least <see cref="Oldest"/> and no greater
    /// than <paramref name="current"/>.</returns>
    public static bool IsSupported(int version, int current) => version >= Oldest && version <= current;
}
