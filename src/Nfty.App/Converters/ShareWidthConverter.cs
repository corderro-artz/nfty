using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>
/// Scales a 0-100 share onto the width its track <em>actually has</em>, rather than onto a width it
/// was assumed to have.
/// </summary>
/// <remarks>
/// <para><see cref="PercentToWidthConverter"/> multiplies by a constant — <c>share * 3.1</c> for a
/// 310px track — which is exact only while the track really is 310px. Three of the four bars in this
/// app pin their track (<c>.distbar</c> is <c>Width="310"</c>, <c>.rt</c> is <c>Width="120"</c>,
/// <c>.mixbar</c> is <c>Width="270"</c>) and are fine. The DNA-space bar's track is <c>.cbar</c>,
/// which has no width and sits in a star column, so it is wider than 310 at any real pane size — and
/// a recipe holding 100% of mints drew a bar about four fifths full. The number was right; the track
/// it was measured against was not.</para>
///
/// <para>So take the track's own width as an input. That removes the magic multiplier as well as the
/// bug: a bar is <em>this fraction of the space it is in</em>, which is what a share bar means.</para>
/// </remarks>
public sealed class ShareWidthConverter : IMultiValueConverter
{
    /// <summary>The shared instance; the converter holds no state.</summary>
    public static readonly ShareWidthConverter Instance = new();

    /// <summary>Multiplies a percentage by its track's width.</summary>
    /// <param name="values">The share (0-100), then the track's width in pixels.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>The fill width, or 0 while either input is still unset — a bar cannot be measured
    /// before its track has been, and a mid-layout pass hands both through as unset.</returns>
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return 0d;
        if (values[0] is not double share || values[1] is not double track) return 0d;
        if (double.IsNaN(share) || double.IsNaN(track) || track <= 0) return 0d;
        return Math.Clamp(share, 0, 100) / 100d * track;
    }
}
