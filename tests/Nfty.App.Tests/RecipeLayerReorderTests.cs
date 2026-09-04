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
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
// Both namespaces define Point and Size; this file measures layout, so Avalonia's win.
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace Nfty.App.Tests;

/// <summary>Drag- and keyboard-reorder in the Recipe detail's Layers table.</summary>
public class RecipeLayerReorderTests
{
    private static readonly string[] Stack = { "bg", "body", "hat" };

    // ---- fixtures ------------------------------------------------------------------------------

    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(8, 8) },
    };

    private static LoadedCookBook MemoryBook()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", Stack, Array.Empty<IncompatibilityRule>()),
            Ingredients = Stack.Select(Ing).ToArray(),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    /// <summary>A three-layer .cbk on disk, opened in a session — the shape the Explorer needs before
    /// it will write anything.</summary>
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

    private static ExplorerViewModel Explorer(CookBookSession session, IStatusService status)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
    }

    /// <summary>The detail pane over an in-memory book, with a reorder that reorders and counts but
    /// never touches a disk — so a view-level test settles synchronously instead of racing a write.</summary>
    private static RecipeDetailViewModel MemoryPane(bool canReorder, out Func<int> moves)
    {
        var book = MemoryBook();
        var current = book;
        int count = 0;
        moves = () => count;
        Task<LoadedCookBook?> Move(string ingredientId, int depth)
        {
            count++;
            current = CookBookEdits.MoveLayer(current, "cat", ingredientId, depth);
            return Task.FromResult<LoadedCookBook?>(current);
        }
        return new RecipeDetailViewModel(book.Recipes[0], book, new ImageBridge(),
            _ => { }, Move, canReorder);
    }

    private static (Window Window, Views.RecipeDetailView View) Show(RecipeDetailViewModel vm)
    {
        var view = new Views.RecipeDetailView { DataContext = vm };
        // The mockups' own units, as every other rendered check in this suite uses.
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static List<Border> Grips(Visual view) => view.GetVisualDescendants()
        .OfType<Border>().Where(b => b.Classes.Contains("grip")).ToList();

    // ---- the move itself -----------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Reorder_moves_the_bound_row_and_renumbers_every_depth()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));

            var hat = pane.Layers[2];
            Assert.True(await pane.MoveToDepthAsync(hat, 1));

            Assert.Equal(new[] { "hat", "bg", "body" }, pane.Layers.Select(l => l.Id));
            Assert.Equal(new[] { 1, 2, 3 }, pane.Layers.Select(l => l.Index));
            // MOVED, not rebuilt: the row objects are the ones the containers - and therefore the
            // grip's focus - are already bound to.
            Assert.Same(hat, pane.Layers[0]);
            // The factor chips follow the stack, and only the first may omit its "x".
            Assert.Equal(new[] { "HAT", "BG", "BODY" }, pane.Factors.Select(f => f.Name));
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task A_move_persists_and_survives_a_reload()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);

            Assert.True(await pane.MoveToDepthAsync(pane.Layers[0], 3));   // bg to the top of the stack

            using var reread = CookBookArchive.Read(path);
            Assert.Equal(new[] { "body", "hat", "bg" }, reread.Recipes[0].Manifest.LayerOrder);
            // ...and the tree the user is looking at agrees with the file.
            Assert.Equal(new[] { "body", "hat", "bg" },
                explorer.Root.Children[0].Children.Select(n => n.Id));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task A_move_off_either_end_is_a_no_op_rather_than_an_error()
    {
        var pane = MemoryPane(canReorder: true, out var moves);
        try
        {
            pane.SelectedLayer = pane.Layers[0];
            Assert.False(await pane.MoveSelectedRowsAsync(-1));    // already the top row
            pane.SelectedLayer = pane.Layers[^1];
            Assert.False(await pane.MoveSelectedRowsAsync(1));     // already the bottom row
            Assert.False(await pane.MoveToDepthAsync(pane.Layers[0], -40));
            Assert.False(await pane.MoveToDepthAsync(pane.Layers[^1], 900));

            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));
            // Clamped BEFORE anything is written, so a nudge off the end is not a pointless save
            // either - which matters when every save rewrites the whole .cbk.
            Assert.Equal(0, moves());
        }
        finally { pane.Dispose(); }
    }

    // ---- the gate ------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Reorder_is_refused_while_the_book_is_locked()
    {
        var (path, session) = OnDisk();
        var status = new StatusService();
        try
        {
            using var explorer = Explorer(session, status);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            Assert.False(pane.CanReorder);
            Assert.False(await pane.MoveToDepthAsync(pane.Layers[2], 1));

            // And the Explorer refuses on its own account, not merely because the pane declined to
            // ask: this is the gate that actually guards the file.
            Assert.Null(await explorer.MoveLayerAsync("cat", "hat", 1));
            Assert.Contains("locked", status.Last, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));
            using var reread = CookBookArchive.Read(path);
            Assert.Equal(Stack, reread.Recipes[0].Manifest.LayerOrder);
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task An_in_memory_book_with_no_file_says_so_rather_than_throwing()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var status = new StatusService();
        using var book = MemoryBook();
        using var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            new CookBookSession(), new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), status);
        explorer.ToggleLockCommand.Execute(null);

        Assert.Null(await explorer.MoveLayerAsync("cat", "hat", 1));
        Assert.Contains("read-only", status.Last, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void Flipping_the_lock_reaches_an_open_pane_without_rebuilding_it()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            Assert.False(pane.CanReorder);

            explorer.ToggleLockCommand.Execute(null);

            Assert.True(pane.CanReorder);
            // Same instance: rebuilding the pane to refresh the lock would reflow the page and drop
            // the row selection the keyboard reorder moves.
            Assert.Same(pane, explorer.CurrentDetail);
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task A_move_leaves_the_same_pane_open_on_the_saved_graph()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);

            await pane.MoveToDepthAsync(pane.Layers[2], 1);

            Assert.Same(pane, explorer.CurrentDetail);            // never rebuilt
            Assert.Same(session.Current, explorer.BookForTest);   // but the tree IS on the saved graph
            Assert.Equal("cat", explorer.SelectedNode!.Id);
        }
        finally { Cleanup(session, path); }
    }

    // ---- keyboard ------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Alt_up_and_alt_down_move_the_selected_row()
    {
        var pane = MemoryPane(canReorder: true, out _);
        try
        {
            pane.SelectedLayer = pane.Layers[2];                     // "hat", the bottom row
            Assert.True(await pane.MoveSelectedRowsAsync(-1));       // Alt+Up: one row up the table
            Assert.Equal(new[] { "bg", "hat", "body" }, pane.Layers.Select(l => l.Id));
            Assert.Equal(2, pane.SelectedLayer!.Index);              // selection travels with the row

            Assert.True(await pane.MoveSelectedRowsAsync(1));        // Alt+Down: back where it was
            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));
            Assert.Equal("hat", pane.SelectedLayer!.Id);
        }
        finally { pane.Dispose(); }
    }

    [AvaloniaFact]
    public void Alt_up_pressed_on_a_focused_grip_reaches_the_view_model()
    {
        var pane = MemoryPane(canReorder: true, out _);
        var (window, view) = Show(pane);
        try
        {
            var hat = pane.Layers[2];
            pane.SelectedLayer = hat;
            Grips(view)[2].Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "bg", "hat", "body" }, pane.Layers.Select(l => l.Id));

            // The second keystroke is the one that proves the feature: it only lands if the first
            // move kept the selection AND left focus inside this pane.
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "hat", "bg", "body" }, pane.Layers.Select(l => l.Id));

            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "bg", "hat", "body" }, pane.Layers.Select(l => l.Id));
        }
        finally { window.Close(); pane.Dispose(); }
    }

    [AvaloniaFact]
    public void Alt_up_does_nothing_while_locked()
    {
        var pane = MemoryPane(canReorder: false, out var moves);
        var (window, view) = Show(pane);
        try
        {
            pane.SelectedLayer = pane.Layers[2];
            Grips(view)[2].Focus();      // the style leaves it unfocusable, so this is a no-op too
            Dispatcher.UIThread.RunJobs();
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));
            Assert.Equal(0, moves());
        }
        finally { window.Close(); pane.Dispose(); }
    }

    // ---- drag ----------------------------------------------------------------------------------

    [AvaloniaTheory]
    // rows are 0..30, 30..60, 60..90; a slot is the gap ABOVE the row of that index
    [InlineData(2, 0)]     // above the first row's midpoint
    [InlineData(20, 1)]    // past it
    [InlineData(50, 2)]
    [InlineData(200, 3)]   // below everything
    public void The_drop_line_lands_in_the_slot_the_pointer_is_nearest(double y, int expected)
    {
        var rows = new[] { (0d, 30d), (30d, 30d), (60d, 30d) };
        Assert.Equal(expected, Views.RecipeDetailView.SlotAt(rows, y));
    }

    /// <summary>
    /// A slot is the gap BETWEEN rows, so one below the dragged row is a place higher once that row is
    /// lifted out; dropping into either gap touching the row leaves it where it is. Driven through the
    /// ViewModel because the translation needs the rows' real depths — which is why it moved out of the
    /// view, where it only had a row's position to work from.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0, new[] { "body", "bg", "hat" })]   // above everything
    [InlineData(1, new[] { "bg", "body", "hat" })]   // the gap above itself: unchanged
    [InlineData(2, new[] { "bg", "body", "hat" })]   // the gap below itself: also unchanged
    [InlineData(3, new[] { "bg", "hat", "body" })]   // below everything
    public async Task A_slot_maps_to_the_depth_the_row_ends_up_at(int slot, string[] expected)
    {
        var pane = MemoryPane(canReorder: true, out _);
        try
        {
            await pane.MoveToSlotAsync(pane.Layers[1], slot);   // drag "body", the middle row
            Assert.Equal(expected, pane.Layers.Select(l => l.Id));
        }
        finally { pane.Dispose(); }
    }

    [AvaloniaFact]
    public void Dragging_a_grip_shows_the_drop_line_and_drops_the_layer_where_it_lands()
    {
        var pane = MemoryPane(canReorder: true, out _);
        var (window, view) = Show(pane);
        try
        {
            var dropLine = view.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("dropline"));
            Assert.DoesNotContain("on", dropLine.Classes);

            var grip = Grips(view)[2];                                  // "hat", the bottom row
            var from = Center(grip, window);
            var topRow = Grips(view)[0];
            var above = Center(topRow, window) - new Point(0, topRow.Bounds.Height);

            window.MouseDown(from, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("on", dropLine.Classes);               // shown as soon as the drag begins
            Assert.Same(pane.Layers[2], pane.SelectedLayer);            // pressing a grip selects its row

            window.MouseMove(above);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(above, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { "hat", "bg", "body" }, pane.Layers.Select(l => l.Id));
            Assert.DoesNotContain("on", dropLine.Classes);              // and hidden again on release
        }
        finally { window.Close(); pane.Dispose(); }
    }

    /// <summary>Only the left button drags. A right-click on a grip used to capture the pointer, show
    /// the drop line and commit — and persist — a reorder on release, which is both a gesture nobody
    /// asked for and one that rewrites the whole .cbk. Avalonia reports a touch contact and a pen tip
    /// as the left button too, so the guard costs the touch path nothing; the left half of this test
    /// is what says so.</summary>
    [AvaloniaFact]
    public void Only_the_left_button_starts_a_drag()
    {
        var pane = MemoryPane(canReorder: true, out var moves);
        var (window, view) = Show(pane);
        try
        {
            var target = Center(Grips(view)[0], window) - new Point(0, Grips(view)[0].Bounds.Height);

            window.MouseDown(Center(Grips(view)[2], window), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(target);
            window.MouseUp(target, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, moves());
            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));

            window.MouseDown(Center(Grips(view)[2], window), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(target);
            window.MouseUp(target, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, moves());
            Assert.Equal(new[] { "hat", "bg", "body" }, pane.Layers.Select(l => l.Id));
        }
        finally { window.Close(); pane.Dispose(); }
    }

    [AvaloniaFact]
    public void A_locked_grip_takes_no_pointer_and_leaves_the_row_click_alone()
    {
        var pane = MemoryPane(canReorder: false, out var moves);
        var (window, view) = Show(pane);
        try
        {
            var grip = Grips(view)[2];
            Assert.False(grip.IsHitTestVisible);   // the press falls through to the row button beneath
            Assert.False(grip.Focusable);          // and Tab does not stop on an invisible control
            window.MouseDown(Center(grip, window), MouseButton.Left);
            window.MouseUp(Center(grip, window), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, moves());
        }
        finally { window.Close(); pane.Dispose(); }
    }

    // ---- geometry ------------------------------------------------------------------------------

    /// <summary>The constraint the spec calls the one most likely to be violated by accident, and the
    /// one a reading of the markup cannot settle: the exploration draws variant B PREPENDING its grip
    /// column on unlock, which shifts every other column 26px right. Here the column is always in the
    /// layout and only the ink changes — so every cell must land on exactly the same pixel in both
    /// states, while the grip's opacity must NOT.</summary>
    [AvaloniaFact]
    public void The_layers_table_is_pixel_identical_locked_and_unlocked()
    {
        var pane = MemoryPane(canReorder: false, out _);
        var (window, view) = Show(pane);
        try
        {
            var locked = Boxes(view);
            var lockedGrip = Grips(view)[0].Bounds;
            Assert.Equal(0, Grips(view)[0].Opacity);
            Assert.True(lockedGrip.Width > 0, "the grip column must occupy layout while locked");

            pane.CanReorder = true;
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, Grips(view)[0].Opacity);      // the ink changed...
            Assert.Equal(lockedGrip, Grips(view)[0].Bounds);
            Assert.Equal(locked, Boxes(view));            // ...and nothing moved
        }
        finally { window.Close(); pane.Dispose(); }
    }

    [AvaloniaFact]
    public void The_table_carries_a_permanent_hint_for_what_depth_one_means()
    {
        foreach (var canReorder in new[] { false, true })
        {
            var pane = MemoryPane(canReorder, out _);
            var (window, view) = Show(pane);
            try
            {
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.IsEffectivelyVisible && t.Text == "1 → paints first, furthest back");
            }
            finally { window.Close(); pane.Dispose(); }
        }
    }

    /// <summary>Every visible text run with the box it occupies, in the view's own coordinates.</summary>
    private static List<(string Text, Rect Box)> Boxes(Visual view) => view.GetVisualDescendants()
        .OfType<TextBlock>()
        .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text))
        .Select(t => (t.Text!, new Rect(t.TranslatePoint(default, view) ?? default, t.Bounds.Size)))
        .ToList();

    private static Point Center(Visual control, Visual window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
        ?? default;

    // ---- canceling, colliding, and books whose stack does not add up ---------------------------

    /// <summary>
    /// Esc abandons a drag. A captured drag has no other way out — release commits, and capture-lost
    /// has already ended it — so without this the universal "never mind" gesture was unavailable on
    /// an action that, per this feature's spec, produces a <i>different collection</i> rather than a
    /// restacked one.
    /// </summary>
    [AvaloniaFact]
    public void Esc_abandons_a_drag_and_moves_nothing()
    {
        var pane = MemoryPane(canReorder: true, out var moves);
        var (window, view) = Show(pane);
        try
        {
            var dropLine = view.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("dropline"));
            var topRow = Grips(view)[0];
            var above = Center(topRow, window) - new Point(0, topRow.Bounds.Height);

            window.MouseDown(Center(Grips(view)[2], window), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(above);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("on", dropLine.Classes);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("on", dropLine.Classes);

            // And the release that inevitably follows must not resurrect the drop either.
            window.MouseUp(above, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, moves());
            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));
        }
        finally { window.Close(); pane.Dispose(); }
    }

    /// <summary>
    /// Dragging away from the table and letting go cancels. The pointer is captured, so a release
    /// anywhere on screen still arrives at the handler — which used to commit the last slot the line
    /// happened to occupy, turning "I changed my mind" into a rewrite of the archive.
    /// </summary>
    [AvaloniaFact]
    public void Releasing_outside_the_table_cancels_instead_of_dropping()
    {
        var pane = MemoryPane(canReorder: true, out var moves);
        var (window, view) = Show(pane);
        try
        {
            var topRow = Grips(view)[0];
            var above = Center(topRow, window) - new Point(0, topRow.Bounds.Height);

            window.MouseDown(Center(Grips(view)[2], window), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(above);          // over a real slot, so a commit WOULD have moved it
            Dispatcher.UIThread.RunJobs();

            // Far below every row: still delivered here because of the capture, but nowhere near
            // the table the gesture belongs to.
            window.MouseUp(new Point(600, 700), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, moves());
            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));
        }
        finally { window.Close(); pane.Dispose(); }
    }

    /// <summary>
    /// A <c>layerOrder</c> entry naming an ingredient the recipe does not carry makes a row's position
    /// stop being its depth — the table can only show the entries that resolve, so with positional
    /// numbering a move landed somewhere the user had not pointed at, silently, having rewritten the
    /// archive.
    ///
    /// <para>Numbering the rows from <c>LayerDepth</c> rather than from their filtered position is what
    /// removes that: <c>Index</c> is a depth by construction, so the move is simply <b>correct</b> on
    /// this book too and needs no gate. The phantom entry keeps its place; the moved layer lands where
    /// the row it displaced was.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task A_recipe_listing_a_layer_it_does_not_carry_still_reorders_by_real_depth()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var ingredients = Stack.Select(Ing).ToArray();
        try
        {
            var recipe = new LoadedRecipe
            {
                // "ghost" is stacked but not carried.
                Manifest = new RecipeManifest("cat", "Cat",
                    new[] { "ghost" }.Concat(Stack).ToArray(), Array.Empty<IncompatibilityRule>()),
                Ingredients = ingredients,
            };
            var manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 });
            CookBookArchive.Write(path, manifest, new[] { recipe });
        }
        finally { foreach (var i in ingredients) i.Dispose(); }

        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        var status = new StatusService();
        try
        {
            using var explorer = Explorer(session, status);
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);

            // The rows skip the phantom but keep the REAL depths: 2, 3, 4 — not 1, 2, 3.
            Assert.Equal(Stack, pane.Layers.Select(l => l.Id));
            Assert.Equal(new[] { 2, 3, 4 }, pane.Layers.Select(l => l.Index));

            // Move the bottom row to the top of what is visible, i.e. to bg's real depth of 2.
            Assert.True(await pane.MoveToDepthAsync(pane.Layers[2], 2));

            Assert.Equal(new[] { "hat", "bg", "body" }, pane.Layers.Select(l => l.Id));
            Assert.Equal(new[] { 2, 3, 4 }, pane.Layers.Select(l => l.Index));

            using var reread = CookBookArchive.Read(path);
            // The phantom kept its place; only the real layers moved around it.
            Assert.Equal(new[] { "ghost", "hat", "bg", "body" }, reread.Recipes[0].Manifest.LayerOrder);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// Two reorders cannot be in flight at once. <c>OnKeyDown</c> is <c>async void</c> and reentrant,
    /// so key auto-repeat fired a second move before the first had saved: the two collided on
    /// <c>book.cbk.tmp</c>, and the loser had already recomputed from the stale graph, so a keystroke
    /// was silently discarded on top of the error dialog. Refused and said, not queued — a queued move
    /// would apply against a stack the user can no longer see.
    /// </summary>
    [AvaloniaFact]
    public async Task A_second_reorder_while_one_is_saving_is_refused_rather_than_colliding()
    {
        var (path, session) = OnDisk();
        var status = new StatusService();
        try
        {
            using var explorer = Explorer(session, status);
            explorer.ToggleLockCommand.Execute(null);

            // Started without awaiting, exactly as auto-repeat does it.
            var first = explorer.MoveLayerAsync("cat", "hat", 1);
            var second = explorer.MoveLayerAsync("cat", "bg", 3);
            var results = await Task.WhenAll(first, second);

            Assert.Single(results, r => r is not null);
            Assert.Contains("moment", status.Last, StringComparison.OrdinalIgnoreCase);

            // Exactly one move landed, and the file agrees with the tree.
            using var reread = CookBookArchive.Read(path);
            Assert.Equal(new[] { "hat", "bg", "body" }, reread.Recipes[0].Manifest.LayerOrder);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A reorder rewrites every PNG in the book, so its save can land after the user has navigated
    /// away and the Explorer has disposed the pane that asked. Re-projecting a dead pane disposed its
    /// hero a second time and built a fresh canvas-sized bitmap nothing would ever free.
    /// </summary>
    [AvaloniaFact]
    public async Task A_reorder_that_lands_after_the_pane_is_gone_leaves_it_alone()
    {
        var pane = MemoryPane(canReorder: true, out var moves);
        var before = pane.Layers.Select(l => l.Id).ToArray();

        pane.Dispose();
        pane.Dispose();   // idempotent: the Explorer may dispose, and teardown dispose again

        // The reply to a request this pane made is simply no longer wanted.
        Assert.True(await pane.MoveToDepthAsync(pane.Layers[2], 1));
        Assert.Equal(1, moves());                                   // the edit still reached the book
        Assert.Equal(before, pane.Layers.Select(l => l.Id));        // but the dead pane was not touched
    }

    /// <summary>
    /// The hero is rolled OFF the UI thread now — a whole generated asset, 25 ms at 512px and 75 ms at
    /// 1000px, which used to freeze the window on every reorder keystroke and every Reroll click. That
    /// introduces an await the pane can be disposed across, so the disposed latch is checked on both
    /// sides of it: a roll that finishes after the user has navigated away drops its image rather than
    /// assigning it to a dead view.
    /// </summary>
    [AvaloniaFact]
    public async Task A_hero_roll_that_finishes_after_the_pane_is_gone_is_dropped()
    {
        var pane = MemoryPane(canReorder: true, out _);
        var hero = pane.Hero;

        pane.Dispose();
        await pane.RerollCommand.ExecuteAsync(null);

        Assert.Same(hero, pane.Hero);
    }

    /// <summary>And on a live pane it does what it says: a new seed, a new hero.</summary>
    [AvaloniaFact]
    public async Task Rerolling_a_live_pane_replaces_the_hero()
    {
        var pane = MemoryPane(canReorder: true, out _);
        try
        {
            var before = pane.Hero;
            int seed = pane.RollSeed;

            await pane.RerollCommand.ExecuteAsync(null);

            Assert.NotEqual(seed, pane.RollSeed);
            Assert.NotSame(before, pane.Hero);
        }
        finally { pane.Dispose(); }
    }

    private static void Cleanup(CookBookSession session, string path)
    {
        session.Dispose();
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }
}
