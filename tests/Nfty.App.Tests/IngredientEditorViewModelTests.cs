using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorViewModelTests
{
    internal static (LoadedIngredient, LoadedRecipe, LoadedCookBook) Real()
    {
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, coloriz,
                new[] { new Variant("glow", "Glow", 1), new Variant("spark", "Spark", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["glow"] = new(8, 8), ["spark"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (ing, recipe, book);
    }

    private static IngredientEditorViewModel Make(out FakeNotYetWired n, out FakeNav nav)
    {
        n = new FakeNotYetWired();
        nav = new FakeNav();
        var (ing, recipe, book) = Real();
        return new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), nav, n);
    }

    [AvaloniaFact]
    public void Editor_filmstrip_reflects_the_real_variants_with_thumbnails()
    {
        var (ing, recipe, book) = Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        Assert.Equal(new[] { "Glow", "Spark" }, vm.Variants.Select(v => v.Name));
        Assert.All(vm.Variants, v => Assert.NotNull(v.Thumbnail));
        Assert.NotNull(vm.SelectedVariant);
    }

    [AvaloniaFact]
    public void Select_tool_sets_the_active_tool()
    {
        using var vm = Make(out _, out _);
        vm.SelectToolCommand.Execute(EditorTool.Fill);
        Assert.Equal(EditorTool.Fill, vm.ActiveTool);
    }

    [AvaloniaFact]
    public void Mode_toggle_changes_the_layer_kind()
    {
        using var vm = Make(out _, out _);
        vm.Mode = LayerKind.Static;
        Assert.Equal(LayerKind.Static, vm.Mode);
    }

    [AvaloniaFact]
    public void Paint_and_save_report_not_yet_wired()
    {
        using var vm = Make(out var n, out _);
        vm.ApplyStrokeCommand.Execute(null); Assert.Equal("Paint", n.Last);
        vm.SaveCommand.Execute(null); Assert.Equal("Save ingredient", n.Last);
    }

    [AvaloniaFact]
    public void Select_variant_sets_the_selected_variant()
    {
        using var vm = Make(out _, out _);
        var second = vm.Variants[1];
        vm.SelectVariantCommand.Execute(second);
        Assert.Same(second, vm.SelectedVariant);
    }

    [AvaloniaFact]
    public void Undo_and_redo_report_not_yet_wired()
    {
        using var vm = Make(out var n, out _);
        vm.UndoCommand.Execute(null); Assert.Equal("Undo", n.Last);
        vm.RedoCommand.Execute(null); Assert.Equal("Redo", n.Last);
    }

    [AvaloniaFact]
    public void Enlarge_and_fill_pane_preview_report_not_yet_wired()
    {
        using var vm = Make(out var n, out _);
        vm.EnlargePreviewCommand.Execute(null); Assert.Equal("Enlarge preview", n.Last);
        vm.FillPanePreviewCommand.Execute(null); Assert.Equal("Fill pane", n.Last);
    }

    [AvaloniaFact]
    public void Mode_defaults_to_the_ingredient_kind_and_custom_falls_back_to_dynamic()
    {
        var (ing, recipe, book) = Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        Assert.Equal(LayerKind.Dynamic, vm.Mode);

        var customIng = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(8, 8) },
        };
        using var customVm = new IngredientEditorViewModel(customIng, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        Assert.Equal(LayerKind.Dynamic, customVm.Mode);
    }

    [AvaloniaFact]
    public void Canvas_and_preview_render_and_update_on_colour_change()
    {
        var (ing, recipe, book) = Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        Assert.NotNull(vm.Canvas);
        Assert.NotNull(vm.Preview);
        var before = vm.Preview;
        vm.HueMin = 120;                 // change colour state
        Assert.NotSame(before, vm.Preview);   // preview rebuilt (old disposed internally)
    }

    [AvaloniaFact]
    public void Reroll_preview_rebuilds_the_preview()
    {
        var (ing, recipe, book) = Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        var before = vm.Preview;
        vm.RerollPreviewCommand.Execute(null);
        Assert.NotSame(before, vm.Preview);
    }

    [AvaloniaFact]
    public void Custom_ingredient_canvas_is_the_raw_image_not_colorized()
    {
        var (_, recipe, book) = Real();
        var map = new Image<Rgba32>(8, 8);
        map[0, 0] = new Rgba32(10, 200, 40, 255);   // distinct R/G/B — colorize would collapse G/B
        var customIng = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = map },
        };
        using var vm = new IngredientEditorViewModel(customIng, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());

        var buffer = new byte[8 * 8 * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                vm.Canvas.CopyPixels(new PixelRect(0, 0, 8, 8), (nint)p, buffer.Length, 8 * 4);
        }
        Assert.Equal(10, buffer[0]); Assert.Equal(200, buffer[1]); Assert.Equal(40, buffer[2]); Assert.Equal(255, buffer[3]);
    }

    [AvaloniaFact]
    public void Zero_variant_ingredient_does_not_crash_the_editor()
    {
        var (_, recipe, book) = Real();
        var emptyIng = new LoadedIngredient
        {
            Manifest = new IngredientManifest("empty", "Empty", LayerKind.Dynamic, null,
                Array.Empty<Variant>()),
            VariantImages = new Dictionary<string, Image<Rgba32>>(),
        };
        using var vm = new IngredientEditorViewModel(emptyIng, recipe, book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired());
        Assert.Empty(vm.Variants);
        Assert.Null(vm.SelectedVariant);
    }
}
