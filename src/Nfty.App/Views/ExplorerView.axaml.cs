using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The explorer view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.
///
/// <para>Tree reorder is one of those: a within-siblings drag needs the pointer measured against the
/// realised rows to know where a node would land, which is geometry no binding can express. It is a
/// <b>pointer-capture</b> gesture for the same reason the Recipe pane's layer drag is — Avalonia's
/// own drag source is a platform service the headless harness does not register, so a
/// <c>DoDragDropAsync</c> gesture could carry no test and its drop line could never appear in a
/// captured frame.</para></summary>
public partial class ExplorerView : UserControl
{
    private readonly TreeView _tree;
    private readonly Border _dropLine;

    /// <summary>How far the pointer must travel before a press becomes a drag rather than a click.
    /// A tree row has no drag grip — one would cost every row a 26px gutter in the app's narrowest
    /// pane — so the row itself is the handle, and this is what keeps selecting a node from
    /// beginning to move it.</summary>
    private const double DragThreshold = 4;

    private ExplorerNode? _pressNode;
    private Point _pressPoint;
    private bool _dragging;
    /// <summary>Where the drop line sits, as a slot BETWEEN siblings: 0 is above the first, Count is
    /// below the last. -1 while no drag is in flight.</summary>
    private int _dropSlot = -1;

    /// <summary>
    /// The reorder this view last started, or an already-completed task when it has never started
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>An input handler has to be <c>async void</c>, so the whole-book write a drop or an
    /// Alt+Up kicks off is otherwise UNOBSERVABLE: the gesture returns the moment the write reaches
    /// its first await, and nothing — not the caller, not a test, not a teardown — can tell whether
    /// the archive has been saved. That is not a theoretical gap. It cost
    /// <c>ExplorerTreeReorderTests</c> a race that deleted the temp directory out from under the
    /// half-written <c>book.cbk.&lt;guid&gt;.tmp</c>, and the IOException it threw from a
    /// <c>finally</c> REPLACED the assertion failure underneath it, so the test reported a locked
    /// file rather than the move that had not landed yet.</para>
    ///
    /// <para>This is that task, kept so the gesture can be AWAITED rather than slept on. It never
    /// faults: <c>MoveNodeToAsync</c> reports its own failures and returns false. Reentrancy is
    /// <b>not</b> this property's job — the ViewModel refuses a second reorder while one is being
    /// written, which is where the book, the source file and the edit lock already live.</para>
    /// </remarks>
    internal Task PendingReorder { get; private set; } = Task.CompletedTask;

