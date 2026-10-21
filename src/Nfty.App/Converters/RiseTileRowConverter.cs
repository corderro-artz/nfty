using System.Globalization;
using Avalonia.Data.Converters;
using Nfty.App.Rise;

namespace Nfty.App.Converters;

/// <summary>One row of the Rise page's tile rail: up to <see cref="RiseTileRowConverter.PerRow"/> tiles.</summary>
/// <param name="Tiles">The items in this row.</param>
public sealed record RiseTileRow(IReadOnlyList<RiseItem> Tiles);

/// <summary>
/// Chunks the rail's items into rows of three, so a virtualizing <c>ListBox</c> (which realizes one
/// element per top-level item) can show a grid of tiles while still realizing only the rows on screen.
/// </summary>
/// <remarks>
/// The rail is a fixed 262px wide, so the count per row is a constant and no width input is needed
/// (the Set browser's <see cref="RowChunkConverter"/> has to follow a resizing pane; this one does
/// not). The last answer is kept and returned verbatim for the same item list: a new collection
/// instance makes the ListBox discard every realized container.
/// </remarks>
public sealed class RiseTileRowConverter : IValueConverter
{
    /// <summary>Tiles per row.</summary>
    public const int PerRow = 3;

    /// <summary>The shared instance markup binds to.</summary>
    public static readonly RiseTileRowConverter Instance = new();

    private IReadOnlyList<RiseItem>? _forItems;
    private IReadOnlyList<RiseTileRow>? _rows;

    private RiseTileRowConverter() { }

    /// <summary>Chunks the items.</summary>
    /// <param name="value">The rail's items.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>The rows; empty for no items.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IReadOnlyList<RiseItem> items || items.Count == 0) return Array.Empty<RiseTileRow>();
        if (_rows is not null && ReferenceEquals(_forItems, items)) return _rows;

        var rows = new List<RiseTileRow>((items.Count + PerRow - 1) / PerRow);
        for (int i = 0; i < items.Count; i += PerRow)
        {
            int take = Math.Min(PerRow, items.Count - i);
            var tiles = new RiseItem[take];
            for (int j = 0; j < take; j++) tiles[j] = items[i + j];
            rows.Add(new RiseTileRow(tiles));
        }
        _forItems = items;
        _rows = rows;
        return rows;
    }

    /// <summary>Not supported: the grouping is one way.</summary>
    /// <param name="value">Ignored.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
