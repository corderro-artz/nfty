namespace Nfty.Core.Model;

/// <summary>
/// A manifest that declares the schema it was written against. Every archive manifest carries
/// one so a future format can be rejected explicitly instead of silently misparsing.
/// </summary>
public interface ISchemaVersioned
{
    int SchemaVersion { get; }
}

public static class Schema
{
    /// <summary>The only schema version this build reads or writes.</summary>
    public const int Current = 1;
}
