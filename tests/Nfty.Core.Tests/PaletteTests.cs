using Nfty.Core.Imaging;

namespace Nfty.Core.Tests;

/// <summary>The palette model: ten ramp slots that change with the mode, plus saved swatches that do
/// not. The ramps are computed rather than typed out, so these assert the construction (evenly
/// spaced, both endpoints, no repeated hue) rather than a transcribed table — a hand-written
/// expected list would only re-state whatever was typed.</summary>
public class PaletteTests
{
    // ---- the ramps -----------------------------------------------------------------------------

    [Fact]
    public void Both_ramps_fill_exactly_the_same_ten_slots()
    {
        Assert.Equal(10, Palette.Slots);
        Assert.Equal(Palette.Slots, Palette.GrayRamp.Count);
        Assert.Equal(Palette.Slots, Palette.RainbowRamp.Count);
    }

    [Fact]
    public void The_gray_ramp_ascends_from_black_to_white()
    {
        Assert.Equal(new RgbColor(0, 0, 0), Palette.GrayRamp[0]);
        Assert.Equal(new RgbColor(255, 255, 255), Palette.GrayRamp[^1]);

        for (int i = 1; i < Palette.Slots; i++)
            Assert.True(Palette.GrayRamp[i].R > Palette.GrayRamp[i - 1].R,
                $"slot {i} ({Palette.GrayRamp[i].R}) must be lighter than slot {i - 1}");
    }

    [Fact]
    public void Every_gray_is_actually_gray()
    {
        // A "gray" with independent channels would be a color that merely looks neutral, and the
        // ramp is what a value-map is authored in.
        foreach (var g in Palette.GrayRamp)
        {
            Assert.Equal(g.R, g.G);
            Assert.Equal(g.G, g.B);
        }
    }

    [Fact]
    public void The_gray_ramp_is_evenly_spaced_across_the_full_range()
    {
        // Reproducible by construction: step = 255/9, so no gap is more than a rounding unit off.
        var step = 255.0 / (Palette.Slots - 1);
        for (int i = 0; i < Palette.Slots; i++)
            Assert.Equal((byte)Math.Round(i * step), Palette.GrayRamp[i].R);
    }

    [Fact]
    public void The_rainbow_ramp_starts_at_red_and_spends_no_slot_twice()
    {
        Assert.Equal(new RgbColor(255, 0, 0), Palette.RainbowRamp[0]);

        // 360 wraps to 0, so a ramp that included it would spend two of the ten slots on red.
        Assert.Equal(Palette.Slots, Palette.RainbowRamp.Distinct().Count());
    }

    [Fact]
    public void The_rainbow_ramp_walks_the_wheel_in_even_steps()
    {
        for (int i = 0; i < Palette.Slots; i++)
        {
            var (h, s, v) = ColorConvert.RgbToHsv(Palette.RainbowRamp[i]);
            Assert.Equal(i * 360.0 / Palette.Slots, h, 1);
            Assert.Equal(1.0, s, 2);   // fully saturated
            Assert.Equal(1.0, v, 2);   // and at full value
        }
    }

    [Theory]
    [InlineData(PaletteMode.Grayscale)]
    [InlineData(PaletteMode.Color)]
    public void The_mode_selects_the_ramp(PaletteMode mode)
    {
        var expected = mode == PaletteMode.Color ? Palette.RainbowRamp : Palette.GrayRamp;

        Assert.Same(expected, Palette.RampFor(mode));
        Assert.Same(expected, (Palette.Empty with { Mode = mode }).Ramp);
    }

    // ---- swapping offers colors, it does not touch artwork -------------------------------------

    [Fact]
    public void Swapping_the_ramp_changes_what_is_offered_and_nothing_else()
    {
        var red = new RgbColor(200, 30, 30);
        var gray = Palette.Empty.WithSwatch(red).WithSwatch(new RgbColor(9, 9, 9));

        var color = gray.WithMode(PaletteMode.Color);

        // The ten slots are different...
        Assert.NotEqual(gray.Ramp, color.Ramp);
        // ...and the user's own colors are untouched, in the same order.
        Assert.Equal(gray.Swatches, color.Swatches);
        // Swapping back is a round trip, not a lossy one.
        Assert.Equal(gray, color.WithMode(PaletteMode.Grayscale));
    }

    [Fact]
    public void Swapping_to_the_mode_already_set_is_the_same_palette()
        => Assert.Same(Palette.Empty, Palette.Empty.WithMode(PaletteMode.Grayscale));

    // ---- swatches ------------------------------------------------------------------------------

