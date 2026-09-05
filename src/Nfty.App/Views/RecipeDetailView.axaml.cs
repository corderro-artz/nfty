using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The recipe detail view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.
///
/// <para>Layer reorder is one of those: a within-list drag needs the pointer's position measured
/// against the realised rows to know where the layer would land, which is geometry no binding can
/// express. It is deliberately a <b>pointer-capture</b> gesture rather than
/// <c>DragDrop.DoDragDropAsync</c> — see the note on <see cref="OnRowsPointerPressed"/>.</para></summary>
public partial class RecipeDetailView : UserControl
{
    private readonly ItemsControl _rows;
    private readonly Panel _stack;
    private readonly Border _dropLine;

    /// <summary>The layer being dragged, or null when no drag is in flight.</summary>
    private LayerRow? _dragRow;
    /// <summary>Where the drop line currently sits, as a slot BETWEEN rows: 0 is above the first row,
    /// <c>Layers.Count</c> is below the last. -1 while no drag is in flight.</summary>
    private int _dropSlot = -1;

    /// <summary>Loads the view.</summary>
    public RecipeDetailView()
    {
        InitializeComponent();
        _rows = this.FindControl<ItemsControl>("LayerRows")!;
        _stack = this.FindControl<Panel>("LayerStack")!;
        _dropLine = this.FindControl<Border>("DropLine")!;

        // Tunnel, not bubble. The grip sits INSIDE the row button, which handles PointerPressed
        // itself; on the way back up the press would read as a click and navigate to that ingredient,
        // taking this whole pane with it. Taking the press on the way DOWN and marking it handled is
        // what keeps a drag from being a navigation.
        _rows.AddHandler(InputElement.PointerPressedEvent, OnRowsPointerPressed, RoutingStrategies.Tunnel);
        _rows.PointerMoved += OnRowsPointerMoved;
        _rows.PointerReleased += OnRowsPointerReleased;
        _rows.PointerCaptureLost += (_, _) => EndDrag();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);

        // The chance field commits when it is DONE being edited, not on every value change: a save
        // rewrites every PNG in the book, so a stepper spun from 0 to 85 would be eighty-five of
        // them. "Done" for a numeric field is losing focus or pressing Enter, and both are handled
        // here rather than per row because the rows are templated and there is no per-row code.
        _rows.AddHandler(LostFocusEvent, OnChanceLostFocus, RoutingStrategies.Bubble);

