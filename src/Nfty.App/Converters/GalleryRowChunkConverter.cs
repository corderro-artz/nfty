using System.Globalization;
using Avalonia.Data.Converters;
using Nfty.App.Viewer;

namespace Nfty.App.Converters;

/// <summary>One row of the viewer gallery's chunked tile grid.</summary>
/// <param name="Tiles">The tiles in this row.</param>
public record GalleryRow(IReadOnlyList<GalleryItem> Tiles);

/// <summary>
/// Chunks a gallery's tiles into rows that fit the list's width, so a virtualizing ListBox (which
/// realizes one element per row) shows several tiles per row and decodes only the rows on screen.
/// The same arithmetic, and the same reason, as <see cref="RowChunkConverter"/>; the last answer is
/// kept so a resize that changes nothing returns the identical rows instead of rebuilding every tile.
/// </summary>
public sealed class GalleryRowChunkConverter : IMultiValueConverter
{
    private const double TileSlotWidth = 120 + 12;
    private const double TileGap = 10;

    /// <summary>The shared instance markup binds to.</summary>
    public static readonly GalleryRowChunkConverter Instance = new();

    private IReadOnlyList<GalleryItem>? _forItems;
    private int _forPerRow = -1;
    private IReadOnlyList<GalleryRow>? _rows;

    private GalleryRowChunkConverter() { }

    /// <summary>Chunks the tiles for the list's current width.</summary>
    /// <param name="values">The tiles, then the list's width.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Rows of tiles; empty before there are tiles.</returns>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var items = values.Count > 0 ? values[0] as IReadOnlyList<GalleryItem> : null;
        if (items is null || items.Count == 0) return Array.Empty<GalleryRow>();

        var width = values.Count > 1 && values[1] is double w ? w : 0;
        var perRow = width > 0 ? (int)((width + TileGap) / (TileSlotWidth + TileGap)) : 4;
        if (perRow < 1) perRow = 1;

        if (_rows is not null && _forPerRow == perRow && ReferenceEquals(_forItems, items)) return _rows;

        var rows = new List<GalleryRow>((items.Count + perRow - 1) / perRow);
        for (var i = 0; i < items.Count; i += perRow)
        {
            var take = Math.Min(perRow, items.Count - i);
            var tiles = new GalleryItem[take];
            for (var j = 0; j < take; j++) tiles[j] = items[i + j];
            rows.Add(new GalleryRow(tiles));
        }
        _forItems = items;
        _forPerRow = perRow;
        _rows = rows;
        return rows;
    }
}
