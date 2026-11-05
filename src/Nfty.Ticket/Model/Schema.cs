namespace Nfty.Core.Model;

/// <summary>
/// A manifest that declares the schema it was written against. Every archive manifest carries
/// one so a future format can be rejected explicitly instead of silently misparsing.
/// </summary>
public interface ISchemaVersioned
{
    /// <summary>The schema version this manifest was written against.</summary>
    int SchemaVersion { get; }
}

/// <summary>The manifest schema range this build can read and write.</summary>
public static class Schema
{
    /// <summary>The schema version this build writes. It reads the inclusive range
    /// <see cref="Oldest"/> through this version.</summary>
    public const int Current = 2;

    /// <summary>The oldest schema this build reads. Schema 1 support was deliberately dropped
    /// when schema 2 introduced depth maps and the <c>partialAlpha</c> listing cache; older and
    /// newer versions are both refused rather than silently misparsed.</summary>
    public const int Oldest = 2;

    /// <summary>Checks whether a version falls in the reader's inclusive supported range.</summary>
    /// <param name="version">The version a manifest declares.</param>
    /// <param name="current">The newest version the reader supports. Explicit so tests can verify
    /// a multi-version range even while <see cref="Oldest"/> equals <see cref="Current"/>.</param>
    /// <returns>True when <paramref name="version"/> is at least <see cref="Oldest"/> and no greater
    /// than <paramref name="current"/>.</returns>
    public static bool IsSupported(int version, int current) => version >= Oldest && version <= current;
}