        // Enter needs handledEventsToo. Focus rests in the field's inner PART_TextBox, and a TextBox
        // marks Enter HANDLED before the event leaves the control — so the view's own OnKeyDown,
        // registered the ordinary way, never saw it and the Enter half of "done" did nothing. Narrow
        // on purpose: taking handled events on the whole view would also hand Escape and Alt+Up/Down
        // to keys a child has already dealt with.
        _rows.AddHandler(KeyDownEvent, OnChanceKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>The layer row a chance field belongs to, or null for any other control.</summary>
    /// <remarks>
    /// Walks UP from the source rather than testing it, because focus does not rest on the
    /// NumericUpDown — it goes to the <c>PART_TextBox</c> inside the template, and every event that
    /// matters is sourced there. Testing the source directly matched exactly one thing: the
    /// LostFocus the NumericUpDown raises while handing focus INWARD, which fires as the user
    /// arrives and carries the value they have not typed yet. The departure, sourced at the
    /// TextBox, was ignored — so the field committed on Enter and silently discarded everything
    /// else. No ViewModel test could see it: they all call CommitAbsentAsync themselves.
    /// </remarks>
    private static LayerRow? ChanceRow(object? source) =>
        (source as Visual)?.FindAncestorOfType<NumericUpDown>(includeSelf: true)
            is { Name: "AbsentField" } f ? f.Tag as LayerRow : null;

    private void OnChanceLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RecipeDetailViewModel vm && ChanceRow(e.Source) is { } row)
            _ = vm.CommitAbsentAsync(row);
    }

    /// <summary>Enter commits a chance without waiting for focus to go somewhere else — the other
    /// half of "done being edited", and the half a user reaches for when the field is the last thing
    /// they touched.</summary>
    private void OnChanceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is RecipeDetailViewModel vm && ChanceRow(e.Source) is { } row)
        {
            e.Handled = true;
            _ = vm.CommitAbsentAsync(row);
        }
    }

    // ---- drag ---------------------------------------------------------------------------------

    /// <summary>
    /// Begins a reorder drag. Pointer capture, not <c>DragDrop.DoDragDropAsync</c>: the framework's
    /// drag source is a platform service, and the headless harness this project verifies every visual
    /// through has none — the drop line could never be shown in a captured frame, and the gesture
    /// could carry no test. A capture-based drag is also what the exploration itself implements
    /// (pointerdown/pointermove/pointerup plus setPointerCapture), works identically under touch, and
    /// needs no payload for a reorder that never leaves the control it started in.
    /// </summary>
    private void OnRowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not RecipeDetailViewModel vm || !vm.CanReorder) return;
        // Left button only. Avalonia reports a touch contact and a pen tip as the left button too, so
        // this costs the touch path nothing — what it rules out is a RIGHT- or middle-click on a grip
        // capturing the pointer, showing the drop line and committing (and persisting) a reorder on
        // release, which is what it did without the check.
        if (!e.GetCurrentPoint(_rows).Properties.IsLeftButtonPressed) return;
        if (GripUnder(e.Source as Visual) is not { } grip) return;
        if (grip.DataContext is not LayerRow row) return;

        e.Handled = true;
        vm.SelectedLayer = row;
        grip.Focus(NavigationMethod.Pointer);
        _dragRow = row;
        e.Pointer.Capture(_rows);
        MoveDropLine(e.GetPosition(_stack).Y);
    }

    private void OnRowsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragRow is null) return;
        MoveDropLine(e.GetPosition(_stack).Y);
    }

    private async void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragRow is not { } row) return;
        int slot = _dropSlot;

        // Released outside the rows is a CANCEL, not a drop at the last place the line happened to
        // be. The pointer is captured, so a release anywhere on screen still arrives here — without
        // this, letting go out in the window committed the reorder, and per this feature's own spec a
        // reorder is a different collection rather than a restacked one. Dragging away and letting go
        // is the universal "never mind"; it must mean that here too.
        bool inside = IsInside(e.GetPosition(_rows), _rows.Bounds.Size);

        if (!inside || DataContext is not RecipeDetailViewModel vm || slot < 0)
        {
            CancelDrag(e.Pointer);
            return;
        }

        e.Pointer.Capture(null);
        EndDrag();
        await vm.MoveToSlotAsync(row, slot);
        FocusGripOf(row);
    }

    /// <summary>Whether a point measured against a control falls within it.</summary>
    private static bool IsInside(Point p, Size size) =>
        p.X >= 0 && p.Y >= 0 && p.X <= size.Width && p.Y <= size.Height;

    /// <summary>Abandons an in-flight drag without moving anything: releases the capture, hides the
    /// drop line, and leaves the stack exactly as it was.</summary>
    private void CancelDrag(IPointer? pointer)
    {
        var row = _dragRow;
        pointer?.Capture(null);
        EndDrag();
        if (row is not null) FocusGripOf(row);
    }

    // A slot is a gap between rows; turning one into a depth needs the rows and their depths, so that
    // translation is RecipeDetailViewModel.MoveToSlotAsync's. This file measures geometry and nothing
    // else — which is the only part a binding genuinely cannot express.

    /// <summary>Which slot a drop at <paramref name="y"/> falls into: the first row whose midpoint the
    /// pointer is above, or the slot past the last row. Midpoints rather than edges, so the line
    /// flips to the far side of a row exactly halfway through it and the target is never ambiguous.</summary>
    /// <param name="rows">Each row's top and height, in the coordinate space <paramref name="y"/> is in.</param>
    /// <param name="y">The pointer's vertical position.</param>
    /// <returns>0..<c>rows.Count</c>.</returns>
    internal static int SlotAt(IReadOnlyList<(double Top, double Height)> rows, double y)
    {
        ArgumentNullException.ThrowIfNull(rows);
        for (int i = 0; i < rows.Count; i++)
            if (y < rows[i].Top + rows[i].Height / 2) return i;
        return rows.Count;
    }

    private void MoveDropLine(double y)
    {
        var rows = RowBoxes();
        if (rows.Count == 0) return;
        _dropSlot = SlotAt(rows, y);
        double top = _dropSlot >= rows.Count
            ? rows[^1].Top + rows[^1].Height
            : rows[_dropSlot].Top;
        // Centered on the boundary rather than sitting under it, like the mockup's 2px line.
        _dropLine.Margin = new Thickness(0, Math.Max(0, top - 1), 0, 0);
        _dropLine.Classes.Set("on", true);
    }

    private void EndDrag()
    {
        _dragRow = null;
        _dropSlot = -1;
        _dropLine.Classes.Set("on", false);
    }

    private List<(double Top, double Height)> RowBoxes() => _rows.GetRealizedContainers()
        .OfType<Control>()
        .Select(c => (Top: c.TranslatePoint(default, _stack)?.Y ?? 0d, c.Bounds.Height))
        .OrderBy(b => b.Top)
        .ToList();

    /// <summary>The grip the pointer went down on, or null if it went down anywhere else in the row.</summary>
    private Border? GripUnder(Visual? source)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, _rows); v = v.GetVisualParent())
            if (v is Border b && b.Classes.Contains("grip")) return b;
        return null;
    }

    // ---- keyboard -----------------------------------------------------------------------------

    /// <summary>Alt+Up / Alt+Down move the selected layer. Shipped WITH the drag, never instead of
    /// it: variant B's own cost table records "Keyboard: none", and a reorder reachable only by
    /// pointer is an incomplete feature. The arrow follows the TABLE — Alt+Up moves the row up the
    /// page, towards #1 — which is why #1's meaning is spelled out in the header hint.</summary>
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter is NOT handled here. It used to be, and it could never fire: this handler is
        // registered the ordinary way, and the TextBox inside the chance field marks Enter handled
        // before it gets this far. OnChanceKeyDown takes it instead, on the rows, handledEventsToo.
        //
        // Esc abandons a drag. Checked before the Alt gate, because Esc carries no modifier — and a
        // captured drag has no other way out: every other route (release, capture lost) commits or
        // has already ended it.
        if (e.Key == Key.Escape && _dragRow is not null)
        {
            e.Handled = true;
            CancelDrag(null);
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Alt) return;
        int rows = e.Key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
        if (rows == 0 || DataContext is not RecipeDetailViewModel vm || vm.SelectedLayer is not { } row) return;

        e.Handled = true;
        await vm.MoveSelectedRowsAsync(rows);
        // Unconditionally, including the no-op at either end: the grip must keep focus so the next
        // keystroke still reaches this handler.
        FocusGripOf(row);
    }

    /// <summary>
    /// Puts focus back on a row's grip after a reorder, so the next keystroke still reaches this
    /// handler and a second Alt+Up is not silently swallowed.
    ///
    /// <para><b>Posted, not called.</b> Measured rather than assumed: an
    /// <c>ObservableCollection.Move</c> makes Avalonia's <see cref="ItemsControl"/> <i>drop and
    /// recreate</i> the moved container — it does not re-parent the old one — and the new container's
    /// child is not realised until the next layout pass, so there is no grip to focus yet and the
    /// focused element is null. Layout runs at a higher dispatcher priority than
    /// <see cref="DispatcherPriority.Loaded"/>, so queuing behind it is what makes the grip exist by
    /// the time this runs.</para></summary>
    private void FocusGripOf(LayerRow row) => Dispatcher.UIThread.Post(() =>
    {
        if (_rows.ContainerFromItem(row) is not { } container) return;
        var grip = container.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("grip"));
        grip?.Focus(NavigationMethod.Directional);
    }, DispatcherPriority.Loaded);

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
