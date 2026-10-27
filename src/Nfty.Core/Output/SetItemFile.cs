using System.Text.Json;
using Nfty.Core.Formats;

namespace Nfty.Core.Output;

/// <summary>
/// The one place a Set's per-item JSON files are parsed. Every reader — the Set browser, the
/// reports, <c>extend</c>, export — goes through here, so a damaged file is refused the same way
/// everywhere: with a <see cref="CorruptSetException"/> that names it. Before this, a literal
/// <c>null</c> was dropped silently by one reader and crashed another, and unparsable JSON escaped
/// as the framework's <see cref="JsonException"/>.
/// </summary>
internal static class SetItemFile
{
    /// <summary>Parses a rich <c>nfty/NNNN.json</c>.</summary>
    /// <param name="path">The file, for the message.</param>
    /// <param name="json">Its contents.</param>
    /// <returns>The record.</returns>
    /// <exception cref="CorruptSetException">Unparsable, a literal null, or missing a required field.</exception>
    internal static NftyMetadata ReadNfty(string path, string json)
    {
        var m = Parse<NftyMetadata>(path, json);
        if (m.Dna is null || m.Recipe is null || m.Rarity is null || m.Layers is null)
            throw new CorruptSetException(path,
                $"Set item '{path}' is missing required fields (dna, recipe, rarity or layers).");
        return m;
    }

    /// <summary>Parses a standards-pure <c>metadata/NNNN.json</c>.</summary>
    /// <param name="path">The file, for the message.</param>
    /// <param name="json">Its contents.</param>
    /// <returns>The record.</returns>
    /// <exception cref="CorruptSetException">Unparsable, a literal null, or missing a required field.</exception>
    internal static OpenSeaMetadata ReadOpenSea(string path, string json)
    {
        var m = Parse<OpenSeaMetadata>(path, json);
        if (m.Attributes is null)
            throw new CorruptSetException(path, $"Set item '{path}' is missing its 'attributes'.");
        return m;
    }

    /// <summary>Reads only the DNA and number an <c>extend</c> needs from an nfty file.</summary>
    /// <param name="path">The file, for the message.</param>
    /// <param name="json">Its contents.</param>
    /// <returns>The DNA and set number.</returns>
    /// <exception cref="CorruptSetException">Unparsable, a literal null, or missing either field.</exception>
    internal static (string Dna, int SetNumber) ReadIdentity(string path, string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw new CorruptSetException(path,
                $"Set item '{path}' is not valid JSON, so extend cannot tell which assets already "
                + $"exist: {ex.Message}");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new CorruptSetException(path,
                    $"Set item '{path}' is not a JSON object, so extend cannot tell which assets "
                    + "already exist.");
            if (!doc.RootElement.TryGetProperty("dna", out var dna)
                || dna.ValueKind != JsonValueKind.String || dna.GetString() is not { } dnaText)
                throw new CorruptSetException(path,
                    $"Set item '{path}' has no 'dna' field, so extend cannot tell which assets "
                    + "already exist.");
            if (!doc.RootElement.TryGetProperty("setNumber", out var number)
                || !number.TryGetInt32(out int setNumber))
                throw new CorruptSetException(path,
                    $"Set item '{path}' has no usable 'setNumber' field, so extend cannot tell "
                    + "where to continue numbering.");
            return (dnaText, setNumber);
        }
    }

    private static T Parse<T>(string path, string json) where T : class
    {
        T? value;
        try { value = JsonSerializer.Deserialize<T>(json, Json.Options); }
        catch (JsonException ex)
        {
            throw new CorruptSetException(path, $"Set item '{path}' is not readable: {ex.Message}");
        }
        return value ?? throw new CorruptSetException(path, $"Set item '{path}' is empty (a JSON null).");
    }
}
