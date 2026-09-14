using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The set browser view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class SetBrowserView : UserControl
{
    /// <summary>Loads the view.</summary>
    public SetBrowserView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnTileClick);

        // ONE BUBBLED HANDLER FOR HOVER, not a per-tile PointerEntered. PointerEntered and
        // PointerExited route DIRECT in Avalonia, so a handler on this view would never see a
        // tile's - and a handler per tile is a subscription per realized container on a grid whose
        // whole design is that containers are cheap to realize. PointerMoved bubbles, and the work
        // is guarded on the row actually changing.
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        PointerExited += (_, _) => Vm?.Hover(null);

        LayoutUpdated += OnLayoutUpdated;
    }

    private double _lastRoom;
    /// <summary>The rarity row height, measured ONCE and then remembered.</summary>
    /// <remarks>
    /// Re-measuring it every pass is what killed the CookBook card: a row's measured height wobbles
    /// for a pass after an <c>ItemsControl</c> rebuilds its containers, so a live window resize
    /// wrote a different height on pass after pass inside one render and Avalonia threw "Infinite
    /// layout loop detected". A rarity row is one line at every window size, so it is measured on
    /// the first pass that draws one and never again — which leaves the ROOM as the only input, and
    /// the room is read off a star row this handler cannot change.
    /// </remarks>
    private double _rowHeight;

    /// <summary>
    /// Sizes the rarity rows host to a WHOLE number of rows, so the fold never cuts one in half.
    /// </summary>
    /// <param name="sender">Unused.</param>
    /// <param name="e">Unused.</param>
    /// <remarks>
    /// <para>Measured rather than declared, for the reason the CookBook card's pager is measured: a
    /// constant is right at one window size and wrong at every other, and this rail gets whatever
    /// height is left after the tile grid beside it.</para>
    ///
    /// <para><b>This cannot feed back into its own input.</b> The room is read off the host's PARENT
    /// row, which is a star row sized by the Grid — setting a height on the child does not change
    /// it. Writing a height computed from the host's own bounds is what threw <c>Infinite layout
    /// loop detected</c> on the CookBook card. The guard on the last pair is belt and braces.</para>
    /// </remarks>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (this.FindControl<ScrollViewer>("RarityRows") is not { } host) return;
        if (host.Parent is not Grid grid || grid.RowDefinitions.Count < 2) return;

        // THE STAR ROW'S OWN HEIGHT, asked of the Grid rather than reconstructed from its children.
        // Reconstructing it - grid height less each sibling's Bounds - was wrong by exactly 12px at
        // every window height, because Bounds EXCLUDES margin and the identity block above carries
        // 12 of bottom margin. So the handler wrote a height ~12 larger than the slot it was sizing,
        // and the rows overflowed into their own bottom margin; nothing overlapped the Save band
        // only because that margin happened to absorb it. RowDefinitions[1] is the number itself,
        // and it is still an input this handler cannot change: a star row takes the space left over
        // by the Auto rows, not the space its child asks for.
        double room = grid.RowDefinitions[1].ActualHeight
            - host.Margin.Top - host.Margin.Bottom;

        if (_rowHeight <= 0)
        {
            var row = host.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("data-row"));
            if (row is null || row.Bounds.Height <= 0) return;
            _rowHeight = row.Bounds.Height;
        }
        if (room <= 0) return;
        if (Math.Abs(room - _lastRoom) < 0.5) return;

        _lastRoom = room;

        // At least one row: a rail too short for even one is a smaller window than the app allows,
        // and showing one row that overflows beats showing none.
        int fits = Math.Max(1, (int)(room / _rowHeight));
        double snapped = fits * _rowHeight;

        // IsNaN FIRST. An unset Height is NaN, and every comparison against NaN is false - so
        // `Math.Abs(host.Height - snapped) > 0.5` was false on the very first pass, which is the one
        // pass that matters, and the snap silently never applied. The test caught it only because it
        // was probed by deleting this line and still passed.
        if (double.IsNaN(host.Height) || Math.Abs(host.Height - snapped) > 0.5)
            host.Height = snapped;
    }

    /// <summary>
    /// A tile click selects the asset and opens the inspector on it.
    ///
    /// <para>One bubbled handler rather than a command per tile: the grid is an ItemsControl of rows
    /// of tiles, so a per-tile binding would have to reach two DataContexts up to find the browser,
    /// and a <c>$parent[]</c> hop through a template is the fragile kind. The row's own DataContext
    /// is the asset, which is all this needs.</para>
    ///
    /// <para>The Save button in the rail is also a Button and also bubbles here — but its
    /// DataContext is the ViewModel, not a SetItemRow, so the pattern match skips it.</para>
    /// </summary>
    private void OnTileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SetBrowserViewModel vm && e.Source is Button { DataContext: SetItemRow row })
            vm.InspectCommand.Execute(row);
    }

    private SetBrowserViewModel? Vm => DataContext as SetBrowserViewModel;

    /// <summary>The asset under a pointer event, or null when it did not land on a tile.</summary>
    private static SetItemRow? TileUnder(object? source) =>
        (source as Visual)?.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: SetItemRow row }
            ? row
            : null;

    /// <summary>
    /// The detail rail follows the pointer.
    /// </summary>
    /// <remarks>
    /// A grid of 500 tiles is SCANNED, not read, and the only way to find out what one was meant
    /// clicking it - which opens the inspector directly over the panel that answers the question, so
    /// the rail only ever appeared to update once the modal was closed again. Moving off a tile onto
    /// anything else clears it, so the rail cannot go on describing an asset the pointer left.
    /// </remarks>
    private void OnPointerMoved(object? sender, PointerEventArgs e) => Vm?.Hover(TileUnder(e.Source));

    /// <summary>
    /// The right button marks a tile active without opening it.
    /// </summary>
    /// <remarks>
    /// <para>Left-click means "show me this bigger", which is the right default on a grid of
    /// pictures. But keeping one asset in the rail - to read its rarity against another, or to Save
    /// it - had no gesture at all short of opening the inspector and closing it again.</para>
    /// <para>On the TUNNEL so it runs before the tile Button and the ListBox under it see the
    /// press. Marking it handled is belt and braces and no test can make it fail: Avalonia's Button
    /// raises Click for the LEFT button only, so the inspector was never in danger from this press.
    /// It stays because "the right button does not open anything" should be a property of this
    /// handler rather than a property of how Fluent's Button happens to be written.</para>
    /// </remarks>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        if (TileUnder(e.Source) is not { } row) return;
        Vm?.SelectCommand.Execute(row);
        e.Handled = true;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
