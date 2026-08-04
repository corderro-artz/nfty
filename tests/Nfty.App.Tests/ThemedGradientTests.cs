using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Guards that gradient fills actually change with the theme.
///
/// A gradient built inside a Style <c>Setter</c> with <c>DynamicResource</c> GradientStops does NOT
/// re-resolve when the theme flips — the brush is constructed once and shared, and its stops keep
/// whatever the values were when the Style was first applied. That shipped the CookBook identity
/// card painting a LIGHT gradient in dark mode: near-white text on cream, functionally illegible,
/// while every solid-brush token on the same card was correct.
///
/// It is a nasty failure because it is invisible in light mode, invisible in the markup, and invisible
/// to any test that only checks the light theme. The fix is to define the whole brush per theme
/// dictionary — the mechanism every other brush here already uses — and these tests pin that: if a
/// key goes missing from one dictionary, or the two are made identical (which is what a
/// stuck-at-light-theme brush effectively is), they fail.</summary>
public class ThemedGradientTests
{
    private static GradientBrush Resolve(string key, ThemeVariant theme)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, theme, out var value),
            $"{key} is not defined for the {theme} theme. Theme-varying gradients must exist in BOTH " +
            "dictionaries — a missing key silently falls back to the other theme's brush.");
        return Assert.IsAssignableFrom<GradientBrush>(value);
    }

    private static Color[] Stops(GradientBrush brush) =>
        brush.GradientStops.Select(s => s.Color).ToArray();

    [AvaloniaTheory]
    [InlineData("CardGradientBrush")]
    [InlineData("ModalGlowBrush")]
    public void Gradient_fills_differ_between_light_and_dark(string key)
    {
        var light = Stops(Resolve(key, ThemeVariant.Light));
        var dark = Stops(Resolve(key, ThemeVariant.Dark));

        Assert.Equal(light.Length, dark.Length);
        Assert.True(
            light.Zip(dark).Any(p => p.First != p.Second),
            $"{key} has identical stops in both themes, so it cannot be tracking the theme. If this " +
            "brush was moved back into a Style Setter with DynamicResource stops, it will render the " +
            "light gradient in dark mode — see the class comment.");
    }

    // The identity card is the one that actually shipped broken, so pin its direction too: the dark
    // card must be darker than the light card, not merely different.
    [AvaloniaFact]
    public void Identity_card_is_dark_in_the_dark_theme()
    {
        var light = Stops(Resolve("CardGradientBrush", ThemeVariant.Light));
        var dark = Stops(Resolve("CardGradientBrush", ThemeVariant.Dark));

        double Luma(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        Assert.True(
            Luma(dark[0]) < Luma(light[0]),
            $"dark card gradient starts at luma {Luma(dark[0]):0.#}, lighter than the light theme's " +
            $"{Luma(light[0]):0.#} — the card is painting the wrong theme's gradient.");
    }
}