    [Fact]
    public void Saving_a_swatch_appends_it_and_leaves_the_original_alone()
    {
        var first = Palette.Empty.WithSwatch(new RgbColor(1, 2, 3));
        var second = first.WithSwatch(new RgbColor(4, 5, 6));

        Assert.Empty(Palette.Empty.Swatches);
        Assert.Equal(new[] { new RgbColor(1, 2, 3) }, first.Swatches);
        Assert.Equal(new[] { new RgbColor(1, 2, 3), new RgbColor(4, 5, 6) }, second.Swatches);
    }

    [Fact]
    public void Saving_a_swatch_already_present_neither_duplicates_nor_reorders_it()
    {
        var p = Palette.Empty.WithSwatch(new RgbColor(1, 2, 3)).WithSwatch(new RgbColor(4, 5, 6));

        var again = p.WithSwatch(new RgbColor(1, 2, 3));

        Assert.Same(p, again);
        Assert.Equal(new[] { new RgbColor(1, 2, 3), new RgbColor(4, 5, 6) }, again.Swatches);
    }

    [Fact]
    public void Forgetting_a_swatch_removes_only_that_one()
    {
        var p = Palette.Empty
            .WithSwatch(new RgbColor(1, 1, 1))
            .WithSwatch(new RgbColor(2, 2, 2))
            .WithSwatch(new RgbColor(3, 3, 3));

        var without = p.WithoutSwatch(new RgbColor(2, 2, 2));

        Assert.Equal(new[] { new RgbColor(1, 1, 1), new RgbColor(3, 3, 3) }, without.Swatches);
        Assert.Same(p, p.WithoutSwatch(new RgbColor(9, 9, 9)));   // not present → no-op
    }

    // ---- the two scopes ------------------------------------------------------------------------

    [Fact]
    public void The_books_swatches_come_first_and_the_app_palette_sits_beneath()
    {
        var book = new[] { new RgbColor(1, 1, 1), new RgbColor(2, 2, 2) };
        var app = new[] { new RgbColor(3, 3, 3) };

        Assert.Equal(new[] { new RgbColor(1, 1, 1), new RgbColor(2, 2, 2), new RgbColor(3, 3, 3) },
            Palette.Combine(book, app));
    }

    [Fact]
    public void A_color_in_both_scopes_appears_once_at_its_book_position()
    {
        var shared = new RgbColor(7, 7, 7);
        var book = new[] { shared, new RgbColor(1, 1, 1) };
        var app = new[] { new RgbColor(2, 2, 2), shared };

        Assert.Equal(new[] { shared, new RgbColor(1, 1, 1), new RgbColor(2, 2, 2) },
            Palette.Combine(book, app));
    }

    [Fact]
    public void With_no_book_open_the_app_palette_is_all_there_is()
    {
        var app = new[] { new RgbColor(3, 3, 3) };

        Assert.Equal(app, Palette.Combine(null, app));
        Assert.Empty(Palette.Combine(null, null));
    }

    // ---- specs ---------------------------------------------------------------------------------

    [Fact]
    public void Swatches_round_trip_through_their_specs()
    {
        var colors = new[] { new RgbColor(214, 36, 159), new RgbColor(0, 0, 0), new RgbColor(255, 255, 255) };

        var specs = Palette.ToSpecs(colors);

        Assert.Equal(new[] { "hex:d6249f", "hex:000000", "hex:ffffff" }, specs);
        Assert.Equal(colors, Palette.FromSpecs(specs));
    }

    [Fact]
    public void A_spec_that_will_not_parse_costs_only_itself()
    {
        // One mangled swatch must never be the reason a CookBook cannot be opened.
        var read = Palette.FromSpecs(new[] { "hex:d6249f", "d6249f", "hex:zzzzzz", null, "rgb:0,128,255" });

        Assert.Equal(new[] { new RgbColor(214, 36, 159), new RgbColor(0, 128, 255) }, read);
    }

    [Fact]
    public void No_specs_at_all_reads_as_an_empty_palette()
    {
        Assert.Empty(Palette.FromSpecs(null));
        Assert.Empty(Palette.FromSpecs(Array.Empty<string?>()));
    }

    [Fact]
    public void Every_ramp_color_survives_a_spec_round_trip_exactly()
    {
        // Format always writes hex for this reason: hsv/hsl resolve through 8-bit RGB and back with
        // rounding at both ends, so a channel could land one off.
        foreach (var c in Palette.GrayRamp.Concat(Palette.RainbowRamp))
            Assert.Equal(c, ColorSpec.Parse(ColorSpec.Format(c)));
    }

    [Fact]
    public void Format_is_lower_case_hex_with_both_digits()
    {
        Assert.Equal("hex:0a0b0c", ColorSpec.Format(new RgbColor(10, 11, 12)));
        Assert.Equal("hex:ffffff", ColorSpec.Format(new RgbColor(255, 255, 255)));
    }
}
