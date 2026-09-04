using System.Text.Json;
using Nfty.Core.Imaging;

namespace Nfty.App.Services;

/// <summary>The app-wide palette — the swatches the user has saved, shared across every CookBook.
///
/// <para>The other scope is the open CookBook's own palette, which travels inside the archive so a
/// collection's colors survive being handed to someone else. With a book open its swatches show
/// first and these sit beneath; with no book open these are all there is. That resolution is
/// <see cref="Palette.Combine"/>, in Core, so both front-ends agree on the precedence.</para>
///
/// <para>The ten-slot ramp is not stored at all: it is computed from the mode, which is editor
/// state. Only what the user actually mixed is worth persisting.</para></summary>
public interface IPaletteService
{
    /// <summary>The saved swatches, in the order they were saved.</summary>
    IReadOnlyList<RgbColor> Swatches { get; }

    /// <summary>Saves a swatch. Re-saving one already present is a no-op.</summary>
    /// <param name="swatch">The color to save.</param>
    void Add(RgbColor swatch);

    /// <summary>Forgets a swatch. Removing one that is not saved is a no-op.</summary>
    /// <param name="swatch">The color to forget.</param>
    void Remove(RgbColor swatch);
}

/// <summary>The app palette, persisted in the <see cref="IStateStore"/> as a list of prefixed color
/// specs — the same form an author types, so the file stays readable and hand-editable.
///
/// <para>Convenience state throughout: a corrupt file loads empty, a failed save is swallowed, and
/// an unwritable store keeps the swatches for the session instead of refusing them. None of it ever
/// blocks or crashes the editor.</para></summary>
/// <inheritdoc cref="IPaletteService"/>
public sealed class PaletteService : IPaletteService
{
    /// <summary>The store file the swatches live in.</summary>
    public const string FileName = "palette.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly IStateStore _store;
    private readonly List<RgbColor> _swatches;

    /// <summary>Creates the service and loads whatever the store holds.</summary>
    /// <param name="store">Where the swatches are persisted.</param>
    public PaletteService(IStateStore store)
    {
        _store = store;
        _swatches = Load(store.Read(FileName));
    }

    /// <inheritdoc />
    public IReadOnlyList<RgbColor> Swatches => _swatches;

    /// <inheritdoc />
    public void Add(RgbColor swatch)
    {
        if (_swatches.Contains(swatch)) return;
        _swatches.Add(swatch);
        Save();
    }

    /// <inheritdoc />
    public void Remove(RgbColor swatch)
    {
        if (_swatches.RemoveAll(c => c == swatch) == 0) return;
        Save();
    }

    private static List<RgbColor> Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            // FromSpecs skips what it cannot parse, so one mangled swatch costs only itself; a file
            // that is not JSON at all lands here and loads as empty.
            return Palette.FromSpecs(JsonSerializer.Deserialize<List<string?>>(json, Json)).ToList();
        }
        catch { return new(); }
    }

    private void Save() =>
        _store.Write(FileName, JsonSerializer.Serialize(Palette.ToSpecs(_swatches), Json));
}
