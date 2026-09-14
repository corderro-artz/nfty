using System;
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
/// Importing a picture as a layer, from the picker to the bytes in the <c>.cbk</c>.
/// </summary>
/// <remarks>
/// <para>The only route in used to be: create an empty ingredient, open the editor, import the file
/// into its blank variant, save. Every step of that is the app asking the author to work around the
/// fact that they already had the art.</para>
///
/// <para>The choice the form exists to make is the KIND, and it is not reversible by hand. Custom
/// keeps the picture untouched; Dynamic and Static keep its LIGHTNESS and take their color at
/// generation time, which throws the picture's own colors away on purpose. So the tests below check
/// the pixels that reach the archive, not just the manifest — a kind that reached the manifest while
/// the wrong raster reached the PNG would pass every other kind of assertion.</para>
/// </remarks>
public class ImportImageAsIngredientTests
{
    private sealed class FakePicker(string? answer) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) =>
            Task.FromResult(answer);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    /// <summary>Shows the form and answers it: runs <paramref name="fill"/> on the real view model
    /// and then presses Create, which is what the dialog layer returns.</summary>
    private sealed class FormDialogs(Action<ImportImageViewModel>? fill = null) : IDialogService
    {
        public ViewModelBase? Active { get; private set; }
        public int Errors { get; private set; }
        public string LastErrorBody { get; private set; } = "";
        public event Action? Changed { add { } remove { } }

        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            Active = dialog;
            if (dialog is ImportImageViewModel form)
            {
                fill?.Invoke(form);
                return Task.FromResult((TResult?)(object?)form);
            }
            if (dialog is ErrorDialogViewModel err)
            {
                Errors++;
                LastErrorBody = err.Message;
            }
            return Task.FromResult<TResult?>(default);
        }

        public void Close(object? result) { }
    }

    private static (string Path, CookBookSession Session) OnDisk(int canvas = 8)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                new[] { new Variant("day", "Day", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["day"] = new(canvas, canvas) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        using (var seed = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(canvas, canvas),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        })
            CookBookArchive.Write(path, seed.Manifest, seed.Recipes);

        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (path, session);
    }

    /// <summary>A picture beside the book: a solid, saturated color, so "did the color survive?" has
    /// an unambiguous answer at every pixel.</summary>
    private static string Picture(string bookPath, string leaf, int size = 8,
        byte r = 200, byte g = 40, byte b = 40)
    {
        string p = Path.Combine(Path.GetDirectoryName(bookPath)!, leaf);
        using var img = new Image<Rgba32>(size, size, new Rgba32(r, g, b, 255));
        img.Save(p);
        return p;
    }

    private static ExplorerViewModel Explorer(CookBookSession session, IFilePickerService picker,
        IDialogService dialogs, IStatusService status)
    {
        var nav = new FakeNav();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, picker,
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
    }

    private static void Cleanup(CookBookSession session, string path)
    {
        session.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { }
    }

    // ---- what reaches the archive ----------------------------------------------------------------

    /// <summary>A Custom import keeps the art exactly as it is, which is what "import my drawing"
    /// means.</summary>
    [AvaloniaFact]
    public async Task A_custom_import_keeps_every_channel()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "aura.png");
            var dialogs = new FormDialogs(f => f.Kind = LayerKind.Custom);
            using var explorer = Explorer(session, new FakePicker(picture), dialogs, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            await explorer.ImportCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            var ing = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(LayerKind.Custom, ing.Manifest.Kind);
            Assert.Null(ing.Manifest.Colorization);           // a custom layer carries none

            var px = ing.VariantImages.Values.Single()[0, 0];
            Assert.Equal(200, px.R);
            Assert.Equal(40, px.G);
            Assert.Equal(40, px.B);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A Dynamic import keeps the picture's LIGHTNESS and nothing else — the layer's color comes
    /// from the roll.
    /// </summary>
    /// <remarks>
    /// The stored PNG must be gray, and Validator says so independently: it refuses a non-grayscale
    /// variant image on a value-map layer. Asserting the manifest alone would pass on a layer
    /// declaring itself dynamic over full-color art, which is a book that cannot be cooked.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_dynamic_import_stores_the_lightness_and_a_range()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "aura.png");
            var dialogs = new FormDialogs(f =>
            {
                f.Kind = LayerKind.Dynamic;
                f.HueMin = 170;
                f.HueMax = 200;
            });
            using var explorer = Explorer(session, new FakePicker(picture), dialogs, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            await explorer.ImportCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            var ing = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(LayerKind.Dynamic, ing.Manifest.Kind);
            var range = Assert.Single(ing.Manifest.Colorization!.Entries).Range!;
            Assert.Equal(170, range.HueMin);
            Assert.Equal(200, range.HueMax);

            var px = ing.VariantImages.Values.Single()[0, 0];
            Assert.Equal(px.R, px.G);
            Assert.Equal(px.G, px.B);
            Assert.True(px.R is > 0 and < 255, $"lightness collapsed to {px.R}");

            // And the whole book still validates, which is the check that catches a gray-looking
            // import that is not actually gray.
            Assert.Empty(Validator.Validate(reread));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>Static gets its one fixed color, and the same value-map treatment.</summary>
    [AvaloniaFact]
    public async Task A_static_import_stores_one_fixed_color()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "aura.png");
            var dialogs = new FormDialogs(f =>
            {
                f.Kind = LayerKind.Static;
                f.FixedColor = "hex:22aa44";
            });
            using var explorer = Explorer(session, new FakePicker(picture), dialogs, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            await explorer.ImportCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            var ing = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(LayerKind.Static, ing.Manifest.Kind);
            Assert.Equal("hex:22aa44", Assert.Single(ing.Manifest.Colorization!.Entries).Fixed);
            Assert.Empty(Validator.Validate(reread));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>A JPEG is a picture too. It carries no alpha, which is a property of the file rather
    /// than a problem with the import.</summary>
    [AvaloniaFact]
    public async Task A_jpeg_imports_as_readily_as_a_png()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "aura.jpg");
            var dialogs = new FormDialogs(f => f.Kind = LayerKind.Custom);
            using var explorer = Explorer(session, new FakePicker(picture), dialogs, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            await explorer.ImportCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Contains(reread.Recipes[0].Ingredients, i => i.Manifest.Id == "aura");
        }
        finally { Cleanup(session, path); }
    }

    // ---- what it refuses -------------------------------------------------------------------------

    /// <summary>
    /// A wrong-sized picture is refused BEFORE the form opens.
    /// </summary>
    /// <remarks>
    /// Every variant in a book is the canvas size and nfty resamples nothing — resizing a value-map
    /// would soften the art the DNA was built on. Refused up front rather than on Create, because a
    /// form that lets you fill it in and then says no was never going to work.
    /// </remarks>
    [AvaloniaFact]
    public async Task A_wrong_sized_picture_is_refused_before_the_form_opens()
    {
        var (path, session) = OnDisk(canvas: 8);
        try
        {
            string picture = Picture(path, "big.png", size: 16);
            var dialogs = new FormDialogs();
            using var explorer = Explorer(session, new FakePicker(picture), dialogs, new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            await explorer.ImportCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.Errors);
            Assert.Contains("16×16", dialogs.LastErrorBody);
            Assert.Contains("8×8", dialogs.LastErrorBody);

            using var reread = CookBookArchive.Read(path);
            Assert.Single(reread.Recipes[0].Ingredients);      // nothing was added
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>A layer belongs to one recipe, so importing a picture with only the book selected
    /// has nothing to infer — the same rule a loose <c>.igt</c> import already follows.</summary>
    [AvaloniaFact]
    public async Task Importing_a_picture_with_no_recipe_selected_says_what_to_do()
    {
        var (path, session) = OnDisk();
        var status = new StatusService();
        try
        {
            string picture = Picture(path, "aura.png");
            using var explorer = Explorer(session, new FakePicker(picture), new FormDialogs(), status);
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root);          // the BOOK

            await explorer.ImportCommand.ExecuteAsync(null);

            Assert.Contains("recipe", status.Last, StringComparison.OrdinalIgnoreCase);
            using var reread = CookBookArchive.Read(path);
            Assert.Single(reread.Recipes[0].Ingredients);
        }
        finally { Cleanup(session, path); }
    }

    // ---- the form itself -------------------------------------------------------------------------

    /// <summary>
    /// The preview shows what the CHOSEN KIND will store, so the grayscale a value-map import
    /// produces is on screen before it is agreed to rather than after.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_follows_the_kind()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "aura.png");
            using var form = new ImportImageViewModel(new FakeDialogs(), picture,
                new Dimensions(8, 8), new ImageBridge());
            Assert.Null(form.TryLoad());

            var asIs = form.Preview;
            Assert.NotNull(asIs);
            Assert.False(form.LosesColor);                    // Custom keeps it

            form.Kind = LayerKind.Dynamic;
            Assert.NotSame(asIs, form.Preview);
            Assert.True(form.LosesColor);                     // and this picture has color to lose

            form.Kind = LayerKind.Custom;
            Assert.False(form.LosesColor);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>A gray picture loses nothing, so it is not warned about. The warning has to mean
    /// something when it appears.</summary>
    [AvaloniaFact]
    public void A_gray_picture_is_not_warned_about()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "gray.png", r: 90, g: 90, b: 90);
            using var form = new ImportImageViewModel(new FakeDialogs(), picture,
                new Dimensions(8, 8), new ImageBridge());
            Assert.Null(form.TryLoad());

            form.Kind = LayerKind.Dynamic;
            Assert.False(form.LosesColor);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>The name is the file's, and the id follows it — which is what people expect and
    /// almost always what they wanted.</summary>
    [AvaloniaFact]
    public void The_layer_is_named_after_the_file()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "Chest Lid.png");
            using var form = new ImportImageViewModel(new FakeDialogs(), picture,
                new Dimensions(8, 8), new ImageBridge());
            Assert.Null(form.TryLoad());

            Assert.Equal("Chest Lid", form.Name);
            Assert.Equal("chest-lid", form.DerivedId);
            Assert.Equal("", form.Problem);
            Assert.True(form.CreateCommand.CanExecute(null));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A clash with a layer already in the recipe is reported while it can still be retyped.
    /// </summary>
    [AvaloniaFact]
    public void A_name_already_in_the_recipe_is_refused_in_the_form()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "bg.png");
            using var form = new ImportImageViewModel(new FakeDialogs(), picture,
                new Dimensions(8, 8), new ImageBridge(), new[] { "bg" });
            Assert.Null(form.TryLoad());

            Assert.False(form.CreateCommand.CanExecute(null));
            Assert.Contains("already has a layer", form.Problem);

            form.Name = "Aura";
            Assert.Equal("", form.Problem);
            Assert.True(form.CreateCommand.CanExecute(null));
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>An empty name is not an id. The identifier chip reads an em dash rather than an
    /// empty box.</summary>
    [AvaloniaFact]
    public void A_blank_name_cannot_be_imported()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "aura.png");
            using var form = new ImportImageViewModel(new FakeDialogs(), picture,
                new Dimensions(8, 8), new ImageBridge());
            Assert.Null(form.TryLoad());

            form.Name = "   ";
            Assert.False(form.CreateCommand.CanExecute(null));
            Assert.Equal("—", form.DerivedIdText);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>The range runs ascending here too — the rule the editor's rail and the New
    /// Ingredient wizard both keep, because Validator refuses an inverted one.</summary>
    [AvaloniaFact]
    public void The_forms_range_cannot_be_inverted()
    {
        var (path, session) = OnDisk();
        try
        {
            string picture = Picture(path, "aura.png");
            using var form = new ImportImageViewModel(new FakeDialogs(), picture,
                new Dimensions(8, 8), new ImageBridge());
            Assert.Null(form.TryLoad());

            form.HueMax = 90;
            form.HueMin = 200;
            Assert.Equal(90, form.HueMin);

            form.SatMin = 60;
            form.SatMax = 10;
            Assert.Equal(60, form.SatMax);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>An unreadable file is a message, not a dialog that never opens.</summary>
    [AvaloniaFact]
    public void A_file_that_is_not_an_image_is_refused_with_a_reason()
    {
        var (path, session) = OnDisk();
        try
        {
            string bogus = Path.Combine(Path.GetDirectoryName(path)!, "notreally.png");
            File.WriteAllText(bogus, "this is not a picture");
            using var form = new ImportImageViewModel(new FakeDialogs(), bogus,
                new Dimensions(8, 8), new ImageBridge());

            var refusal = form.TryLoad();
            Assert.NotNull(refusal);
            Assert.Contains("notreally.png", refusal!);
        }
        finally { Cleanup(session, path); }
    }

    // ---- the shared conversion --------------------------------------------------------------------

    /// <summary>
    /// The editor's variant import and this one reduce a picture the same way, because they call the
    /// same function.
    /// </summary>
    /// <remarks>
    /// Pure green is the case that makes it matter: <c>ValueMap.FromImage</c> reads the RED channel,
    /// so green would import as BLACK without the desaturation, though it reads as mid-bright to the
    /// eye. Two copies of that rule is how the same file comes to import differently depending on
    /// which button was pressed.
    /// </remarks>
    [Fact]
    public void Green_imports_as_mid_bright_rather_than_black()
    {
        using var green = new Image<Rgba32>(4, 4, new Rgba32(0, 255, 0, 255));
        var map = ImageImport.ToValueMap(green);
        Assert.True(map.GetValue(0, 0) is > 100 and < 200, $"green became {map.GetValue(0, 0)}");

        // A picture that is already gray round-trips exactly - the lossless path the red-channel
        // read exists for.
        using var gray = new Image<Rgba32>(4, 4, new Rgba32(90, 90, 90, 255));
        Assert.Equal(90, ImageImport.ToValueMap(gray).GetValue(0, 0));
        Assert.False(ImageImport.HasColor(gray));
        Assert.True(ImageImport.HasColor(green));
    }

    /// <summary>A colored pixel under zero alpha cannot be seen, so it is not color to lose.</summary>
    [Fact]
    public void A_transparent_pixel_is_not_color()
    {
        using var img = new Image<Rgba32>(2, 2, new Rgba32(255, 0, 0, 0));
        Assert.False(ImageImport.HasColor(img));
    }
}
