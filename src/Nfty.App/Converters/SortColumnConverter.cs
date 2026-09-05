using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>
/// True when the bound value equals the converter parameter, ordinally. Used by a sortable column
/// header to light its own arrow: the header knows its column key, the ViewModel knows which column
/// is active, and this is the one comparison between them.
///
/// <para>A converter rather than a bool per column on each ViewModel, because that shape does not
/// scale — the variant table alone has four sortable columns, and each would need a property, a
/// notification and a place to be forgotten.</para>
/// </summary>
public sealed class SortColumnConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static readonly SortColumnConverter Instance = new();

    private SortColumnConverter() { }

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string v && parameter is string p && string.Equals(v, p, StringComparison.Ordinal);

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("SortColumnConverter is one-way: a header reads which column is active.");
}