    /// <summary>Loads the view.</summary>
    public ExplorerView()
    {
        InitializeComponent();
        _tree = this.FindControl<TreeView>("Tree")!;
        _dropLine = this.FindControl<Border>("TreeDropLine")!;

        _tree.SelectionChanged += (_, e) =>
        {
            if (DataContext is ExplorerViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is ExplorerNode node)
                vm.SelectNodeCommand.Execute(node);
        };

        // TUNNEL for the press, and deliberately NOT handled. A TreeViewItem marks the press handled
        // while making its own selection, so a bubble handler here never ran and the drag could not
        // start at all - measured, not assumed: the gesture test failed with the drop line never
        // lit. Taking it on the way DOWN sees every press; leaving it unhandled lets the selection
        // happen exactly as it always did, which matters because the row is both the drag handle and
        // the thing you click to select. Nothing has to guess which gesture it is up front: a drag
        // only begins once the pointer has actually travelled.
        _tree.AddHandler(InputElement.PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        // Moves and releases take handled events for the same reason - a child deals with them first
        // - and narrowly: both no-op unless a press has already armed this gesture.
        _tree.AddHandler(InputElement.PointerMovedEvent, OnTreePointerMoved,
            RoutingStrategies.Bubble, handledEventsToo: true);
        _tree.AddHandler(InputElement.PointerReleasedEvent, OnTreePointerReleased,
            RoutingStrategies.Bubble, handledEventsToo: true);
        _tree.PointerCaptureLost += (_, _) => EndDrag();
        // TUNNEL, like the press, and for a sharper reason: a TreeView handles the ARROW KEYS
        // itself, for navigation, and it does so whatever modifier is held - so a bubble handler
        // here never saw Alt+Up at all and the chord silently moved the SELECTION instead of the
        // node. Measured: Alt_up_moves_the_selected_layer_up_the_list failed with the stack
        // unchanged. Taking it on the way down and marking it handled is what makes Alt+Up mean
        // "move this" rather than "go up one"; every key this handler does not claim still reaches
        // the tree untouched, including a bare Up and Down.
        _tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Takes initial focus once the view is on screen.</summary>
    /// <param name="e">Ignored beyond forwarding to the base implementation.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    // Ctrl+K can't be a <KeyBinding Command="..."/> because focusing a control isn't a command — it
    // has to happen here, where the actual TextBox instance is reachable.
    /// <summary>Handles Ctrl+K, which focuses the search box. In code-behind because focusing a
    /// specific control is not something a ViewModel can express.</summary>
    /// <param name="e">The key event; marked handled when the gesture matches.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            this.FindControl<TextBox>("SearchBox")?.Focus();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    // ---- drag ------------------------------------------------------------------------------------

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressNode = null;
        if (DataContext is not ExplorerViewModel vm) return;
        // Left button only. Avalonia reports a touch contact and a pen tip as the left button too, so
        // this costs the touch path nothing; what it rules out is a right-click on a row capturing
        // the pointer and committing a reorder on release.
        if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed) return;
        // Not on the expander chevron: expanding a branch is a press-and-release in place, and the
        // pointer wanders far enough during it to cross the threshold surprisingly often.
        if (IsExpanderPart(e.Source as Visual)) return;
        if (NodeUnder(e.Source as Visual) is not { } node || !vm.CanMove(node)) return;

        _pressNode = node;
        _pressPoint = e.GetPosition(_tree);
    }

