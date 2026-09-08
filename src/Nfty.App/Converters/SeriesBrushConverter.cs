using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Nfty.App.Converters;

/// <summary>
/// Paints one mint-distribution series: a hue rotation from the view model, seated on the palette's
/// own saturation and lightness, so a book may have any number of recipes and no two adjacent ones
/// share a color.
/// </summary>
/// <remarks>
/// <para>There were six series tokens and the assignment cycled, so recipe seven repeated recipe
/// one — two indistinguishable segments in the same bar, which is the one property a categorical
/// scale owes. Generating the hue removes the ceiling entirely.</para>
///
/// <para><b>Generated, not random.</b> A color rolled at render time would repaint the same book
/// differently on every launch, and would put two near-identical hues beside each other about as
/// often as chance allows — which is the defect this replaced, reintroduced. The rotation is the
/// golden angle off the row's position (<see cref="ViewModels.RecipeShareRow.HueShift"/>), which is
/// the arrangement that keeps consecutive values as far apart on the wheel as any sequence can.</para>
///
/// <para><b>The palette still owns the color.</b> Only the hue is computed; saturation, lightness
/// and where the wheel starts are read from <c>SeriesAnchorBrush</c> in whichever theme dictionary
/// is live — so these stay on-palette by construction, and the light set stays dark against a light
/// ground while the dark set stays light. That is what the previous hash-to-HSV version could not
/// do, and why it was replaced by tokens: a <see cref="Color"/> chosen in a view model cannot
/// re-resolve when the theme flips. This converter takes the theme variant as an INPUT, so a flip
/// re-runs it — the binding is what makes generating a color admissible here at all.</para>
/// </remarks>
public sealed class SeriesBrushConverter : IMultiValueConverter
{
    /// <summary>The shared instance; the converter holds no state.</summary>
    public static readonly SeriesBrushConverter Instance = new();

    /// <summary>The token whose hue, saturation and lightness the generated colors are seated on.</summary>
    private const string AnchorKey = "SeriesAnchorBrush";

    /// <summary>Builds the brush for one row.</summary>
    /// <param name="values">The row's hue shift in degrees, the control being painted (a resource
    /// host, so the anchor is read from the dictionary that is actually in scope), and the control's
    /// <see cref="StyledElement.ActualThemeVariant"/> — present so a theme flip re-runs this.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>A brush, or unset while an input has not arrived — which leaves the fallback the
    /// <c>Border.series</c> style paints, rather than a transparent hole that reads as a bug.</returns>
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3) return AvaloniaProperty.UnsetValue;
        if (values[0] is not double shift || double.IsNaN(shift)) return AvaloniaProperty.UnsetValue;
        if (values[1] is not IResourceHost host) return AvaloniaProperty.UnsetValue;

        var variant = values[2] as ThemeVariant;
        if (!host.TryFindResource(AnchorKey, variant, out object? found)
            || found is not ISolidColorBrush anchor)
        {
            return AvaloniaProperty.UnsetValue;
        }

        return new SolidColorBrush(Shift(anchor.Color, shift));
    }

    /// <summary>Rotates a color's hue, keeping its saturation and lightness.</summary>
    /// <param name="anchor">The palette's series color for the live theme.</param>
    /// <param name="degrees">How far around the wheel to turn it.</param>
    /// <returns>The rotated color, fully opaque.</returns>
    /// <remarks>
    /// HSL rather than HSV: lightness is the axis the two theme sets differ on (the light anchor
    /// sits at about 33%, the dark one at 63%), so holding it fixed is what keeps a generated color
    /// as legible against its ground as the anchor it came from.
    /// </remarks>
    public static Color Shift(Color anchor, double degrees)
    {
        var hsl = anchor.ToHsl();
        double hue = (hsl.H + degrees) % 360;
        if (hue < 0) hue += 360;
        return new HslColor(1, hue, hsl.S, hsl.L).ToRgb();
    }
}
