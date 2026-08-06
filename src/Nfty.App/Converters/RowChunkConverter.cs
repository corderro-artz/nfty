using System.Globalization;
using Avalonia.Data.Converters;
using Nfty.App.ViewModels;

namespace Nfty.App.Converters;

/// <summary>One row of a chunked thumbnail grid (view-only grouping, not a ViewModel projection).</summary>
public record TileRow(IReadOnlyList<SetItemRow> Tiles);

/// <summary>Chunks a flat item list into <see cref="TileRow"/> rows sized to fit the ListBox's
/// current width, so a virtualizing ListBox — which realizes one element per top-level ItemsSource
/// entry — can show several thumbnails per row while still virtualizing over "row" elements for a
/// large Set.
///
/// A fixed chunk-of-4 sliced the last tile in a row off at the pane edge whenever the pane was
/// narrower than four tile-slots (audit finding: set-browser-*.png, #0004 cut in half at x=900).
/// This is an <see cref="IMultiValueConverter"/> over [Items, ListBox.Bounds.Width] instead — see
/// SetBrowserView.axaml's MultiBinding — so the row count recomputes and the grid reflows whenever
/// the pane is resized, rather than clipping a hard-coded row width.
///
/// Why chunk instead of a wrapping panel: Avalonia has no virtualizing wrap/uniform-grid layout.
/// <c>UniformGridLayout</c> is absent from <c>Avalonia.Controls</c> — verified against the installed
/// assembly rather than the docs, first on 11.2.3 and re-checked on <b>12.1.1</b>, which is what this
/// project pins today. (The comment said 11.2.3 was the pinned version long after it was not; the
/// conclusion survived the upgrade, the premise did not.) Swapping a ListBox's ItemsPanel to WrapPanel (the
/// only wrapping panel that does exist) disables virtualization entirely, since WrapPanel implements
/// no virtualizing panel interface. ListBox's default ItemsPanel — VirtualizingStackPanel — only
/// virtualizes a single vertical/horizontal run, so grouping fixed-size rows ourselves is the only
/// way to keep true virtualization for a large Set while still presenting a grid.</summary>
public sealed class RowChunkConverter : IMultiValueConverter
{
    /// <summary>Per-tile footprint used to compute how many tiles fit in the available width: the
    /// 120px tile plus the 12px Button.Padding that wraps it (6px each side) plus the 10px
    /// StackPanel.Spacing that separates tiles (SetBrowserView.axaml: Border Width="120" inside
    /// Button Padding="6" inside StackPanel Orientation="Horizontal" Spacing="10").</summary>
    private const double TileSlotWidth = 120 + 12;
    private const double TileGap = 10;

    /// <summary>The shared instance markup binds to.</summary>
    public static readonly RowChunkConverter Instance = new();

    private RowChunkConverter() { }

    /// <summary>Chunks the item list into fixed-size rows for the pane's current width.</summary>
    /// <param name="values">The items, then the ListBox's width.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Rows of tiles, or an empty list before the pane has been measured.</returns>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var items = values.Count > 0 ? values[0] as IReadOnlyList<SetItemRow> : null;
        if (items is null || items.Count == 0) return Array.Empty<TileRow>();

        var width = values.Count > 1 && values[1] is double w ? w : 0;
        // width <= 0 covers the pre-layout pass (Bounds not measured yet) — fall back to the old
        // fixed width of 4 so the first frame isn't empty.
        var perRow = width > 0 ? (int)((width + TileGap) / (TileSlotWidth + TileGap)) : 4;
        if (perRow < 1) perRow = 1;

        return items.Chunk(perRow).Select(chunk => new TileRow(chunk)).ToList();
    }
}
