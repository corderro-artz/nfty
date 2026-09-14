using System.Text.Json;
using Nfty.Core.Imaging;

namespace Nfty.App.Services;

/// <summary>The app-wide palette — the swatches the user has saved, shared across every CookBook.
///
/// <para><b>There is one palette per <see cref="PaletteMode"/>, not one palette.</b> Grayscale mode
/// paints a value-map, where a color means only its lightness, so a saved vermilion offered there is
/// a cell that silently arms a mid gray; a row of them is a row the author cannot read. The two
/// palettes swap with the mode, so the colors on screen are always the ones the current mode can
/// actually lay down. <see cref="Palette.InMode"/>, in Core, is the rule for the swatches that
/// arrive carrying no mode of their own.</para>
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
    /// <summary>The saved swatches for one mode, in the order they were saved.</summary>
    /// <param name="mode">Which palette is wanted.</param>
    /// <returns>That mode's swatches.</returns>
    IReadOnlyList<RgbColor> SwatchesIn(PaletteMode mode);

    /// <summary>Saves a swatch to a mode's palette. Re-saving one already present is a no-op.</summary>
    /// <param name="swatch">The color to save.</param>
    /// <param name="mode">Which palette to save it in.</param>
    void Add(RgbColor swatch, PaletteMode mode);

    /// <summary>Forgets a swatch. Removing one that is not saved is a no-op.</summary>
    /// <param name="swatch">The color to forget.</param>
    /// <param name="mode">Which palette to forget it from.</param>
    void Remove(RgbColor swatch, PaletteMode mode);
}

/// <summary>The app palette, persisted in the <see cref="IStateStore"/> as one list of prefixed
/// color specs per mode — the same form an author types, so the file stays readable and
/// hand-editable.
///
/// <para>Convenience state throughout: a corrupt file loads empty, a failed save is swallowed, and
/// an unwritable store keeps the swatches for the session instead of refusing them. None of it ever
/// blocks or crashes the editor.</para></summary>
/// <inheritdoc cref="IPaletteService"/>
public sealed class PaletteService : IPaletteService
{
    /// <summary>The store file the swatches live in.</summary>
    public const string FileName = "palette.json";

    // camelCase on the way out to match every other JSON this project writes, and case-INSENSITIVE
    // on the way in because this file is meant to be hand-edited: a palette must never be the reason
    // the editor will not open, and "Color" spelled with a capital is not a corrupt file.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The on-disk shape: one spec list per mode.
    ///
    /// <para>Nullable members because this is read from a file a user may have edited by hand — a
    /// palette must never be the reason the editor will not open, so a missing half loads as
    /// empty.</para></summary>
    private sealed class Stored
    {
        public List<string?>? Grayscale { get; set; }
        public List<string?>? Color { get; set; }
    }

    private readonly IStateStore _store;
    private readonly Dictionary<PaletteMode, List<RgbColor>> _swatches;

    /// <summary>Creates the service and loads whatever the store holds.</summary>
    /// <param name="store">Where the swatches are persisted.</param>
    public PaletteService(IStateStore store)
    {
        _store = store;
        _swatches = Load(store.Read(FileName));
    }

    /// <inheritdoc />
    public IReadOnlyList<RgbColor> SwatchesIn(PaletteMode mode) => _swatches[mode];

    /// <inheritdoc />
    public void Add(RgbColor swatch, PaletteMode mode)
    {
        if (_swatches[mode].Contains(swatch)) return;
        _swatches[mode].Add(swatch);
        Save();
    }

    /// <inheritdoc />
    public void Remove(RgbColor swatch, PaletteMode mode)
    {
        if (_swatches[mode].RemoveAll(c => c == swatch) == 0) return;
        Save();
    }

    private static Dictionary<PaletteMode, List<RgbColor>> Load(string? json)
    {
        var loaded = Fresh();
        if (string.IsNullOrWhiteSpace(json)) return loaded;

        try
        {
            // MIGRATION, and it needs no version field. Older builds wrote a bare JSON ARRAY of
            // specs for one shared palette; the modes are an OBJECT. The two shapes cannot be
            // confused, so which one is on disk is readable from the first character - and a flat
            // list splits by grayness, exactly as Palette.InMode routes a book's own swatches, so a
            // palette saved before this existed lands where saving it today would have put it.
            if (json.TrimStart().StartsWith('['))
            {
                var flat = Palette.FromSpecs(JsonSerializer.Deserialize<List<string?>>(json, Json));
                foreach (var mode in Modes) loaded[mode].AddRange(Palette.InMode(flat, mode));
                return loaded;
            }

            var stored = JsonSerializer.Deserialize<Stored>(json, Json);
            loaded[PaletteMode.Grayscale].AddRange(Palette.FromSpecs(stored?.Grayscale));
            loaded[PaletteMode.Color].AddRange(Palette.FromSpecs(stored?.Color));
            return loaded;
        }
        // FromSpecs skips what it cannot parse, so one mangled swatch costs only itself; a file that
        // is not JSON at all lands here and loads as empty.
        catch { return Fresh(); }
    }

    private static PaletteMode[] Modes => [PaletteMode.Grayscale, PaletteMode.Color];

    private static Dictionary<PaletteMode, List<RgbColor>> Fresh() =>
        Modes.ToDictionary(m => m, _ => new List<RgbColor>());

    private void Save() => _store.Write(FileName, JsonSerializer.Serialize(new Stored
    {
        Grayscale = Palette.ToSpecs(_swatches[PaletteMode.Grayscale]).Cast<string?>().ToList(),
        Color = Palette.ToSpecs(_swatches[PaletteMode.Color]).Cast<string?>().ToList(),
    }, Json));
}
