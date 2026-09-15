using System.IO;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// THE EDITOR OPENS IN THE VIEW THE READER LAST LEFT.
/// </summary>
/// <remarks>
/// <para>The pixel grid is already documented as view state that must never mark the editor dirty.
/// This is the other half of that sentence: state that is not part of the work should not have to be
/// re-chosen every time the work is opened. An author who paints 16px sprites on an 8px lattice sets
/// that on every single layer, and until now the app forgot it every single time.</para>
///
/// <para>What is NOT remembered is asserted too, and deliberately: zoom and pan belong to the image
/// rather than to the reader, and restoring them would open an unrelated layer magnified and panned
/// into a corner.</para>
/// </remarks>
public class EditorViewStateTests
{
    private static LoadedIngredient Ing(int canvas) => new()
    {
        Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
            new[] { new Variant("glow", "Glow", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(canvas, canvas) },
    };

    private static (LoadedCookBook Book, LoadedRecipe Recipe, LoadedIngredient Ing) Fixture(int canvas)
    {
        var ing = Ing(canvas);
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(canvas, canvas),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, ing);
    }

    private static IngredientEditorViewModel Editor(IEditorViewState state, int canvas = 32)
    {
        var (book, recipe, ing) = Fixture(canvas);
        return new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService(), viewState: state);
    }

    [AvaloniaFact]
    public void The_editor_opens_in_the_view_that_was_left()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var service = new EditorViewStateService(StateStore.At(dir));

            using (var first = Editor(service))
            {
                first.GridSize = 8;
                first.ShowPixelGrid = false;
                first.EnlargePreviewCommand.Execute(null);
                first.ShowReferencesTabCommand.Execute(null);
            }

            // A SECOND SERVICE over the same folder, not the same object: anything short of that is
            // a test of a field rather than of the file.
            using var second = Editor(new EditorViewStateService(StateStore.At(dir)));

            Assert.Equal(8, second.GridSize);
            Assert.False(second.ShowPixelGrid);
            Assert.True(second.PreviewEnlarged);
            Assert.True(second.IsReferencesTab);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// A STEP THE CANVAS CANNOT TAKE MUST NOT OVERWRITE THE PREFERENCE.
    /// </summary>
    /// <remarks>
    /// <c>GridSize</c> clamps to the canvas, so a remembered 16 arrives at 8 on an 8px layer. Saving
    /// that would throw the author's real preference away because they happened to glance at a small
    /// sprite — which is exactly the kind of quiet loss a "remember it for me" feature is judged on.
    /// Deleting the loading guard fails this and nothing else.
    /// </remarks>
    [AvaloniaFact]
    public void A_step_a_small_canvas_cannot_take_does_not_overwrite_the_preference()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var service = new EditorViewStateService(StateStore.At(dir));
            using (var big = Editor(service, canvas: 32)) big.GridSize = 16;
            Assert.Equal(16, service.Current.GridStep);

            using (var small = Editor(service, canvas: 8))
                Assert.Equal(8, small.GridSize);      // clamped for THIS canvas...

            Assert.Equal(16, service.Current.GridStep);   // ...and the preference is untouched

            using var big2 = Editor(service, canvas: 32);
            Assert.Equal(16, big2.GridSize);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Zoom and pan belong to the IMAGE. Opening an unrelated layer magnified and panned
    /// into a corner is not a remembered preference, it is a lost canvas.</summary>
    [AvaloniaFact]
    public void Zoom_and_pan_are_not_remembered()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var service = new EditorViewStateService(StateStore.At(dir));
            using (var first = Editor(service))
            {
                first.Zoom = 8;
                first.PanBy(40, 40);
                Assert.True(first.IsZoomed);
            }

            using var second = Editor(new EditorViewStateService(StateStore.At(dir)));
            Assert.False(second.IsZoomed);
            Assert.Equal(0, second.PanX);
            Assert.Equal(0, second.PanY);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Convenience state: a file somebody has edited by hand must never be the reason the
    /// editor will not open.</summary>
    [AvaloniaTheory]
    [InlineData("{ not json at all")]
    [InlineData("{ \"gridStep\": 0 }")]
    [InlineData("")]
    public void An_unusable_file_opens_the_defaults(string contents)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var store = StateStore.At(dir);
            store.Write(EditorViewStateService.FileName, contents);

            using var vm = Editor(new EditorViewStateService(store));

            Assert.True(vm.ShowPixelGrid);
            Assert.Equal(1, vm.GridSize);
            Assert.True(vm.IsColorizeTab);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Nothing here may make the editor dirty: the grid and the preview size are how the
    /// layer is LOOKED at, and Save must stay dim for a layer nobody has changed.</summary>
    [AvaloniaFact]
    public void Restoring_a_view_does_not_make_the_editor_dirty()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var service = new EditorViewStateService(StateStore.At(dir));
            using (var first = Editor(service))
            {
                first.GridSize = 4;
                first.ShowPixelGrid = false;
                first.EnlargePreviewCommand.Execute(null);
                Assert.False(first.IsDirty, "changing how the canvas LOOKS is not an edit");
            }

            using var second = Editor(new EditorViewStateService(StateStore.At(dir)));
            Assert.Equal(4, second.GridSize);
            Assert.False(second.IsDirty);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
