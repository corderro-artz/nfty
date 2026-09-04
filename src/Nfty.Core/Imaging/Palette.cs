namespace Nfty.Core.Imaging;

/// <summary>Which ramp a <see cref="Palette"/>'s ten slots currently offer.
///
/// The mode is what the palette OFFERS, never what the artwork is. Swapping it hands the author a
/// different set of colors to pick from and changes no pixel that has already been painted —
/// recoloring existing art through a ramp is a separate feature with its own mapping curve, preview
/// and undo entry, and is deliberately not this.</summary>
public enum PaletteMode
{
    /// <summary>Ten ascending grays — what a value-map is authored in, since a Dynamic or Static
    /// layer takes its lightness from the gray and its hue and saturation from the colorization.</summary>
    Grayscale,

    /// <summary>Ten hues around the wheel — full-color painting, which only ever saves as a
    /// <c>Custom</c> ingredient.</summary>
    Color,
}

/// <summary>
/// The palette a painting front-end offers: <see cref="Slots"/> ramp entries that change with the
/// <see cref="Mode"/>, plus the swatches the user has saved.
///
/// <para>It lives in Core, not in a front-end, for the same reason <c>VariantPreview</c> does: the
/// CLI and the GUI must agree on what "the fifth gray" is, and two copies is how that stops being
/// true. The ramps are also <em>computed</em> rather than typed out, so the grays are reproducible
/// and evenly spaced by construction instead of by whoever transcribed them.</para>
///
/// <para>Both ramps are exactly <see cref="Slots"/> long, and swapping the mode reuses the same ten
/// slots — the palette's shape never changes, only its contents.</para>
/// </summary>
/// <param name="Mode">Which ramp <see cref="Ramp"/> reports.</param>
/// <param name="Swatches">The user's saved colors, in the order they were saved. Unaffected by
/// <see cref="Mode"/>: a swatch mixed in color mode is still there in grayscale mode, because a
/// saved color is user data rather than a property of the ramp.</param>
public sealed record Palette(PaletteMode Mode, IReadOnlyList<RgbColor> Swatches)
{
    /// <summary>How many ramp slots there are, in either mode.</summary>
    public const int Slots = 10;

    /// <summary>A grayscale palette with no saved swatches — the state a first run starts in, and
    /// the state a corrupt or unreachable store loads as.</summary>
    public static Palette Empty { get; } = new(PaletteMode.Grayscale, Array.Empty<RgbColor>());

    /// <summary>Ten ascending grays from black to white, evenly spaced across the full 0..255 range.
    ///
    /// <para>Both endpoints are included — unlike the half-open ranges <c>ColorRoller</c> samples,
    /// this is a fixed ten-entry table rather than a sampled interval, and a gray ramp that could
    /// not reach white would be missing the color authors reach for most.</para></summary>
    public static IReadOnlyList<RgbColor> GrayRamp { get; } = BuildGrayRamp();

    /// <summary>Ten fully saturated hues spread evenly around the wheel, starting at red.
    ///
    /// <para>The step is 360/10, and the last slot is 324° rather than 360°: 360 wraps to 0, so
    /// including it would spend two of the ten slots on the same red.</para></summary>
    public static IReadOnlyList<RgbColor> RainbowRamp { get; } = BuildRainbowRamp();

    /// <summary>The ramp for a mode.</summary>
    /// <param name="mode">Which ramp is wanted.</param>
    /// <returns>Exactly <see cref="Slots"/> colors.</returns>
    public static IReadOnlyList<RgbColor> RampFor(PaletteMode mode) =>
        mode == PaletteMode.Color ? RainbowRamp : GrayRamp;

    /// <summary>The ten slots this palette currently offers.</summary>
    public IReadOnlyList<RgbColor> Ramp => RampFor(Mode);

    /// <summary>Swaps the ramp, keeping every saved swatch.</summary>
    /// <param name="mode">The mode to switch to.</param>
    /// <returns>The same palette offering the other ramp.</returns>
    public Palette WithMode(PaletteMode mode) => Mode == mode ? this : this with { Mode = mode };

