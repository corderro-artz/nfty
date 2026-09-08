using System;
using System.Linq;
using Avalonia.Controls;
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
        LayoutUpdated += OnLayoutUpdated;
    }

    private double _lastRoom;
    private double _lastRow;

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

        // The star row's own height, less this host's margins: what the rows may occupy.
        double room = grid.Bounds.Height
            - grid.Children.OfType<Control>().Where(c => Grid.GetRow(c) != 1)
                .Sum(c => c.Bounds.Height)
            - host.Margin.Top - host.Margin.Bottom;

        var row = host.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("data-row"));
        if (row is null || row.Bounds.Height <= 0 || room <= 0) return;
        if (Math.Abs(room - _lastRoom) < 0.5 && Math.Abs(row.Bounds.Height - _lastRow) < 0.5) return;

        _lastRoom = room;
        _lastRow = row.Bounds.Height;

        // At least one row: a rail too short for even one is a smaller window than the app allows,
        // and showing one row that overflows beats showing none.
        int fits = Math.Max(1, (int)(room / row.Bounds.Height));
        double snapped = fits * row.Bounds.Height;

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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