    private void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressNode is null) return;
        var p = e.GetPosition(_tree);

        if (!_dragging)
        {
            if (Math.Abs(p.Y - _pressPoint.Y) < DragThreshold
                && Math.Abs(p.X - _pressPoint.X) < DragThreshold) return;
            _dragging = true;
            // Captured only once the gesture is definitely a drag. Capturing at press would swallow
            // the click that selects a node, which is what this tree is mostly used for.
            e.Pointer.Capture(_tree);
        }

        MoveDropLine(p.Y);
    }

    private async void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var node = _pressNode;
        int slot = _dropSlot;
        bool dragging = _dragging;
        _pressNode = null;

        if (!dragging || node is null) { EndDrag(); return; }

        // Released outside the tree is a CANCEL rather than a drop at the last place the line
        // happened to be. The pointer is captured, so a release anywhere on screen still arrives
        // here — and dragging away to let go is the universal "never mind".
        bool inside = IsInside(e.GetPosition(_tree), _tree.Bounds.Size);
        e.Pointer.Capture(null);
        EndDrag();

        if (!inside || slot < 0 || DataContext is not ExplorerViewModel vm) return;
        await (PendingReorder = vm.MoveNodeAsync(node, slot));
    }

    /// <summary>Alt+Up / Alt+Down move the selected node among its siblings. Shipped WITH the drag,
    /// never instead of it: a reorder reachable only by pointer is an incomplete feature, and it is
    /// also the only route a reader who cannot see the drop line has.</summary>
    private async void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        // Esc abandons a drag, and carries no modifier — so it is checked before the Alt gate. A
        // captured drag has no other way out: every other route commits or has already ended it.
        if (e.Key == Key.Escape && _dragging)
        {
            e.Handled = true;
            _pressNode = null;
            EndDrag();
            return;
        }

        if (e.KeyModifiers != KeyModifiers.Alt) return;
        int places = e.Key switch { Key.Up => -1, Key.Down => 1, _ => 0 };
        if (places == 0 || DataContext is not ExplorerViewModel vm) return;
        if (vm.SelectedNode is not { } node || !vm.CanMove(node)) return;

        e.Handled = true;
        await (PendingReorder = vm.MoveNodeByAsync(node, places));
    }

    // ---- geometry --------------------------------------------------------------------------------

    /// <summary>Whether a point measured against a control falls within it.</summary>
    private static bool IsInside(Point p, Size size) =>
        p.X >= 0 && p.Y >= 0 && p.X <= size.Width && p.Y <= size.Height;

    /// <summary>The node a press landed on, or null if it landed on no row at all.</summary>
    private ExplorerNode? NodeUnder(Visual? source)
    {
        for (var v = source; v is not null && !ReferenceEquals(v, _tree); v = v.GetVisualParent())
            if (v is TreeViewItem { DataContext: ExplorerNode node }) return node;
        return null;
    }

    /// <summary>Whether the press landed on a branch's expand/collapse chevron.</summary>
    private static bool IsExpanderPart(Visual? source)
    {
        for (var v = source; v is not null and not TreeViewItem; v = v.GetVisualParent())
            if (v is Control { Name: "PART_ExpandCollapseChevron" } or ToggleButton) return true;
        return false;
    }

    /// <summary>
    /// The HEADER row of each sibling of the dragged node, top-first, in the tree's own coordinates.
    /// </summary>
    /// <remarks>
    /// A <see cref="TreeViewItem"/>'s own bounds include its whole expanded subtree, so measuring
    /// against containers would put a recipe's midpoint somewhere inside its layers. The row a reader
    /// sees is the <c>Border.node</c> the item template draws — and the FIRST one under a container
    /// belongs to that container, every deeper one to a descendant.
    /// </remarks>
    private List<(ExplorerNode Node, double Top, double Height)> SiblingRows(ExplorerNode dragged)
    {
        if (DataContext is not ExplorerViewModel vm || vm.ParentOf(dragged) is not { } parent)
            return new List<(ExplorerNode, double, double)>();

        var rows = new List<(ExplorerNode Node, double Top, double Height)>();
        foreach (var sibling in parent.Children)
        {
            if (RowOf(sibling) is not { } row) continue;                     // scrolled out, or collapsed away
            var top = row.TranslatePoint(default, _tree);
            if (top is null) continue;
            rows.Add((sibling, top.Value.Y, row.Bounds.Height));
        }
        rows.Sort((a, b) => a.Top.CompareTo(b.Top));
        return rows;
    }

    /// <summary>The drawn row for one node: the node box its own container carries.</summary>
    private Border? RowOf(ExplorerNode node) => _tree.GetVisualDescendants()
        .OfType<TreeViewItem>()
        .Where(i => ReferenceEquals(i.DataContext, node))
        .SelectMany(i => i.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("node") && ReferenceEquals(b.DataContext, node)))
        .FirstOrDefault();

    /// <summary>Which slot a drop at <paramref name="y"/> falls into: the first sibling whose
    /// midpoint the pointer is above, or the slot past the last one. Midpoints rather than edges, so
    /// the line flips to the far side of a row exactly halfway through it.</summary>
    /// <param name="rows">Each sibling's top and height.</param>
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
        if (_pressNode is not { } dragged) return;
        var rows = SiblingRows(dragged);
        if (rows.Count == 0) return;

        _dropSlot = SlotAt(rows.Select(r => (r.Top, r.Height)).ToList(), y);
        double top = _dropSlot >= rows.Count
            ? rows[^1].Top + rows[^1].Height
            : rows[_dropSlot].Top;

        // Indented to the row it is describing, so the line says WHICH level the node is landing in
        // — a full-width line under a recipe would read the same whether the layer was going into
        // that recipe or beside it.
        double left = rows[0].Node.Kind == ExplorerNodeKind.Ingredient ? 34 : 14;
        _dropLine.Margin = new Thickness(left, Math.Max(0, top - 1), 10, 0);
        _dropLine.Classes.Set("on", true);
    }

    private void EndDrag()
    {
        _dragging = false;
        _dropSlot = -1;
        _dropLine.Classes.Set("on", false);
    }
}
