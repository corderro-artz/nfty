using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.Cli;

/// <summary>Reads an authoring manifest JSON file through the shared <see cref="Json.Options"/>,
/// turning framework parse failures into messages that name the file. Enforces the schema version
/// so an authoring input cannot declare a format this build cannot write.</summary>
public static class ManifestFile
{
    /// <summary>Reads an authoring manifest.</summary>
    /// <typeparam name="T">The manifest record to deserialize.</typeparam>
    /// <param name="path">Path to the JSON file.</param>
    /// <returns>The parsed manifest.</returns>
    /// <exception cref="FileNotFoundException">No file at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not valid JSON, or not a valid
    /// <typeparamref name="T"/>.</exception>
    /// <exception cref="Nfty.Core.Formats.UnsupportedSchemaVersionException">It declares a schema
    /// version this build cannot write.</exception>
    public static T Read<T>(string path) where T : ISchemaVersioned
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No such manifest file: {path}", path);
        string json = File.ReadAllText(path);
        T manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<T>(json, Json.Options)
                ?? throw new InvalidDataException($"Manifest '{path}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Manifest '{path}' is not valid JSON: {ex.Message}", ex);
        }
        // Omitting schemaVersion is fine (it defaults to Schema.Current); declaring an unsupported
        // one is rejected with the same message the archive readers use.
        UnsupportedSchemaVersionException.Require(manifest);
        return manifest;
    }
}
