using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Ingredient Editor's reference-layer panel: the above/below split, the two cached
/// stacks it composites from, Kitchen placement, and the refusals.</summary>
public class IngredientEditorReferencesTests
{
    internal const int Canvas = 16;

    /// <summary>A value-map (or full-colour) layer that paints one horizontal band, so a composite is
    /// legible band by band rather than being one flat wash.</summary>
    internal static Image<Rgba32> Band(int top, int bottom, byte value, Rgba32? colour = null)
    {
        var img = new Image<Rgba32>(Canvas, Canvas);
        var px = colour ?? new Rgba32(value, value, value, 255);
        for (int y = top; y <= bottom; y++)
            for (int x = 0; x < Canvas; x++)
                img[x, y] = px;
        return img;
    }

    internal static LoadedIngredient Ing(string id, string name, LayerKind kind, Image<Rgba32> image)
    {
        var colorization = kind == LayerKind.Custom ? null
            : new Colorization(ColorModel.Hsv, 12, 4, new[]
            {
                kind == LayerKind.Static
                    ? new ColorEntry(1, null, "hex:d6249f")
                    : new ColorEntry(1, new ColorRange(0, 360, 40, 100), null),
            });
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, name, kind, colorization,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = image },
        };
    }

    /// <summary>The exploration's own stack: Body(1) and Ears(2) under, Eyes(3) being edited,
    /// Accessory(4) over. Each layer paints its own band, so who is above whom is visible.</summary>
    internal static (LoadedCookBook Book, LoadedRecipe Recipe, LoadedIngredient Edited) FourLayerStack()
    {
        var body = Ing("body", "Body", LayerKind.Dynamic, Band(12, 15, 200));
        var ears = Ing("ears", "Ears", LayerKind.Dynamic, Band(4, 5, 160));
        var eyes = Ing("eyes", "Eyes", LayerKind.Static, Band(8, 9, 220));
        var acc = Ing("accessory", "Accessory", LayerKind.Custom,
            Band(0, 2, 0, new Rgba32(20, 180, 90, 255)));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Aurora",
                new[] { "body", "ears", "eyes", "accessory" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { body, ears, eyes, acc },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(Canvas, Canvas),
                new Collection("VaporPets", "", "VP"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, eyes);
    }

    /// <summary>Writes a real Kitchen (a <c>.ktn</c> plus loose <c>.igt</c> files) into a fresh temp
    /// directory and opens a session over it. Nothing here goes near the real %APPDATA%.</summary>
    internal static (KitchenSession Session, string Dir) TempKitchen(params (string Id, int Size)[] loose)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var ktn = Path.Combine(dir, "workspace.ktn");
        Kitchen.Create(ktn, new KitchenManifest("ws", "VaporStudio"));

        foreach (var (id, size) in loose)
        {
            using var img = size == Canvas
                ? Band(6, 7, 0, new Rgba32(230, 60, 120, 255))
                : new Image<Rgba32>(size, size);
            IngredientArchive.Write(Path.Combine(dir, id + ".igt"),
                new IngredientManifest(id, id, LayerKind.Custom, null,
                    new[] { new Variant("a", "A", 1) }),
                new Dictionary<string, Image<Rgba32>> { ["a"] = img });
        }

        var session = new KitchenSession();
        session.Open(ktn);
        return (session, dir);
    }

    internal static IngredientEditorViewModel Editor(
        LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IKitchenSession? kitchen = null, IDialogService? dialogs = null) =>
        new(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), dialogs ?? new FakeDialogs(), new FilePickerService(),
            looseSavePath: null, kitchen: kitchen);

    /// <summary>Reads one pixel back off a rendered canvas bitmap, as RGBA.</summary>
    internal static (byte R, byte G, byte B, byte A) Pixel(Bitmap bmp, int x, int y)
    {
        var buffer = new byte[Canvas * Canvas * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new Avalonia.PixelRect(0, 0, Canvas, Canvas), (nint)p, buffer.Length, Canvas * 4);
        }
        int i = (y * Canvas + x) * 4;
        return (buffer[i], buffer[i + 1], buffer[i + 2], buffer[i + 3]);
    }

    private static ReferenceLayer Sibling(IngredientEditorViewModel vm, string id) =>
        vm.Siblings.First(s => s.Key == id);

    // ---- the split ------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Split_puts_each_sibling_on_the_correct_side_of_the_pinned_layer()
    {
        var (book, recipe, eyes) = FourLayerStack();
        using var vm = Editor(eyes, recipe, book);

        Assert.Equal(new[] { "body", "ears", "accessory" }, vm.Siblings.Select(s => s.Key));
        Assert.Equal(new[] { "1", "2", "4" }, vm.Siblings.Select(s => s.DepthText));
        Assert.Equal("3", vm.PinnedDepthText);

        // The tag is not derivable from the row's position on screen: body and ears sit ABOVE the
        // pinned row in the list while compositing BENEATH it.
        Assert.False(Sibling(vm, "body").IsAbove);
        Assert.False(Sibling(vm, "ears").IsAbove);
        Assert.True(Sibling(vm, "accessory").IsAbove);
        book.Dispose();
    }

    [AvaloniaFact]
    public void An_above_reference_paints_over_the_edited_layer_and_a_below_one_under_it()
    {
        var (book, recipe, eyes) = FourLayerStack();
        using var vm = Editor(eyes, recipe, book);
        vm.GhostAbove = false;   // measure the true composite, not the ghost

        // Accessory (depth 4, Custom, opaque green over rows 0..2) composites OVER; Body (depth 1)
        // composites UNDER and cannot reach row 0.
        vm.ToggleReferenceCommand.Execute(Sibling(vm, "accessory"));
        var over = Pixel(vm.Canvas, 4, 1);
        Assert.Equal((20, 180, 90, 255), over);

        vm.ToggleReferenceCommand.Execute(Sibling(vm, "accessory"));   // off again
        vm.ToggleReferenceCommand.Execute(Sibling(vm, "body"));
        // Body paints rows 12..15, so row 1 is empty and row 13 is not.
        Assert.Equal(0, Pixel(vm.Canvas, 4, 1).A);
        Assert.Equal(255, Pixel(vm.Canvas, 4, 13).A);
        book.Dispose();
    }

    [AvaloniaFact]
    public void Zero_references_renders_exactly_what_the_editor_drew_before()
    {
        var (book, recipe, eyes) = FourLayerStack();
        using var vm = Editor(eyes, recipe, book);

        Assert.Equal("0 / 3 on", vm.ReferenceCountText);
        // The edited layer's own band, and nothing else anywhere on the canvas.
        Assert.Equal(255, Pixel(vm.Canvas, 4, 8).A);
        Assert.Equal(0, Pixel(vm.Canvas, 4, 1).A);
        Assert.Equal(0, Pixel(vm.Canvas, 4, 13).A);
        book.Dispose();
    }

    [AvaloniaFact]
    public void Toggling_a_reference_changes_the_composite_and_the_count()
    {
        var (book, recipe, eyes) = FourLayerStack();
        using var vm = Editor(eyes, recipe, book);

        var before = vm.Canvas;
        vm.ToggleReferenceCommand.Execute(Sibling(vm, "body"));
        Assert.NotSame(before, vm.Canvas);
        Assert.Equal("1 / 3 on", vm.ReferenceCountText);
        Assert.Equal(255, Pixel(vm.Canvas, 4, 13).A);

        vm.ToggleReferenceCommand.Execute(Sibling(vm, "body"));
        Assert.Equal("0 / 3 on", vm.ReferenceCountText);
        Assert.Equal(0, Pixel(vm.Canvas, 4, 13).A);
        book.Dispose();
    }

    // ---- the performance claim ------------------------------------------------------------------

    [AvaloniaFact]
    public void The_cached_stacks_rebuild_on_a_selection_change_and_never_on_a_brush_stroke()
    {
        // This is THE performance claim of the feature - RebuildSurfaces() runs on every stroke and
        // every slider tick, and compositing N canvas-sized layers there would stutter at 1000x1000.
        // So it is asserted rather than assumed.
        var (book, recipe, eyes) = FourLayerStack();
        using var vm = Editor(eyes, recipe, book);

        vm.ToggleReferenceCommand.Execute(Sibling(vm, "body"));
        vm.ToggleReferenceCommand.Execute(Sibling(vm, "accessory"));
        int afterSelection = vm.StackRebuildCount;
        Assert.True(afterSelection >= 2, $"a selection change must rebuild; saw {afterSelection}");

        vm.ActiveTool = EditorTool.Fill;
        vm.BrushValue = 200;
        for (int i = 0; i < 5; i++) vm.ApplyToolStroke(new[] { (i, i) });
        vm.HueMin = 40;         // a slider tick, which also runs RebuildSurfaces
        vm.HueMax = 300;
        vm.RerollPreviewCommand.Execute(null);
        Assert.Equal(afterSelection, vm.StackRebuildCount);

        // ...and a placement change IS a selection change, so it must rebuild.
        vm.GhostAbove = false;
        Assert.Equal(afterSelection + 1, vm.StackRebuildCount);
        book.Dispose();
    }

    // ---- ghosting -------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Ghosting_dims_the_above_stack_and_leaves_the_below_stack_at_full_opacity()
    {
        var (book, recipe, eyes) = FourLayerStack();
        using var vm = Editor(eyes, recipe, book);
        Assert.True(vm.GhostAbove);   // the default: an above layer must never hide the art

        vm.ToggleReferenceCommand.Execute(Sibling(vm, "accessory"));   // above, rows 0..2
        vm.ToggleReferenceCommand.Execute(Sibling(vm, "body"));        // below, rows 12..15

        // 0.35 of 255, composited onto an empty canvas: the ghost keeps its own alpha.
        var ghosted = Pixel(vm.Canvas, 4, 1);
        Assert.Equal(89, ghosted.A);
        Assert.Equal((byte)255, Pixel(vm.Canvas, 4, 13).A);   // below is NEVER dimmed

        vm.ShowTrueColourCommand.Execute(null);
        Assert.Equal((byte)255, Pixel(vm.Canvas, 4, 1).A);
        Assert.Equal((byte)255, Pixel(vm.Canvas, 4, 13).A);

        vm.ShowGhostedCommand.Execute(null);
        Assert.Equal((byte)89, Pixel(vm.Canvas, 4, 1).A);
        book.Dispose();
    }

    // ---- Kitchen --------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task A_kitchen_layer_is_listed_placed_on_top_and_never_touches_a_sibling_depth()
    {
        var (book, recipe, eyes) = FourLayerStack();
        var (kitchen, dir) = TempKitchen(("shades", Canvas));
        try
        {
            using var vm = Editor(eyes, recipe, book, kitchen);
            var row = Assert.Single(vm.KitchenLayers);
            Assert.True(row.IsKitchen);
            Assert.Equal("—", row.DepthText);           // no canonical depth, ever
            Assert.Equal(4, row.Gap);                   // on top, like the CLI's `--with`
            Assert.Equal("over Accessory", row.PlacementText);
            Assert.True(row.IsAbove);

            var orderBefore = recipe.Manifest.LayerOrder.ToArray();
            var depthsBefore = vm.Siblings.Select(s => s.DepthText).ToArray();

            await vm.ToggleReferenceCommand.ExecuteAsync(row);
            Assert.True(row.IsOn);
            Assert.Equal("shades", row.Name);
            Assert.Equal("C", row.KindMark);            // adopted from the archive it just opened

            // Walk it down through every gap and back across the pin.
            vm.PlaceDownCommand.Execute(row);           // gap 3 - over Eyes, still above the pin
            Assert.Equal("over Eyes", row.PlacementText);
            Assert.True(row.IsAbove);
            vm.PlaceDownCommand.Execute(row);           // gap 2 - over Ears, now BELOW the pin
            Assert.Equal("over Ears", row.PlacementText);
            Assert.False(row.IsAbove);
            vm.PlaceDownCommand.Execute(row);
            vm.PlaceDownCommand.Execute(row);           // gap 0 - under everything
            Assert.Equal("under Body", row.PlacementText);
            Assert.False(row.CanStepDown);
            vm.PlaceDownCommand.Execute(row);           // clamped: a no-op, not an error
            Assert.Equal(0, row.Gap);

            // Placement is a GAP, so it can renumber nothing.
            Assert.Equal(orderBefore, recipe.Manifest.LayerOrder);
            Assert.Equal(depthsBefore, vm.Siblings.Select(s => s.DepthText));
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_kitchen_layer_composites_on_the_side_of_the_pin_its_gap_puts_it()
    {
        var (book, recipe, eyes) = FourLayerStack();
        var (kitchen, dir) = TempKitchen(("shades", Canvas));
        try
        {
            using var vm = Editor(eyes, recipe, book, kitchen);
            vm.GhostAbove = false;
            var row = vm.KitchenLayers[0];
            await vm.ToggleReferenceCommand.ExecuteAsync(row);

            // shades paints rows 6..7 in opaque pink; the edited Eyes layer paints rows 8..9, so the
            // two do not overlap and the pink must simply be present either way. What changes is
            // which side of the pin it is reported on.
            Assert.True(row.IsAbove);
            Assert.Equal((230, 60, 120, 255), Pixel(vm.Canvas, 4, 6));

            vm.PlaceDownCommand.Execute(row);
            vm.PlaceDownCommand.Execute(row);   // gap 2 - below the pin now
            Assert.False(row.IsAbove);
            Assert.Equal((230, 60, 120, 255), Pixel(vm.Canvas, 4, 6));

            // ...and back above, where ghosting reaches it while the below-stack is untouched.
            vm.PlaceUpCommand.Execute(row);
            vm.PlaceUpCommand.Execute(row);
            vm.ShowGhostedCommand.Execute(null);
            Assert.Equal(89, Pixel(vm.Canvas, 4, 6).A);
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_canvas_mismatched_kitchen_file_is_refused_with_both_sizes_named()
    {
        var (book, recipe, eyes) = FourLayerStack();
        var (kitchen, dir) = TempKitchen(("toobig", 32));
        var dialogs = new FakeDialogs();
        try
        {
            using var vm = Editor(eyes, recipe, book, kitchen, dialogs);
            var row = vm.KitchenLayers[0];
            await vm.ToggleReferenceCommand.ExecuteAsync(row);

            Assert.False(row.IsOn);                     // refused, not scaled
            var error = Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
            Assert.Contains("32x32", error.Message, StringComparison.Ordinal);
            Assert.Contains("16x16", error.Message, StringComparison.Ordinal);
            Assert.Contains("never scales", error.Message, StringComparison.Ordinal);
            Assert.Equal("0 / 4 on", vm.ReferenceCountText);
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void A_reference_whose_fixed_colour_cannot_be_parsed_is_reported_not_thrown()
    {
        // A colour spec is parsed when the layer is DRAWN, not when the archive is read, and Validator
        // only REPORTS an unprefixed one - so a book that opens saying "1 problem" carries static
        // layers whose colour first fails right here, inside a repaint. Escaping the filter would let
        // the exception out through AsyncRelayCommand onto the dispatcher, where the desktop head
        // installs no handler at all.
        var (book, recipe, eyes) = FourLayerStack();
        var bad = new LoadedIngredient
        {
            // "d6249f" is missing its hex: prefix - the one mistake the colour-spec rule exists to refuse.
            Manifest = new IngredientManifest("bad", "Bad", LayerKind.Static,
                new Colorization(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, null, "d6249f") }),
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = Band(0, 3, 200) },
        };
        var broken = new LoadedRecipe
        {
            Manifest = recipe.Manifest with { LayerOrder = new[] { "body", "bad", "ears", "eyes", "accessory" } },
            Ingredients = recipe.Ingredients.Append(bad).ToList(),
        };
        var brokenBook = new LoadedCookBook { Manifest = book.Manifest, Recipes = new[] { broken } };
        try
        {
            Assert.Contains(Validator.Validate(brokenBook),
                p => p.Contains("invalid fixed color", StringComparison.Ordinal));

            using var vm = Editor(eyes, broken, brokenBook);
            Assert.Null(Record.Exception(() => vm.ToggleReferenceCommand.Execute(Sibling(vm, "bad"))));
            Assert.Contains("missing a prefix", vm.StackProblem!, StringComparison.Ordinal);
            Assert.NotNull(vm.Canvas);   // and the layer being painted still draws
        }
        finally { brokenBook.Dispose(); }
    }

    [AvaloniaFact]
    public void With_no_kitchen_open_the_section_says_so_rather_than_showing_an_empty_box()
    {
        var (book, recipe, eyes) = FourLayerStack();
        using var vm = Editor(eyes, recipe, book);
        Assert.False(vm.HasKitchen);
        Assert.False(vm.HasKitchenLayers);
        Assert.Contains("No Kitchen is open", vm.NoKitchenText, StringComparison.Ordinal);
        book.Dispose();
    }

    // ---- the loose path -------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_loose_igt_has_no_siblings_and_the_section_says_so()
    {
        // The loose editor wraps the ingredient in a synthetic single-layer book, so "In this recipe"
        // is legitimately empty. A real state, not an error.
        using var loose = Ing("aura", "Aura", LayerKind.Dynamic, Band(6, 9, 180));
        var book = LooseWorkspace.WrapIngredient(loose);
        using var vm = new IngredientEditorViewModel(loose, book.Recipes[0], book, new ImageBridge(),
            new FakeNav(), new CookBookSession(), new FakeDialogs(),
            new FilePickerService(), looseSavePath: Path.Combine(Path.GetTempPath(), "aura.igt"));

        Assert.True(vm.ReferencesAvailable);
        Assert.Empty(vm.Siblings);
        Assert.False(vm.HasSiblings);
        Assert.Contains("belongs to no recipe", vm.NoSiblingsText, StringComparison.Ordinal);
        Assert.Equal("1", vm.PinnedDepthText);
        Assert.NotNull(vm.Canvas);
    }

    [AvaloniaFact]
    public async Task A_loose_igt_can_still_line_up_against_a_kitchen_layer()
    {
        using var loose = Ing("aura", "Aura", LayerKind.Dynamic, Band(8, 9, 180));
        var book = LooseWorkspace.WrapIngredient(loose);
        var (kitchen, dir) = TempKitchen(("shades", Canvas));
        try
        {
            using var vm = new IngredientEditorViewModel(loose, book.Recipes[0], book, new ImageBridge(),
                new FakeNav(), new CookBookSession(), new FakeDialogs(),
                new FilePickerService(), looseSavePath: Path.Combine(dir, "aura.igt"), kitchen: kitchen);

            var row = Assert.Single(vm.KitchenLayers);
            Assert.Equal("over Aura", row.PlacementText);    // the only layer there is
            await vm.ToggleReferenceCommand.ExecuteAsync(row);
            vm.ShowTrueColourCommand.Execute(null);
            Assert.Equal((230, 60, 120, 255), Pixel(vm.Canvas, 4, 6));

            vm.PlaceDownCommand.Execute(row);
            Assert.Equal("under Aura", row.PlacementText);
            Assert.False(row.IsAbove);
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void A_loose_igt_saved_into_the_kitchen_is_not_offered_as_a_reference_to_itself()
    {
        using var loose = Ing("shades", "Shades", LayerKind.Custom, Band(6, 7, 0, new Rgba32(1, 2, 3, 255)));
        var book = LooseWorkspace.WrapIngredient(loose);
        var (kitchen, dir) = TempKitchen(("shades", Canvas), ("visor", Canvas));
        try
        {
            using var vm = new IngredientEditorViewModel(loose, book.Recipes[0], book, new ImageBridge(),
                new FakeNav(), new CookBookSession(), new FakeDialogs(),
                new FilePickerService(),
                looseSavePath: Path.Combine(dir, "shades.igt"), kitchen: kitchen);

            // The file on disk is a stale copy of what is being painted; offering it would let the
            // editor line the art up against its own last save.
            Assert.Equal(new[] { "visor" }, vm.KitchenLayers.Select(k => k.Name));
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    // ---- a recipe that does not stack the edited layer -------------------------------------------

    [AvaloniaFact]
    public void A_recipe_that_does_not_stack_the_edited_layer_simply_offers_no_references()
    {
        var (book, recipe, _) = FourLayerStack();
        using var orphan = Ing("orphan", "Orphan", LayerKind.Dynamic, Band(0, 1, 90));
        using var vm = Editor(orphan, recipe, book);

        Assert.False(vm.ReferencesAvailable);   // there is no depth to split the stack around
        Assert.Empty(vm.Siblings);
        Assert.Equal("—", vm.PinnedDepthText);
        Assert.NotNull(vm.Canvas);              // and the editor still opens
        book.Dispose();
    }

    // ---- disposal -------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Nothing_the_panel_allocates_outlives_the_editor()
    {
        var (kitchen, dir) = TempKitchen(("shades", Canvas));
        try
        {
            MemoryDiagnostics.UndisposedAllocation += Noop;
            long before = MemoryDiagnostics.TotalUndisposedAllocationCount;
            {
                var (book, recipe, eyes) = FourLayerStack();
                var vm = Editor(eyes, recipe, book, kitchen);
                await vm.ToggleReferenceCommand.ExecuteAsync(vm.KitchenLayers[0]);
                vm.ToggleReferenceCommand.Execute(Sibling(vm, "body"));
                vm.ToggleReferenceCommand.Execute(Sibling(vm, "accessory"));
                vm.ShowTrueColourCommand.Execute(null);
                vm.ShowGhostedCommand.Execute(null);
                vm.PlaceDownCommand.Execute(vm.KitchenLayers[0]);
                // Note: Image.Width does NOT throw after Dispose, so a disposed image cannot be
                // detected by poking at it - the allocation counter is the only honest witness.
                vm.Dispose();
                book.Dispose();
            }
            Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
        }
        finally
        {
            MemoryDiagnostics.UndisposedAllocation -= Noop;
            Directory.Delete(dir, recursive: true);
        }

        static void Noop(string _) { }
    }

    // ---- geometry -------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task An_active_row_is_exactly_as_tall_as_an_inactive_one()
    {
        // The spec's hard rule, and the one variant B breaks AS DRAWN: activating a layer makes the
        // over/under tag and the placement stepper appear, and the row grows to fit them. Every one
        // of those already owns its box here, so this measures the real laid-out heights rather than
        // trusting the markup.
        var (book, recipe, eyes) = FourLayerStack();
        var (kitchen, dir) = TempKitchen(("shades", Canvas), ("visor", Canvas));
        try
        {
            using var vm = Editor(eyes, recipe, book, kitchen);
            var view = new Views.IngredientEditorView { DataContext = vm };
            var window = new Window { Content = view, Width = 1180, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            double[] Rows() => view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("refrow"))
                .Select(b => b.Bounds.Height).ToArray();

            var resting = Rows();
            Assert.Equal(5, resting.Length);   // 3 siblings + 2 Kitchen files
            double panelWidth = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("refrow")).Bounds.Width;

            vm.ToggleReferenceCommand.Execute(Sibling(vm, "accessory"));   // above: shows OVER
            vm.ToggleReferenceCommand.Execute(Sibling(vm, "body"));        // below: shows UNDER
            await vm.ToggleReferenceCommand.ExecuteAsync(vm.KitchenLayers[0]);   // shows the stepper
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(resting, Rows());
            Assert.Equal(panelWidth, view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("refrow")).Bounds.Width);

            // Both sections are internally uniform too, so a row cannot silently be the odd one out.
            Assert.Equal(resting[0], resting[1]);
            Assert.Equal(resting[3], resting[4]);
            window.Close();
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_stepper_that_has_run_out_of_stack_is_visibly_dimmed()
    {
        // The house trap: a LOCAL value beats every Style setter, so binding Opacity onto the stepper
        // buttons themselves would silently defeat `Button:disabled { Opacity 0.38 }` and an
        // end-of-stack chevron would look live. The switch is on the group instead - and this is what
        // says so, since nothing about the markup does.
        var (book, recipe, eyes) = FourLayerStack();
        var (kitchen, dir) = TempKitchen(("shades", Canvas));
        try
        {
            using var vm = Editor(eyes, recipe, book, kitchen);
            var view = new Views.IngredientEditorView { DataContext = vm };
            var window = new Window { Content = view, Width = 1180, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            await vm.ToggleReferenceCommand.ExecuteAsync(vm.KitchenLayers[0]);   // opens at gap 4, the top
            Dispatcher.UIThread.RunJobs();

            var steppers = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("sbtn")).ToList();
            Assert.Equal(2, steppers.Count);
            var down = steppers[0];
            var up = steppers[1];

            Assert.False(up.IsEffectivelyEnabled);      // already on top
            Assert.True(down.IsEffectivelyEnabled);
            Assert.True(up.Opacity < 1.0, $"a disabled stepper must dim; opacity was {up.Opacity}");
            Assert.Equal(1.0, down.Opacity);
            window.Close();
        }
        finally { book.Dispose(); Directory.Delete(dir, recursive: true); }
    }
}
