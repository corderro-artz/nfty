using Avalonia.Data.Converters;
using Nfty.App.ViewModels;

namespace Nfty.App.Converters;

/// <summary>One row of a chunked thumbnail grid (view-only grouping, not a ViewModel projection).</summary>
public record TileRow(IReadOnlyList<SetItemRow> Tiles);

/// <summary>Chunks a flat item list into fixed-size <see cref="TileRow"/> rows so a virtualizing
/// ListBox — which realizes one element per top-level ItemsSource entry — can show several
/// thumbnails per row while still virtualizing over "row" elements for a large Set.
///
/// Why chunk instead of a wrapping panel: Avalonia 11.2.3 (this project's pinned version, verified
/// against the actual installed assembly, not just docs) has no virtualizing wrap/uniform-grid
/// layout — <c>ItemsRepeater</c>/<c>UniformGridLayout</c> do not exist in this package version
/// despite appearing in newer Context7 docs, and swapping a ListBox's ItemsPanel to WrapPanel (the
/// only wrapping panel that does exist) disables virtualization entirely, since WrapPanel implements
/// no virtualizing panel interface. ListBox's default ItemsPanel — VirtualizingStackPanel — only
/// virtualizes a single vertical/horizontal run, so grouping fixed-size rows ourselves is the only
/// way to keep true virtualization for a large Set while still presenting a grid.</summary>
public static class RowChunkConverter
{
    private const int TilesPerRow = 4;

    public static readonly IValueConverter By4 =
        new FuncValueConverter<IReadOnlyList<SetItemRow>?, IReadOnlyList<TileRow>>(items =>
            items is null
                ? Array.Empty<TileRow>()
                : items.Chunk(TilesPerRow).Select(chunk => new TileRow(chunk)).ToList());
}
