using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Per-variant depth maps must not leak from a deleted variant onto a later one that reuses its id
/// (<c>NextVariantId</c> reuses the smallest free <c>variant-N</c>), and must follow a duplicated
/// variant's cloned art.
/// </summary>
public class IngredientEditorDepthTests
{
    // Two variants named "variant-1"/"variant-2" (so a later AddVariant's NextVariantId reuses
    // "variant-2" once it is deleted), the second carrying a depth map over opaque art.
    private static (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) OnDiskTwoVariants()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var depth = DepthMap.Flat(8, 8).With(1, 1, front: 11, back: 22);
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, coloriz,
                new[] { new Variant("variant-1", "One", 1), new Variant("variant-2", "Two", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["variant-1"] = new(8, 8, new Rgba32(64, 64, 64, 255)),
                ["variant-2"] = new(8, 8, new Rgba32(128, 128, 128, 255)),
            },
            Depths = new Dictionary<string, DepthMap> { ["variant-2"] = depth },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, manifest, new[] { recipe });
        var book = CookBookArchive.Read(path);
        var session = new CookBookSession();
        session.Open(book, path);
        var r = book.Recipes[0];
        return (path, session, r, r.Ingredients[0]);
    }

    private sealed class FakeConfirmingDialogs : IDialogService
    {
        public ViewModelBase? Active => null;
        public event System.Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
            => Task.FromResult((TResult?)(object?)true);
        public void Close(object? result) { }
    }

    [AvaloniaFact]
    public async Task Deleting_a_variant_and_reusing_its_id_does_not_resurrect_its_depth_map()
    {
        var (path, session, recipe, ing) = OnDiskTwoVariants();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeConfirmingDialogs(), new FilePickerService());

            // Select and delete "variant-2", the one carrying the depth map.
            vm.SelectedVariant = vm.Variants.Single(v => v.Id == "variant-2");
            await vm.DeleteVariantCommand.ExecuteAsync(null);

            // NextVariantId reuses the smallest free "variant-N" id: "variant-1" is taken, so the
            // new blank variant is "variant-2" again.
            vm.AddVariantCommand.Execute(null);
            var reused = vm.Variants.Single(v => v.Id == "variant-2");
            vm.SelectedVariant = reused;

            // Paint it fully opaque — a transparent pixel would be zeroed on write regardless,
            // masking the bug: the written map must be the deleted variant's, not absent.
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushValue = 50;
            vm.ApplyToolStroke(new[] { (0, 0) });

            await vm.SaveCommand.ExecuteAsync(null);
            vm.Dispose();

            using var reread = CookBookArchive.Read(path);
            var rip = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.False(rip.Depths.ContainsKey("variant-2"));
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Duplicating_a_variant_copies_its_depth_map()
    {
        var (path, session, recipe, ing) = OnDiskTwoVariants();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeConfirmingDialogs(), new FilePickerService());

            vm.SelectedVariant = vm.Variants.Single(v => v.Id == "variant-2");
            vm.DuplicateVariantCommand.Execute(null);
            var copy = vm.Variants.Single(v => v.Id != "variant-1" && v.Id != "variant-2");

            await vm.SaveCommand.ExecuteAsync(null);
            vm.Dispose();

            using var reread = CookBookArchive.Read(path);
            var rip = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.True(rip.Depths.ContainsKey(copy.Id));
            Assert.Equal(11, rip.Depths[copy.Id].Front(1, 1));
            Assert.Equal(22, rip.Depths[copy.Id].Back(1, 1));
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
