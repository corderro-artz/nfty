using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>
/// A manifest declares a schema this build cannot read. Raised instead of letting an unknown
/// format deserialize into whatever happens to fit. (Not an <see cref="InvalidDataException"/>
/// only because that type is sealed — the archive is well-formed, just too new to support.)
/// </summary>
public class UnsupportedSchemaVersionException : NotSupportedException
{
    public int Found { get; }
    public int Supported { get; }

    public UnsupportedSchemaVersionException(int found, int supported, string message)
        : base(message)
    {
        Found = found;
        Supported = supported;
    }

    /// <summary>Accepts any schema this build understands — version 1 through
    /// <see cref="Schema.Current"/> — and rejects anything newer.
    ///
    /// This used to demand EXACT equality with <see cref="Schema.Current"/>, which made the format
    /// unevolvable: bumping Current by one would have made every .cbk, .rcp and .igt already written
    /// unreadable, including the tests/fixtures archives that exist to catch precisely that. A
    /// version gate whose only safe value is the one it already has is not a gate.
    ///
    /// Reading an older version is sound because of a contract this project keeps deliberately:
    /// **every field added after v1 is optional and carries a default that means "absent"**. A v1
    /// archive is therefore a valid vN archive with those fields unset. A change that cannot honour
    /// that — a renamed field, a changed meaning, a new REQUIRED field — is a breaking change, and
    /// needs a real migration here rather than a bump.
    ///
    /// Newer is still refused outright: a future field this build does not know about could change
    /// what the archive means, and silently ignoring it would produce confidently wrong output.</summary>
    public static void Require<T>(T manifest) where T : ISchemaVersioned
    {
        if (Schema.IsSupported(manifest.SchemaVersion, Schema.Current)) return;

        string kind = typeof(T).Name.Replace("Manifest", string.Empty);
        string why = manifest.SchemaVersion > Schema.Current
            ? $"this build of nfty supports up to schemaVersion {Schema.Current}."
            : $"schemaVersion must be at least 1; {manifest.SchemaVersion} is not a valid schema.";
        throw new UnsupportedSchemaVersionException(manifest.SchemaVersion, Schema.Current,
            $"{kind} manifest declares schemaVersion {manifest.SchemaVersion}; {why}");
    }
}
