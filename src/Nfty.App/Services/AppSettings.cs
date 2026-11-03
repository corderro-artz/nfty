using System.Text.Json;
using System.Text.Json.Nodes;
using Nfty.App.ViewModels;

namespace Nfty.App.Services;

/// <summary>
/// <c>settings.json</c>, the one file every Settings-card choice is kept in, read and written a key
/// at a time.
/// </summary>
/// <remarks>
/// <b>A key at a time, never the whole file.</b> Several services own one key each (the thread
/// count, the canvas size, the theme, the upload provider); each writing a record of only ITS
/// fields would erase every other service's choice on every save. Unknown keys survive, so a file
/// written by a newer build keeps what it holds. Nothing secret is ever written here: the Pinata key
/// lives in the operating system's credential store (<see cref="ISecretStore"/>).
/// </remarks>
public static class SettingsJson
{
    /// <summary>The store file.</summary>
    public const string FileName = "settings.json";

    /// <summary>The file as an object; empty when absent, unreadable or not an object.</summary>
    /// <param name="store">The store.</param>
    /// <returns>The object.</returns>
    public static JsonObject Read(IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var text = store.Read(FileName);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        try { return JsonNode.Parse(text) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    /// <summary>Sets one key and writes the file, keeping every other key. A failed save is swallowed.</summary>
    /// <param name="store">The store.</param>
    /// <param name="key">The camelCase key.</param>
    /// <param name="value">Its value; null writes JSON null.</param>
    public static void Set(IStateStore store, string key, JsonNode? value)
    {
        var doc = Read(store);
        doc[key] = value;
        try { store.Write(FileName, doc.ToJsonString(StateJson.Options)); }
        catch (NotSupportedException) { /* convenience state: never surface */ }
    }

    /// <summary>A string key, or null when absent or not a string. Case-insensitive like every state file.</summary>
    /// <param name="doc">The file.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    public static string? String(JsonObject doc, string key) =>
        Find(doc, key) is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    /// <summary>An integer key, or null when absent or not an integer.</summary>
    /// <param name="doc">The file.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value.</returns>
    public static int? Int(JsonObject doc, string key) =>
        Find(doc, key) is JsonValue v && v.TryGetValue(out int i) ? i : null;

    private static JsonNode? Find(JsonObject doc, string key) =>
        doc.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>Which theme the app opens in.</summary>
public enum ThemeChoice
{
    /// <summary>Follow the operating system.</summary>
    System,

    /// <summary>Always light.</summary>
    Light,

    /// <summary>Always dark.</summary>
    Dark,
}

/// <summary>
/// The GENERAL page of the Settings card: the canvas a new CookBook or Ingredient starts at, and
/// the theme the app opens in.
/// </summary>
public interface IAppSettings
{
    /// <summary>The side, in pixels, a new CookBook's or loose Ingredient's canvas starts at.</summary>
    int DefaultCanvasSide { get; }

    /// <summary>The theme the app opens in.</summary>
    ThemeChoice Theme { get; }

    /// <summary>Remembers the starting canvas side, clamped to <see cref="AppSettingsService.MinCanvasSide"/>..<see cref="AppSettingsService.MaxCanvasSide"/>.</summary>
    /// <param name="side">The side.</param>
    void SetDefaultCanvasSide(int side);

    /// <summary>Remembers the theme. Applying it is the caller's job (<see cref="IThemeService.Apply"/>).</summary>
    /// <param name="theme">The theme.</param>
    void SetTheme(ThemeChoice theme);
}

/// <inheritdoc cref="IAppSettings"/>
public sealed class AppSettingsService : IAppSettings
{
    /// <summary>The smallest starting canvas side.</summary>
    public const int MinCanvasSide = 1;

    /// <summary>The largest starting canvas side: past this a canvas is a painting, not a sprite.</summary>
    public const int MaxCanvasSide = 4096;

    private readonly IStateStore _store;

    /// <summary>Creates the settings and loads what the store holds.</summary>
    /// <param name="store">Where they are persisted.</param>
    public AppSettingsService(IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        var doc = SettingsJson.Read(store);
        DefaultCanvasSide = SettingsJson.Int(doc, "defaultCanvas") is { } side and >= MinCanvasSide and <= MaxCanvasSide
            ? side
            : CanvasAdvice.DefaultSide;
        Theme = Enum.TryParse<ThemeChoice>(SettingsJson.String(doc, "theme"), ignoreCase: true, out var t)
                && Enum.IsDefined(t)
            ? t
            : ThemeChoice.System;
    }

    /// <inheritdoc />
    public int DefaultCanvasSide { get; private set; }

    /// <inheritdoc />
    public ThemeChoice Theme { get; private set; }

    /// <inheritdoc />
    public void SetDefaultCanvasSide(int side)
    {
        side = Math.Clamp(side, MinCanvasSide, MaxCanvasSide);
        if (side == DefaultCanvasSide) return;
        DefaultCanvasSide = side;
        SettingsJson.Set(_store, "defaultCanvas", side);
    }

    /// <inheritdoc />
    public void SetTheme(ThemeChoice theme)
    {
        if (theme == Theme) return;
        Theme = theme;
        SettingsJson.Set(_store, "theme", theme.ToString().ToLowerInvariant());
    }
}
