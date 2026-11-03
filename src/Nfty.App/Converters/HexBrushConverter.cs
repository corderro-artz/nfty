using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Nfty.App.Converters;

/// <summary>
/// A reader-typed <c>#RRGGBB</c> as a brush: the viewer's Custom backdrop swatch shows the color the
/// reader chose, which no theme token can name. Anything that does not parse draws nothing, so a
/// half-typed value leaves the swatch empty rather than throwing.
/// </summary>
public sealed class HexBrushConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static readonly HexBrushConverter Instance = new();

    /// <summary>Parses the color.</summary>
    /// <param name="value">The text.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>A solid brush, or null.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && Color.TryParse(s, out var c) ? new SolidColorBrush(c) : null;

    /// <summary>Not supported: the swatch is drawn, never edited.</summary>
    /// <param name="value">Ignored.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Never.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
