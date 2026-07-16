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

    /// <summary>Rejects any manifest not written against <see cref="Schema.Current"/>.</summary>
    public static void Require<T>(T manifest) where T : ISchemaVersioned
    {
        if (manifest.SchemaVersion == Schema.Current) return;

        string kind = typeof(T).Name.Replace("Manifest", string.Empty);
        throw new UnsupportedSchemaVersionException(manifest.SchemaVersion, Schema.Current,
            $"{kind} manifest declares schemaVersion {manifest.SchemaVersion}; "
            + $"this build of nfty supports schemaVersion {Schema.Current} only.");
    }
}
