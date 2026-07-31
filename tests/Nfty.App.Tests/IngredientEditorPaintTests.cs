using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorPaintTests
{
    // A small dynamic ingredient (value-map layer) with one variant on an 8x8 canvas.
    private static (LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book) Fixture()
    {
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, coloriz,
                new[] { new Variant("glow", "Glow", 1), new Variant("spark", "Spark", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8), ["spark"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, System.Array.Empty<IncompatibilityRule>()),
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

    private static IngredientEditorViewModel Editor()
    {
        var (ing, recipe, book) = Fixture();
        return new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(), new FakeNotYetWired(),
            new CookBookSession(), new FakeDialogs());
    }

    [AvaloniaFact]
    public void Canvas_and_preview_build_over_a_draft()
    {
        using var vm = Editor();
        Assert.NotNull(vm.Canvas);
        Assert.NotNull(vm.Preview);
        Assert.Equal(0, vm.ValueAt(2, 2));   // seeded from a blank 8x8 image → value 0
    }

    [AvaloniaFact]
    public void Brush_paints_and_undo_reverts()
    {
        using var vm = Editor();
        vm.ActiveTool = EditorTool.Brush; vm.BrushValue = 200; vm.BrushSize = 1;
        Assert.Equal(0, vm.ValueAt(4, 4));
        vm.ApplyToolStroke(new[] { (4, 4) });
        Assert.True(vm.ValueAt(4, 4) > 0);   // painted
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);
        Assert.Equal(0, vm.ValueAt(4, 4));   // reverted
        Assert.True(vm.RedoCommand.CanExecute(null));
        vm.RedoCommand.Execute(null);
        Assert.True(vm.ValueAt(4, 4) > 0);   // re-applied
    }

    [AvaloniaFact]
    public void Fill_changes_the_region()
    {
        using var vm = Editor();
        vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 150;
        vm.ApplyToolStroke(new[] { (0, 0) });
        Assert.Equal(150, vm.ValueAt(7, 7));   // flood filled the blank canvas
    }

    [AvaloniaFact]
    public void No_op_edit_leaves_history_untouched()
    {
        using var vm = Editor();
        vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 150;
        vm.ApplyToolStroke(new[] { (0, 0) });               // fills the blank canvas to 150
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.ApplyToolStroke(new[] { (0, 0) });               // fill 150 again → no-op, must not be recorded
        vm.UndoCommand.Execute(null);                       // a single undo restores blank...
        Assert.Equal(0, vm.ValueAt(7, 7));                  // ...so only the first fill was ever on the stack
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void History_is_per_variant()
    {
        using var vm = Editor();
        vm.ActiveTool = EditorTool.Brush; vm.BrushValue = 200; vm.BrushSize = 1;
        vm.ApplyToolStroke(new[] { (4, 4) });               // paint variant "glow"
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.SelectVariantCommand.Execute(vm.Variants[1]);    // switch to "spark"
        Assert.False(vm.UndoCommand.CanExecute(null));      // spark has no history
    }
}