    /// <summary>Saves a swatch, appending it. Re-saving a color already present is a no-op rather
    /// than a duplicate or a reorder — a palette is a board the author arranges, so an entry that
    /// moved because it was picked again would be the palette rearranging itself under them.</summary>
    /// <param name="swatch">The color to save.</param>
    /// <returns>The palette with the swatch in it.</returns>
    public Palette WithSwatch(RgbColor swatch) =>
        Swatches.Contains(swatch) ? this : this with { Swatches = [.. Swatches, swatch] };

    /// <summary>Forgets a swatch. Removing one that is not saved is a no-op.</summary>
    /// <param name="swatch">The color to forget.</param>
    /// <returns>The palette without it.</returns>
    public Palette WithoutSwatch(RgbColor swatch) =>
        Swatches.Contains(swatch)
            ? this with { Swatches = Swatches.Where(c => c != swatch).ToArray() }
            : this;

    /// <summary>
    /// The two palette scopes resolved into one list: the open CookBook's swatches first, the
    /// app-wide ones beneath, with anything present in both appearing once, at its book position.
    /// </summary>
    /// <param name="book">The open CookBook's swatches, or null when no book is open — in which
    /// case the app palette is all there is.</param>
    /// <param name="app">The app-wide swatches from the <c>.nfty</c> store.</param>
    /// <returns>The combined list, in precedence order.</returns>
    public static IReadOnlyList<RgbColor> Combine(
        IEnumerable<RgbColor>? book, IEnumerable<RgbColor>? app)
    {
        var combined = new List<RgbColor>();
        foreach (var c in (book ?? []).Concat(app ?? []))
            if (!combined.Contains(c))
                combined.Add(c);
        return combined;
    }

    /// <summary>
    /// Reads swatches out of the prefixed color specs a manifest or a store file holds.
    ///
    /// <para>An entry that will not parse is SKIPPED rather than thrown on. A palette is convenience
    /// state — one mangled swatch must never be the reason a CookBook cannot be opened — and the
    /// same read has to survive a hand-edited manifest. Anything that must reject a bad spec loudly
    /// should call <see cref="ColorSpec.Parse"/> itself.</para>
    /// </summary>
    /// <param name="specs">Prefixed specs (<c>hex:</c>, <c>rgb:</c>, <c>hsl:</c>, <c>hsv:</c>), or null.</param>
    /// <returns>The colors that parsed, in order.</returns>
    public static IReadOnlyList<RgbColor> FromSpecs(IEnumerable<string?>? specs)
    {
        if (specs is null) return [];
        var colors = new List<RgbColor>();
        foreach (var spec in specs)
        {
            if (spec is null) continue;
            try { colors.Add(ColorSpec.Parse(spec)); }
            catch (FormatException) { /* skipped: one mangled swatch must not cost the book */ }
        }
        return colors;
    }

    /// <summary>Writes swatches as the prefixed <c>hex:</c> specs a manifest or store file holds —
    /// the same form an author types, so a palette stays readable and re-runnable by hand.</summary>
    /// <param name="colors">The swatches to write.</param>
    /// <returns>One spec per color, in order.</returns>
    public static IReadOnlyList<string> ToSpecs(IEnumerable<RgbColor> colors) =>
        colors.Select(ColorSpec.Format).ToArray();

    private static RgbColor[] BuildGrayRamp()
    {
        var ramp = new RgbColor[Slots];
        for (int i = 0; i < Slots; i++)
        {
            var v = (byte)Math.Round(i * 255.0 / (Slots - 1));
            ramp[i] = new RgbColor(v, v, v);
        }
        return ramp;
    }

    private static RgbColor[] BuildRainbowRamp()
    {
        var ramp = new RgbColor[Slots];
        for (int i = 0; i < Slots; i++)
            ramp[i] = ColorConvert.HsvToRgb(i * 360.0 / Slots, 1.0, 1.0);
        return ramp;
    }
}
