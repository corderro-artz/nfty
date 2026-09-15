using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Point = Avalonia.Point;

namespace Nfty.App.Tests;

/// <summary>
/// REORDERING IN THE CONTENTS TREE, BY DRAG AND BY KEYBOARD.
/// </summary>
/// <remarks>
/// <para>The tree could show the hierarchy and not change it: layer order was reachable only from
/// the Recipe pane's table, and recipe order was not reachable at all — it was whatever the ids
/// sorted into. A node moves among its OWN SIBLINGS and nowhere else, which is the one constraint
/// that makes the gesture safe to offer on two levels at once.</para>
///
/// <para>The two moves are not the same act, and the tests say so: a layer move changes which RNG
/// draw reaches which layer, so the same seed rolls a different collection, while a recipe move is
/// presentation and cannot change an asset (<c>RecipeOrderTests</c> in Core holds that line).</para>
/// </remarks>
public class ExplorerTreeReorderTests
{
    private static readonly string[] Layers = { "bg", "body", "hat" };

    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(8, 8) },
    };

    private static LoadedRecipe Rec(string id, params string[] layers) => new()
    {
        Manifest = new RecipeManifest(id, id.ToUpperInvariant(), layers, Array.Empty<IncompatibilityRule>()),
        Ingredients = layers.Select(Ing).ToArray(),
    };

    /// <summary>Two recipes whose ids sort ordinally as "cat" then "dog", the first with three
    /// layers.</summary>
    private static LoadedCookBook MemoryBook() => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"),
            new Dictionary<string, double> { ["cat"] = 60, ["dog"] = 40 }),
        Recipes = new[] { Rec("cat", Layers), Rec("dog", "body") },
    };

    private static (string Path, CookBookSession Session) OnDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        using (var seed = MemoryBook())
            CookBookArchive.Write(path, seed.Manifest, seed.Recipes);
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (path, session);
    }

    private static ExplorerViewModel Explorer(CookBookSession session)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
    }

    private static void Cleanup(CookBookSession session, string path)
    {
        session.Dispose();
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    private static string[] RecipeIds(ExplorerViewModel vm) => vm.Root.Children.Select(c => c.Id).ToArray();
    private static string[] LayerIds(ExplorerViewModel vm) =>
        vm.Root.Children[0].Children.Select(c => c.Id).ToArray();

    // ---- what may move ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void Nothing_moves_while_the_book_is_locked()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            Assert.False(vm.CanReorderTree);
            Assert.False(vm.CanMove(vm.Root.Children[0]));

            vm.ToggleLockCommand.Execute(null);
            Assert.True(vm.CanReorderTree);
            Assert.True(vm.CanMove(vm.Root.Children[0]));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>The root has no siblings, so it has nowhere to go.</summary>
    [AvaloniaFact]
    public void The_cookbook_root_never_moves()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            Assert.False(vm.CanMove(vm.Root));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A FILTERED TREE CANNOT BE REORDERED. What the reader sees is a subset of each parent's
    /// children, so a drop slot in it names a position that is not the position — the move would
    /// land somewhere the screen never showed.
    /// </summary>
    [AvaloniaFact]
    public void A_search_suspends_reordering_until_it_is_cleared()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            Assert.True(vm.CanReorderTree);

            vm.SearchQuery = "hat";
            Assert.False(vm.CanReorderTree);

            vm.ClearSearchCommand.Execute(null);
            Assert.True(vm.CanReorderTree);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// LOCKED MEANS LOCKED, ON EVERY ROUTE INTO A REORDER.
    /// </summary>
    /// <remarks>
    /// <para>A layer move is not a restack: <c>Generator.RollOne</c> walks <c>layerOrder</c>
    /// consuming one <c>WeightedRoller.Roll</c> per layer, so moving a layer moves which draw
    /// reaches it — and depth IS the paint order, so even an unchanged selection composites
    /// differently. The same seed over a reordered recipe is a different collection. That is exactly
    /// the class of edit the lock exists for, and it is why every route has to be gated rather than
    /// just the obvious one.</para>
    ///
    /// <para>The routes are: the tree's drag, the tree's Alt+Up/Down, and the two ViewModel seams
    /// each of them calls. The Recipe pane's own grip drag is gated by <c>CanReorder</c> and covered
    /// by <c>RecipeLayerReorderTests.A_locked_grip_takes_no_pointer_and_leaves_the_row_click_alone</c>.
    /// This is the sweep that says no new door was left open.</para>
    /// </remarks>
    [AvaloniaFact]
    public async Task No_route_into_a_reorder_works_while_the_book_is_locked()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);                      // opens READ-ONLY
            Assert.False(vm.IsEditing);

            var before = File.ReadAllBytes(path);
            var hat = vm.Root.Children[0].Children[2];
            var dog = vm.Root.Children[1];

            Assert.False(vm.CanReorderTree);
            Assert.False(vm.CanMove(hat));
            Assert.False(vm.CanMove(dog));

            // The seams the gestures call, executed directly - a gate that only dims a control is a
            // label, which is how the editor's own Save came to write into a locked book.
            Assert.False(await vm.MoveNodeAsync(hat, 0));
            Assert.False(await vm.MoveNodeByAsync(hat, -1));
            Assert.False(await vm.MoveNodeAsync(dog, 0));
            Assert.False(await vm.MoveNodeByAsync(dog, 1));

            Assert.Equal(Layers, LayerIds(vm));
            Assert.Equal(new[] { "cat", "dog" }, RecipeIds(vm));
            Assert.Equal(before, File.ReadAllBytes(path));         // and the archive never moved
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>The same, as gestures on the rendered tree: a locked row takes no drag and the chord
    /// does nothing.</summary>
    [AvaloniaFact]
    public void A_locked_tree_takes_no_drag_and_no_chord()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);                      // still locked
            vm.Root.Children[0].IsExpanded = true;
            var (window, view) = Show(vm);
            try
            {
                Dispatcher.UIThread.RunJobs();
                var dropLine = view.GetVisualDescendants().OfType<Border>()
                    .Single(b => b.Classes.Contains("dropline"));

                var hatRow = Row(view, vm.Root.Children[0].Children[2]);
                var bgRow = Row(view, vm.Root.Children[0].Children[0]);
                var above = Center(bgRow, window) - new Point(0, bgRow.Bounds.Height);

                window.MouseDown(Center(hatRow, window), MouseButton.Left);
                window.MouseMove(above);
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("on", dropLine.Classes);     // no line: the drag never started
                window.MouseUp(above, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Layers, LayerIds(vm));

                // Clicking a row still selects it - the lock governs structure, not navigation.
                Assert.Equal("hat", vm.SelectedNode?.Id);

                window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Layers, LayerIds(vm));
            }
            finally { window.Close(); }
        }
        finally { Cleanup(session, path); }
    }

    // ---- the moves themselves ---------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Moving_a_layer_in_the_tree_rewrites_the_paint_order_and_saves()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            Assert.Equal(Layers, LayerIds(vm));

            var hat = vm.Root.Children[0].Children[2];
            Assert.True(await vm.MoveNodeAsync(hat, 0));

            Assert.Equal(new[] { "hat", "bg", "body" }, LayerIds(vm));

            using var reread = CookBookArchive.Read(path);
            Assert.Equal(new[] { "hat", "bg", "body" },
                reread.Recipes.First(r => r.Manifest.Id == "cat").Manifest.LayerOrder);
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task Moving_a_recipe_in_the_tree_rewrites_the_listing_and_saves()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            Assert.Equal(new[] { "cat", "dog" }, RecipeIds(vm));

            var dog = vm.Root.Children[1];
            Assert.True(await vm.MoveNodeAsync(dog, 0));

            Assert.Equal(new[] { "dog", "cat" }, RecipeIds(vm));

            // And it survives the reload, which is the half a ViewModel assertion cannot make: a
            // listing that reached the manifest and not the reader is the same as no listing.
            using var reread = CookBookArchive.Read(path);
            Assert.Equal(new[] { "dog", "cat" }, reread.Recipes.Select(r => r.Manifest.Id));
            Assert.Equal(new[] { "dog", "cat" }, reread.Manifest.RecipeOrder);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>Alt+Up / Alt+Down, at the ViewModel seam the key handler calls. A nudge at either end
    /// is a no-op rather than an error — and therefore not a pointless whole-book save either.</summary>
    [AvaloniaFact]
    public async Task Moving_by_places_clamps_at_both_ends()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);

            Assert.False(await vm.MoveNodeByAsync(vm.Root.Children[0].Children[0], -1));
            Assert.Equal(Layers, LayerIds(vm));

            Assert.True(await vm.MoveNodeByAsync(vm.Root.Children[0].Children[0], 1));
            Assert.Equal(new[] { "body", "bg", "hat" }, LayerIds(vm));

            Assert.False(await vm.MoveNodeByAsync(vm.Root.Children[0].Children[2], 1));
            Assert.Equal(new[] { "body", "bg", "hat" }, LayerIds(vm));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>A node can only land among its OWN siblings — the gesture spans two levels and must
    /// never mix them. Dropping a layer at slot 9 clamps inside its recipe rather than escaping to
    /// the recipes' own list.</summary>
    [AvaloniaFact]
    public async Task A_layer_cannot_be_dropped_out_of_its_recipe()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);

            Assert.True(await vm.MoveNodeAsync(vm.Root.Children[0].Children[0], 9));
            Assert.Equal(new[] { "body", "hat", "bg" }, LayerIds(vm));
            Assert.Equal(new[] { "cat", "dog" }, RecipeIds(vm));      // the recipes did not move
            Assert.Equal(2, vm.Root.Children.Count);
        }
        finally { Cleanup(session, path); }
    }

    // ---- the gesture -------------------------------------------------------------------------------

    private static (Window Window, Views.ExplorerView View) Show(ExplorerViewModel vm)
    {
        var view = new Views.ExplorerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    /// <summary>The drawn row for a node — the <c>Border.node</c> its own container carries.</summary>
    private static Border Row(Visual view, ExplorerNode node) => view.GetVisualDescendants()
        .OfType<Border>()
        .First(b => b.Classes.Contains("node") && ReferenceEquals(b.DataContext, node));

    private static Point Center(Visual control, Visual window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? default;

    /// <summary>
    /// A REAL DRAG, on the rendered tree. Calling <c>MoveNodeAsync</c> is not a test that the tree
    /// invokes it — the same rule <c>ChanceFieldGestureTests</c> exists for.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_a_layer_over_its_sibling_shows_the_line_and_drops_it_there()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            vm.Root.Children[0].IsExpanded = true;
            var (window, view) = Show(vm);
            try
            {
                Dispatcher.UIThread.RunJobs();
                var dropLine = view.GetVisualDescendants().OfType<Border>()
                    .Single(b => b.Classes.Contains("dropline"));
                Assert.DoesNotContain("on", dropLine.Classes);

                var hatRow = Row(view, vm.Root.Children[0].Children[2]);
                var bgRow = Row(view, vm.Root.Children[0].Children[0]);
                var from = Center(hatRow, window);
                var above = Center(bgRow, window) - new Point(0, bgRow.Bounds.Height);

                window.MouseDown(from, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                // Nothing yet: a press is a selection until the pointer has actually travelled.
                Assert.DoesNotContain("on", dropLine.Classes);

                window.MouseMove(above);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("on", dropLine.Classes);

                window.MouseUp(above, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(new[] { "hat", "bg", "body" }, LayerIds(vm));
                Assert.DoesNotContain("on", dropLine.Classes);
            }
            finally { window.Close(); }
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A CLICK IS STILL A CLICK. The row is the drag handle, so the gesture that selects a node and
    /// the gesture that moves it begin identically — the travel threshold is the only thing keeping
    /// them apart, and without it every selection would be a reorder.
    /// </summary>
    [AvaloniaFact]
    public void A_press_and_release_in_place_selects_and_moves_nothing()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            vm.Root.Children[0].IsExpanded = true;
            var (window, view) = Show(vm);
            try
            {
                Dispatcher.UIThread.RunJobs();
                var hat = vm.Root.Children[0].Children[2];
                var at = Center(Row(view, hat), window);

                window.MouseDown(at, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                window.MouseUp(at, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(Layers, LayerIds(vm));
                Assert.Equal("hat", vm.SelectedNode?.Id);
            }
            finally { window.Close(); }
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// ALT+UP MOVES THE NODE UP THE LIST, pressed on the real control.
    /// </summary>
    /// <remarks>
    /// Driving the running app produced a move in the WRONG DIRECTION from this chord, which a
    /// ViewModel test cannot see: <c>MoveNodeByAsync</c> is right, and what was in question is
    /// whether a TreeView - which handles the arrow keys itself, for navigation - lets the chord
    /// reach this view at all and with which node selected. So this presses the key rather than
    /// calling the method, the way ChanceFieldGestureTests presses Enter.
    /// </remarks>
    [AvaloniaFact]
    public void Alt_up_moves_the_selected_layer_up_the_list()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            vm.Root.Children[0].IsExpanded = true;
            var (window, view) = Show(vm);
            try
            {
                Dispatcher.UIThread.RunJobs();
                // "hat" is the bottom layer; select it through the tree, as a click would.
                var hat = vm.Root.Children[0].Children[2];
                var row = Row(view, hat);
                window.MouseDown(Center(row, window), MouseButton.Left);
                window.MouseUp(Center(row, window), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("hat", vm.SelectedNode?.Id);

                window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
                // The key handler is async: it writes the whole book. Pump until the move lands
                // rather than assuming one RunJobs is enough - without this the test raced the
                // save and the temp directory could not even be deleted.
                PumpUntil(() => LayerIds(vm)[1] == "hat");

                Assert.Equal(new[] { "bg", "hat", "body" }, LayerIds(vm));
            }
            finally { window.Close(); }
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A BARE ARROW STILL NAVIGATES. The key handler tunnels, so it sees every key before the
    /// TreeView does — and it must claim only the chord it owns. Without this, taking the keys on
    /// the way down would have traded a reorder for the tree's own keyboard navigation.
    /// </summary>
    [AvaloniaFact]
    public void A_plain_arrow_key_still_moves_the_selection_and_nothing_else()
    {
        var (path, session) = OnDisk();
        try
        {
            using var vm = Explorer(session);
            vm.ToggleLockCommand.Execute(null);
            vm.Root.Children[0].IsExpanded = true;
            var (window, view) = Show(vm);
            try
            {
                Dispatcher.UIThread.RunJobs();
                var bg = vm.Root.Children[0].Children[0];
                var row = Row(view, bg);
                window.MouseDown(Center(row, window), MouseButton.Left);
                window.MouseUp(Center(row, window), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("bg", vm.SelectedNode?.Id);

                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("body", vm.SelectedNode?.Id);      // navigated
                Assert.Equal(Layers, LayerIds(vm));             // and moved nothing
            }
            finally { window.Close(); }
        }
        finally { Cleanup(session, path); }
    }

    private static void PumpUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Slot arithmetic, off the geometry it is computed from. Midpoints rather than edges,
    /// so the line flips to the far side of a row exactly halfway through it.</summary>
    [Fact]
    public void A_slot_is_decided_by_the_row_midpoint_it_is_above()
    {
        var rows = new (double Top, double Height)[] { (0, 20), (20, 20), (40, 20) };

        Assert.Equal(0, Views.ExplorerView.SlotAt(rows, 0));
        Assert.Equal(0, Views.ExplorerView.SlotAt(rows, 9));
        Assert.Equal(1, Views.ExplorerView.SlotAt(rows, 11));
        Assert.Equal(2, Views.ExplorerView.SlotAt(rows, 31));
        Assert.Equal(3, Views.ExplorerView.SlotAt(rows, 55));
    }
}
