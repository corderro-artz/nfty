using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Converters;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The mint-distribution colors: generated per recipe rather than drawn from a fixed set, so a book
/// may hold any number of recipes and no two segments look alike.
/// </summary>
/// <remarks>
/// <para>There were six tokens, assigned by position and cycling, so the seventh recipe repeated the
/// first — an eight-recipe bar carried two pairs a reader could not tell apart. The view model now
/// supplies only a hue SHIFT and the converter seats it on the palette's anchor.</para>
///
/// <para>Every assertion here reads the brush off a control in a laid-out frame, not off the
/// converter alone: the thing that could break is the plumbing — a class that stopped matching, a
/// binding whose inputs never arrive — and a converter unit test would pass through all of it.</para>
/// </remarks>
public class SeriesColorTests
{
    /// <summary>Renders the Explorer on a book of <paramref name="count"/> recipes.</summary>
    /// <param name="count">How many recipes the book holds.</param>
    /// <param name="variant">The theme to render in.</param>
    /// <param name="window">The host, for the caller to close.</param>
    /// <returns>The laid-out Explorer view.</returns>
    private static Views.ExplorerView Render(int count, ThemeVariant variant, out Window window)
    {
        var book = CookBookDetailViewModelTests.ManyRecipeBook(count);
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = explorer };
        window = new Window
        {
            RequestedThemeVariant = variant,
            Content = view,
            Width = 1180,
            Height = 720,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    /// <summary>The mint bar's segment colors, left to right.</summary>
    /// <param name="view">A laid-out Explorer.</param>
    /// <returns>One color per segment.</returns>
    private static List<Color> Segments(Visual view)
    {
        var bar = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("distbar"));
        return bar.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("series"))
            .Select(b => Assert.IsType<SolidColorBrush>(b.Background).Color)
            .ToList();
    }

    [AvaloniaFact]
    public void Every_segment_of_a_ten_recipe_bar_is_its_own_color()
    {
        // THE BUG THIS REPLACED, at the size that showed it: with six cycling tokens, segments 7-10
        // repeated 1-4. Ten is past the old ceiling in both directions - it proves the generator has
        // none, and that the extra colors are real rather than merely unequal.
        var view = Render(10, ThemeVariant.Dark, out var window);
        try
        {
            var colors = Segments(view);
            Assert.Equal(10, colors.Count);
            Assert.Equal(10, colors.Distinct().Count());

            // Distinct is not enough - two colors one step apart are the same defect as two equal
            // ones. Compare on the wheel, where 359 deg and 1 deg are neighbours.
            var hues = colors.Select(c => c.ToHsl().H).ToList();
            for (int i = 0; i < hues.Count; i++)
            {
                for (int j = i + 1; j < hues.Count; j++)
                {
                    double d = Math.Abs(hues[i] - hues[j]);
                    Assert.True(Math.Min(d, 360 - d) > 5,
                        $"segments {i} and {j} are {Math.Min(d, 360 - d):F1} deg apart");
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_generated_color_keeps_the_palette_s_saturation_and_lightness()
    {
        // What stops "infinitely many colors" becoming "any color at all". Only the hue is
        // generated: everything else is the anchor token's, so a generated segment is exactly as
        // dark and as saturated as the hand-picked color it was turned from - which is what keeps it
        // legible on its ground and at home beside the kind colors.
        var view = Render(6, ThemeVariant.Dark, out var window);
        try
        {
            Assert.True(Application.Current!.TryGetResource(
                "SeriesAnchorBrush", ThemeVariant.Dark, out object? found));
            var anchor = Assert.IsType<SolidColorBrush>(found).Color.ToHsl();

            var colors = Segments(view);
            Assert.Equal(anchor.H, colors[0].ToHsl().H, 1);      // the first row IS the anchor

            foreach (var c in colors)
            {
                var hsl = c.ToHsl();
                Assert.Equal(anchor.S, hsl.S, 2);
                Assert.Equal(anchor.L, hsl.L, 2);
                Assert.Equal(255, c.A);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_series_follows_the_theme()
    {
        // The reason the hue is generated in a CONVERTER that takes the theme variant as an input
        // rather than picked in the view model: a Color chosen at that level cannot re-resolve when
        // the theme flips, which is the defect that made these tokens in the first place. The bar is
        // the heaviest element on the card, so a light-mode palette rendered in dark mode is the
        // whole card looking wrong.
        var dark = Render(6, ThemeVariant.Dark, out var w1);
        List<Color> inDark;
        try { inDark = Segments(dark); }
        finally { w1.Close(); }

        var light = Render(6, ThemeVariant.Light, out var w2);
        try
        {
            var inLight = Segments(light);

            // The two anchors do NOT share a hue - #3b5b6f is 203 deg and #7fb0c4 is 197, because
            // they were picked by eye one theme at a time - so the assertion is that the series is
            // the same SHAPE in both, turned as a whole. That is the property the generator owns;
            // where the wheel starts belongs to the palette, and asserting it here would be
            // asserting that two swatches happen to match.
            for (int i = 1; i < inLight.Count; i++)
            {
                double l = inLight[i].ToHsl().H - inLight[0].ToHsl().H;
                double d = inDark[i].ToHsl().H - inDark[0].ToHsl().H;
                Assert.True(Math.Abs(l - d) < 1.5,
                    $"segment {i} sits {l:F1} deg off the first in light and {d:F1} in dark");
            }

            // And the dark set is the lighter one, exactly as the two anchor swatches are.
            Assert.All(inLight.Zip(inDark), p =>
                Assert.True(p.Second.ToHsl().L > p.First.ToHsl().L,
                    "the dark theme's series should be the lighter of the two"));
        }
        finally { w2.Close(); }
    }

    [AvaloniaFact]
    public void A_row_paints_its_dot_and_its_track_in_the_row_s_own_color()
    {
        // Three surfaces draw a recipe's color - the dot, the share track, and the bar segment - and
        // they are three separate controls. They agree because they read one number off one row, and
        // this is what would catch a fourth surface being added that computes its own.
        var view = Render(6, ThemeVariant.Dark, out var window);
        try
        {
            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            var rows = card.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("crow")).ToList();
            Assert.NotEmpty(rows);

            var segments = Segments(view);
            for (int i = 0; i < rows.Count; i++)
            {
                var painted = rows[i].GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("series"))
                    .Select(b => Assert.IsType<SolidColorBrush>(b.Background).Color)
                    .ToList();

                Assert.Equal(2, painted.Count);               // the dot and the share track
                Assert.Single(painted.Distinct());
                Assert.Equal(segments[i], painted[0]);
            }
        }
        finally { window.Close(); }
    }

    [Fact]
    public void A_shift_turns_the_hue_and_leaves_everything_else()
    {
        // The arithmetic on its own, including the wrap: a shift past 360 comes back round rather
        // than saturating at red, which is what makes the sequence unbounded.
        var anchor = Color.FromRgb(0x7f, 0xb0, 0xc4);
        var src = anchor.ToHsl();

        // Tolerances rather than equalities, and the reason is 8 bits: a color is stored as three
        // bytes, so the hue that comes back out of one is quantized - about half a degree here, and
        // coarser still on a near-gray, where a whole hue range collapses onto the same bytes. That
        // is the floor on how finely any generated palette can be spaced, and it is far below the
        // 137 deg this one uses.
        var turned = SeriesBrushConverter.Shift(anchor, 90);
        var got = turned.ToHsl();
        Assert.True(Math.Abs((src.H + 90) % 360 - got.H) < 1,
            $"turned 90 deg from {src.H:F1} and landed on {got.H:F1}");
        Assert.Equal(src.S, got.S, 2);
        Assert.Equal(src.L, got.L, 2);

        Assert.Equal(SeriesBrushConverter.Shift(anchor, 0), SeriesBrushConverter.Shift(anchor, 360));
        Assert.InRange(SeriesBrushConverter.Shift(anchor, 350).ToHsl().H, 0, 360);
    }
}
