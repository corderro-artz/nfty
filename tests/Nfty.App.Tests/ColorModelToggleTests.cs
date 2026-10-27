using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The HSV / HSL tray: the editor and both wizards let the author pick the color model a layer's hue
/// and saturation are read in. Changing it loses nothing, so it asks nothing; it is an edit, so it
/// dirties the editor and re-renders the preview.
/// </summary>
public class ColorModelToggleTests
{
    private sealed class Answering : IDialogService
    {
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Active = d;
            return Task.FromResult((TResult?)(object?)true);
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

    private static (string Path, CookBookSession Session, LoadedRecipe Recipe, LoadedIngredient Ing)
        OnDisk(LayerKind kind)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var colorization = kind == LayerKind.Custom ? null
            : new Colorization(ColorModel.Hsv, 12, 4,
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
            new Answering(), new FilePickerService());

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
    public async Task Choosing_hsl_in_the_editor_saves_an_hsl_layer()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Dynamic);
        using var vm = Editor(recipe, ing, session);
        try
        {
            vm.SetModelHslCommand.Execute(null);
            await vm.SaveCommand.ExecuteAsync(null);

            using var book = CookBookArchive.Read(path);
            Assert.Equal(ColorModel.Hsl, book.Recipes[0].Ingredients[0].Manifest.Colorization!.Model);
        }
        finally { Cleanup(path, session); }
    }

    [AvaloniaFact]
    public void Changing_the_model_marks_the_editor_dirty_and_rerenders_the_preview()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Dynamic);
        using var vm = Editor(recipe, ing, session);
        try
        {
            Assert.False(vm.IsDirty);
            var before = Pixels(vm.Preview);

            vm.SetModelHslCommand.Execute(null);

            Assert.True(vm.IsDirty);
            Assert.NotEqual(before, Pixels(vm.Preview));
            Assert.True(vm.IsModelHsl);
            Assert.False(vm.IsModelHsv);
        }
        finally { Cleanup(path, session); }
    }

    [AvaloniaFact]
    public void The_model_tray_is_laid_out_but_disabled_on_a_custom_layer()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Custom);
        using var vm = Editor(recipe, ing, session);
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1366, Height = 768 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tray = view.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("seg") && b.GetVisualDescendants().OfType<Button>()
                    .Any(x => x.Content as string == "HSV"));
            Assert.True(tray.Bounds.Height > 0 && tray.Bounds.Width > 0, "the tray must be laid out");
            Assert.True(tray.IsVisible);
            Assert.False(tray.IsHitTestVisible);
            Assert.False(vm.IsModelHsv);   // nothing is lit: no model applies to Custom art
            Assert.False(vm.IsModelHsl);
            Assert.Equal(ColorModel.Hsv, vm.ColorModel);   // a Custom layer turned colorized starts at HSV
        }
        finally { window.Close(); Cleanup(path, session); }

        var (p2, s2, r2, i2) = OnDisk(LayerKind.Dynamic);
        using var dyn = Editor(r2, i2, s2);
        try { Assert.True(dyn.CanChooseModel); }
        finally { Cleanup(p2, s2); }
    }

    [AvaloniaFact]
    public void The_new_ingredient_wizard_creates_the_chosen_model()
    {
        var vm = new NewIngredientViewModel(new FakeDialogs()) { Name = "Aura", Kind = LayerKind.Dynamic };
        Assert.Equal(ColorModel.Hsv, vm.BuildColorization()!.Model);

        vm.SetModelHslCommand.Execute(null);
        Assert.Equal(ColorModel.Hsl, vm.BuildColorization()!.Model);

        vm.Kind = LayerKind.Static;
        Assert.Equal(ColorModel.Hsl, vm.BuildColorization()!.Model);

        vm.Kind = LayerKind.Custom;
        Assert.False(vm.CanChooseModel);
        Assert.Null(vm.BuildColorization());
    }

    [AvaloniaFact]
    public void The_import_wizard_creates_the_chosen_model()
    {
        using var vm = VisualCapture.ImportImageForm(new FakeDialogs());
        vm.Kind = LayerKind.Dynamic;
        Assert.Equal(ColorModel.Hsv, vm.BuildColorization()!.Model);

        vm.SetModelHslCommand.Execute(null);
        Assert.Equal(ColorModel.Hsl, vm.BuildColorization()!.Model);

        vm.Kind = LayerKind.Static;
        Assert.Equal(ColorModel.Hsl, vm.BuildColorization()!.Model);

        vm.Kind = LayerKind.Custom;
        Assert.False(vm.CanChooseModel);
    }
}
