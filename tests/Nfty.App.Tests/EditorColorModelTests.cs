using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The editor keeps a layer's color model. The rail edits hue and saturation, never the model, so
/// saving used to rewrite every layer as HSV and the preview colorized in HSV: an HSL layer changed
/// model silently, and its preview showed colors the cook would not produce.
/// </summary>
public class EditorColorModelTests
{
    private sealed class Answering(bool answer) : IDialogService
    {
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Active = d;
            return Task.FromResult((TResult?)(object?)answer);
        }
        public void Close(object? result) { }
    }

    private static Image<Rgba32> MidGray()
    {
        var img = new Image<Rgba32>(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++) img[x, y] = new Rgba32(128, 128, 128, 255);
        return img;
    }

    /// <summary>A one-layer HSL book written to disk, so a save can be read back from the file.</summary>
    private static (string Path, CookBookSession Session, LoadedRecipe Recipe, LoadedIngredient Ing)
        OnDisk(LayerKind kind)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var colorization = kind == LayerKind.Static
            ? new Colorization(ColorModel.Hsl, 12, 4, new[] { new ColorEntry(1, null, "hsl:200,60,50") })
            : new Colorization(ColorModel.Hsl, 12, 4,
                new[] { new ColorEntry(1, new ColorRange(170, 200, 55, 95), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", kind, colorization,
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = MidGray() },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, manifest, new[] { recipe });

        var book = CookBookArchive.Read(path);
        var session = new CookBookSession();
        session.Open(book, path);
        var r = book.Recipes[0];
        return (path, session, r, r.Ingredients[0]);
    }

    private static IngredientEditorViewModel Editor(
        LoadedRecipe recipe, LoadedIngredient ing, CookBookSession session) =>
        new(ing, recipe, session.Current!, new ImageBridge(), new FakeNav(), session,
            new Answering(true), new FilePickerService());

    private static Colorization Reread(string path)
    {
        using var book = CookBookArchive.Read(path);
        return book.Recipes[0].Ingredients[0].Manifest.Colorization!;
    }

    private static void Cleanup(string path, CookBookSession session)
    {
        session.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { }
    }

    private static byte[] Pixels(Bitmap bmp)
    {
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        var buffer = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (nint)p, buffer.Length, w * 4);
        }
        return buffer;
    }

    [AvaloniaFact]
    public async Task Saving_an_hsl_dynamic_layer_keeps_it_hsl()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Dynamic);
        using var vm = Editor(recipe, ing, session);
        try
        {
            vm.HueMax = 190;
            await vm.SaveCommand.ExecuteAsync(null);

            var saved = Reread(path);
            Assert.Equal(ColorModel.Hsl, saved.Model);
            var range = Assert.Single(saved.Entries).Range!;
            Assert.Equal(190, range.HueMax);
            Assert.Equal(170, range.HueMin);
            Assert.Equal(55, range.SatMin);
            Assert.Equal(95, range.SatMax);
        }
        finally { Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task Saving_an_hsl_static_layer_keeps_it_hsl()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Static);
        using var vm = Editor(recipe, ing, session);
        try
        {
            vm.FixedColor = "hsl:30,70,40";
            await vm.SaveCommand.ExecuteAsync(null);

            var saved = Reread(path);
            Assert.Equal(ColorModel.Hsl, saved.Model);
            Assert.Equal("hsl:30,70,40", Assert.Single(saved.Entries).Fixed);
        }
        finally { Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task Switching_kind_keeps_the_color_model()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Dynamic);
        using var vm = Editor(recipe, ing, session);
        try
        {
            await vm.SetModeStaticCommand.ExecuteAsync(null);
            await vm.SaveCommand.ExecuteAsync(null);

            Assert.Equal(ColorModel.Hsl, Reread(path).Model);
        }
        finally { Cleanup(path, session); }
    }

    [AvaloniaFact]
    public void The_editor_preview_colorizes_in_the_layers_own_model()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Static);
        using var vm = Editor(recipe, ing, session);
        try
        {
            using var expected = VariantPreview.Render(ing, "glow", "hsl:200,60,50");
            using var viaHsv = VariantPreview.Render(ing, "glow", "hsl:200,60,50", ColorModel.Hsv);
            var bridge = new ImageBridge();
            var want = Pixels(bridge.ToBitmap(expected));

            // The fixture only proves anything if the two models really disagree on this gray.
            Assert.NotEqual(want, Pixels(bridge.ToBitmap(viaHsv)));

            Assert.Equal(want, Pixels(vm.Preview));
        }
        finally { Cleanup(path, session); }
    }
}
