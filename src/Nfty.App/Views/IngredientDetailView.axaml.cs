using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The ingredient detail view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class IngredientDetailView : UserControl
{
    private VariantRow? _pressed;
    private Point _origin;
    private bool _dragging;
    private readonly ItemsControl _rows;

    /// <summary>The current variant reorder save, so callers can await the gesture.</summary>
    internal Task PendingReorder { get; private set; } = Task.CompletedTask;

    /// <summary>Loads the view.</summary>
    public IngredientDetailView()
    {
        InitializeComponent();
        _rows = this.FindControl<ItemsControl>("VariantRows")!;
        _rows.AddHandler(PointerPressedEvent, OnPress, RoutingStrategies.Tunnel);
        _rows.AddHandler(PointerMovedEvent, OnMove, RoutingStrategies.Bubble, handledEventsToo: true);
        _rows.AddHandler(PointerReleasedEvent, OnRelease, RoutingStrategies.Tunnel);
        _rows.PointerCaptureLost += (_, _) => { _pressed = null; _dragging = false; };
        _rows.AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        // A variant's NAME is edited in the editor, so double-clicking a row opens it on that variant
        // (the first click already selected it, and the editor opens on the selection).
        _rows.DoubleTapped += (_, e) =>
        {
            if (DataContext is IngredientDetailViewModel vm && RowUnder(e.Source as Visual) is { } row)
            {
                vm.SelectVariantCommand.Execute(row.Id);
                vm.EditIngredientCommand.Execute(null);
                e.Handled = true;
            }
        };
        var delete = new MenuItem { Header = "Delete variant" };
        var edit = new MenuItem { Header = "Edit variant" };
        delete.Click += (_, _) => (DataContext as IngredientDetailViewModel)?.DeleteVariantCommand.Execute(null);
        edit.Click += (_, _) => (DataContext as IngredientDetailViewModel)?.EditIngredientCommand.Execute(null);
        _rows.ContextMenu = new ContextMenu { Items = { edit, delete } };
        _rows.ContextRequested += (_, e) =>
        {
            if (DataContext is not IngredientDetailViewModel vm || RowUnder(e.Source as Visual) is not { } row)
            { e.Handled = true; return; }
            if (!ReferenceEquals(vm.Selected, row)) vm.SelectVariantCommand.Execute(row.Id);
            edit.IsEnabled = vm.CanReorder;
            delete.IsEnabled = vm.DeleteVariantCommand.CanExecute(null);
        };
    }

    private static VariantRow? RowUnder(Visual? source) => source?
        .GetSelfAndVisualAncestors().OfType<Button>().Select(b => b.DataContext).OfType<VariantRow>().FirstOrDefault();

    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not IngredientDetailViewModel { CanReorder: true } vm
            || !e.GetCurrentPoint(_rows).Properties.IsLeftButtonPressed) return;
        // A drag starts on the GRIP only, as on the Recipe pane's layer rows. A press anywhere else in
        // the row is left to the row's Button, which selects the variant on click.
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("grip")) is null) return;
        var row = RowUnder(e.Source as Visual);
        if (row is null) return;
        (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault()?.Focus();
        e.Handled = true;
        vm.SelectVariantCommand.Execute(row.Id);
        e.Pointer.Capture(_rows);
        _pressed = row;
        _origin = e.GetPosition(_rows);
    }

    private void OnMove(object? sender, PointerEventArgs e)
    {
        if (_pressed is null) return;
        if (!_dragging && Math.Abs(e.GetPosition(_rows).Y - _origin.Y) > 4)
        {
            _dragging = true;
        }
    }

    private async void OnRelease(object? sender, PointerReleasedEventArgs e)
    {
        var row = _pressed;
        bool moved = _dragging;
        _pressed = null; _dragging = false;
        if (row is null || DataContext is not IngredientDetailViewModel vm) return;
        e.Handled = true;
        if (!moved)
        {
            e.Pointer.Capture(null);       // a press on the grip that never moved: selected, nothing more
            return;
        }
        var buttons = _rows.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext is VariantRow).ToArray();
        double y = e.GetPosition(_rows).Y;
        if (y < 0 || y > _rows.Bounds.Height) { e.Pointer.Capture(null); return; }
        int index = buttons.Count(b => b.TranslatePoint(new Point(0, b.Bounds.Height / 2), _rows) is { } p && p.Y < y);
        int from = vm.Variants.ToList().FindIndex(v => v.Id == row.Id);
        if (index > from) index--;
        e.Pointer.Capture(null);
        var top = TopLevel.GetTopLevel(this);
        PendingReorder = vm.MoveVariantAsync(row.Id, index);
        await PendingReorder;
        RestoreFocus(top, vm.Identity, row.Id);
    }

    private async void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Alt || e.Key is not (Key.Up or Key.Down)
            || DataContext is not IngredientDetailViewModel { CanReorder: true } vm
            || RowUnder(e.Source as Visual) is not { } row) return;
        e.Handled = true;
        int from = vm.Variants.ToList().FindIndex(v => v.Id == row.Id);
        var top = TopLevel.GetTopLevel(this);
        PendingReorder = vm.MoveVariantAsync(row.Id, from + (e.Key == Key.Up ? -1 : 1));
        await PendingReorder;
        RestoreFocus(top, vm.Identity, row.Id);
    }

    private static void RestoreFocus(TopLevel? top, (string Recipe, string Ingredient) identity, string id) => Dispatcher.UIThread.Post(() =>
        top?.GetVisualDescendants().OfType<IngredientDetailView>()
            .FirstOrDefault(v => v.DataContext is IngredientDetailViewModel vm && vm.Identity == identity)?.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.DataContext is VariantRow row && row.Id == id)?.Focus(), DispatcherPriority.Loaded);

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
