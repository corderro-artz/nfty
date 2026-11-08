using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>
/// Scales a percentage or fraction onto its track's measured width.
/// </summary>
/// <remarks>
/// Reading the track width through the binding keeps fixed and stretching bars under one rule;
/// changing a layout width requires no matching multiplier in code. Unset inputs render an empty
/// fill until layout supplies the track width.
/// </remarks>
public sealed class ShareWidthConverter : IMultiValueConverter
{
    /// <summary>What the first input means.</summary>
    public enum ShareScale
    {
        /// <summary>0-100: the percentages the rarity and mint-distribution bars carry.</summary>
        Percent,

        /// <summary>0-1: a fraction, as the Rise thickness rows carry.</summary>
        Fraction,
    }

    /// <summary>The shared instance for a share in percent; the converter holds no state.</summary>
    public static readonly ShareWidthConverter Instance = new(ShareScale.Percent);

    /// <summary>The shared instance for a share that is a 0-1 fraction.</summary>
    public static readonly ShareWidthConverter FractionInstance = new(ShareScale.Fraction);

    private readonly ShareScale _scale;

    private ShareWidthConverter(ShareScale scale) => _scale = scale;

    /// <summary>Multiplies a share by its track's width.</summary>
    /// <param name="values">The share (0-100, or 0-1 for <see cref="FractionInstance"/>), then the
    /// track's width in pixels.</param>
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
        if (_scale == ShareScale.Fraction) share *= 100;
        return Math.Clamp(share, 0, 100) / 100d * track;
    }
}
