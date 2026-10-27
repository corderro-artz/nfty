using System.Text.Json;

namespace Nfty.App.Services;

/// <summary>
/// The one JSON dialect the small state files (<c>recents.json</c>, <c>palette.json</c>,
/// <c>editor-view.json</c>, the parallelism setting) share: camelCase out, like every JSON this
/// project writes, and case-INSENSITIVE in, because these files are meant to be hand-edited and a
/// capitalized key is not a corrupt file.
/// </summary>
public static class StateJson
{
    /// <summary>The serializer options every state file reads and writes with.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads a state file's JSON, or null when there is nothing usable: blank text, text that
    /// is not JSON, or JSON of the wrong shape. Only <see cref="JsonException"/> is caught — any other
    /// failure is a bug that should not be swallowed as "start empty".</summary>
    /// <typeparam name="T">The shape the file holds.</typeparam>
    /// <param name="json">The file's text, null when the file is absent.</param>
    /// <returns>The value, or null.</returns>
    public static T? TryRead<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>Writes a value the way every state file is written.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The JSON text.</returns>
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
