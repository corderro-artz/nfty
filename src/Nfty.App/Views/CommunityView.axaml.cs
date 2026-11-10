using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The Community page. Code-behind is the page-size measurement and the wheel that steps the
/// preview's cells; everything else is bound.</summary>
public partial class CommunityView : UserControl
{
    /// <summary>The host size the page size was last computed from.</summary>
    private double _lastWidth, _lastHeight;

    /// <summary>Loads the view.</summary>
    public CommunityView()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
        this.FindControl<Border>("RailShot")!.PointerWheelChanged += OnRailWheel;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Tells the ViewModel how many tiles a page holds.</summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">Unused.</param>
    /// <remarks>
    /// <para>The columns and rows come from the host the Grid gave the tile area, divided by the tile
    /// pitch tokens the tile style also reads. The host is a <see cref="ScrollViewer"/> with both bars
    /// off, which reports the room it was GIVEN, so writing the page size (which changes what is inside
    /// it) cannot change the two inputs read here - the rule that stopped the CookBook card's
    /// "Infinite layout loop detected". A tile is NOT measured: a drawn tile wobbles for a pass after
    /// the ItemsControl rebuilds its containers.</para>
    /// </remarks>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is not CommunityViewModel vm) return;
        if (this.FindControl<ScrollViewer>("TilesHost") is not { Bounds: { Width: > 0, Height: > 0 } } host) return;
        if (!this.TryFindResource("CommunityTilePitchWidth", ActualThemeVariant, out object? w) || w is not double pitchW
            || !this.TryFindResource("CommunityTilePitchHeight", ActualThemeVariant, out object? h) || h is not double pitchH
            || pitchW <= 0 || pitchH <= 0)
        {
            return;
        }
        if (host.Bounds.Width == _lastWidth && host.Bounds.Height == _lastHeight) return;
        _lastWidth = host.Bounds.Width;
        _lastHeight = host.Bounds.Height;

        int cols = Math.Max(1, (int)(host.Bounds.Width / pitchW));
        int rows = Math.Max(1, (int)(host.Bounds.Height / pitchH));
        vm.PageSize = cols * rows;
    }

    private void OnRailWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not CommunityViewModel vm || e.Delta.Y == 0) return;
        vm.StepCell(e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }
}
