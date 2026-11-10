using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The Community page. Code-behind is the page-size measurement, the wheel that steps the
/// preview's cells, the tag picker's filter keys and the page's keyboard; everything else is bound.</summary>
public partial class CommunityView : UserControl
{
    /// <summary>The host size the page size was last computed from.</summary>
    private double _lastWidth, _lastHeight;

    private readonly TextBox _search;
    private readonly Button _picker;

    /// <summary>Loads the view.</summary>
    public CommunityView()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
        this.FindControl<Border>("RailShot")!.PointerWheelChanged += OnRailWheel;
        _search = this.FindControl<TextBox>("SearchBox")!;
        _picker = this.FindControl<Button>("TagPickerButton")!;
        this.FindControl<TextBlock>("SearchKey")!.Text = KeyHints.Search;

        // The page's keys, all on one focus-guarded handler (the editor's and viewer's pattern). TUNNEL and
        // handledEventsToo: a focused tile button marks Enter and Space handled before a bubbling handler would
        // see them. The page takes focus on arrival, so the keys are live without a click first.
        AddHandler(KeyDownEvent, (_, e) => OnShortcut(e), RoutingStrategies.Tunnel, handledEventsToo: true);
        AttachedToVisualTree += (_, _) => Focus();

        if (_picker.Flyout is Flyout flyout)
        {
            flyout.Opened += (_, _) => OnPickerOpened(flyout);
            if (flyout.Content is Control content
                && content.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "TagFilterBox") is { } filter)
            {
                filter.AddHandler(KeyDownEvent, (_, e) => OnFilterKey(flyout, e), RoutingStrategies.Tunnel,
                    handledEventsToo: true);
            }
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private CommunityViewModel? Vm => DataContext as CommunityViewModel;

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
        vm.Columns = cols;
        vm.PageSize = cols * rows;
    }

    private void OnRailWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not CommunityViewModel vm || e.Delta.Y == 0) return;
        vm.StepCell(e.Delta.Y > 0 ? -1 : 1);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- the tag picker

    /// <summary>Opening the picker starts with an empty filter and the keyboard in it, so typing narrows at once.</summary>
    private void OnPickerOpened(Flyout flyout)
    {
        if (Vm is { } vm) vm.TagFilter = "";
        if (flyout.Content is Control content
            && content.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "TagFilterBox") is { } filter)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => filter.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>Enter adds (or drops) the first tag the filter leaves; Escape clears the filter, then closes.</summary>
    private void OnFilterKey(Flyout flyout, KeyEventArgs e)
    {
        if (Vm is not { } vm || e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            vm.ToggleFirstPickerTag();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (vm.TagFilter.Length > 0) vm.TagFilter = "";
            else flyout.Hide();
            e.Handled = true;
        }
    }

    /// <summary>Opens the tag picker (the T key), if the gallery has tags to pick.</summary>
    internal void OpenTagPicker()
    {
        if (Vm is not { HasTags: true } || _picker.Flyout is not { } flyout) return;
        flyout.ShowAt(_picker);
    }

    // ---------------------------------------------------------------- the keys

    /// <summary>
    /// The page's keys. Search: Ctrl+K (anywhere on the page) or / (not in a text field). Grid: the arrows
    /// move the selection a tile or a row, PgUp/PgDn turn the page, Home/End jump to the ends, Enter
    /// downloads the selection. T opens the tag picker, R (or F5) refreshes. Escape clears the search
    /// first, then leaves; Alt+Left leaves. None of them acts while a dialog is open, and only Ctrl+K and
    /// Escape act while the search box has the keyboard (a typed letter there is text).
    /// </summary>
    private void OnShortcut(KeyEventArgs e)
    {
        if (Vm is not { } vm || vm.Dialogs.OpenDialogs.Count > 0) return;
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused) return;
        if (focused.FindAncestorOfType<CommunityView>(includeSelf: true) != this) return;
        bool inText = focused.FindAncestorOfType<TextBox>(includeSelf: true) is not null;

        if (e.Key == Key.K && e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt && !inText)
        {
            vm.BackCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (vm.Query.Length > 0) vm.ClearSearchCommand.Execute(null);
            else if (inText) Focus();
            else vm.BackCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (inText || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return;

        switch (e.Key)
        {
            case Key.OemQuestion or Key.Divide when e.KeyModifiers == KeyModifiers.None:
                FocusSearch(); break;
            case Key.Left: vm.MoveSelection(-1); Focus(); break;
            case Key.Right: vm.MoveSelection(1); Focus(); break;
            case Key.Up: vm.MoveSelection(-Math.Max(1, vm.Columns)); Focus(); break;
            case Key.Down: vm.MoveSelection(Math.Max(1, vm.Columns)); Focus(); break;
            case Key.PageUp: vm.PrevPageCommand.Execute(null); break;
            case Key.PageDown: vm.NextPageCommand.Execute(null); break;
            case Key.Home: vm.FirstPage(); break;
            case Key.End: vm.LastPage(); break;
            case Key.Enter:
                // A focused tile is the one Enter means: select it first, as its click would.
                if (focused.FindAncestorOfType<Button>(includeSelf: true) is { DataContext: CommunityTile tile }
                    && !ReferenceEquals(tile, vm.Selected))
                {
                    vm.SelectCommand.Execute(tile);
                }
                if (vm.DownloadCommand.CanExecute(null)) vm.DownloadCommand.Execute(null);
                break;
            case Key.T: OpenTagPicker(); break;
            case Key.R or Key.F5: vm.RefreshCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }

    private void FocusSearch()
    {
        _search.Focus();
        _search.SelectAll();
    }
}
