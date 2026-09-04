using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorImportTests
{
    // Writes a solid-fill WxH PNG (gray for dynamic/static import, any RGB for custom) to a fresh
    // temp file and returns its path.
    private static string WritePng(int width, int height, byte r, byte g, byte b, byte a = 255)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "import.png");
        using var img = new Image<Rgba32>(width, height);
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < width; x++)
                    row[x] = new Rgba32(r, g, b, a);
            }
        });
        img.Save(path);
        return path;
    }

    // A picker stub whose OpenFileAsync returns a fixed (possibly null) path.
    private sealed class OpenPicker : IFilePickerService
    {
        private readonly string? _open;
        public OpenPicker(string? open) => _open = open;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_open);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    // A dialogs stub that records the title/message of the last error shown (mirrors
    // ExplorerAddLooseTests.LooseWizardDialogs's ErrorTitle-recording pattern).
    private sealed class RecordingDialogs : IDialogService
    {
        public string? ErrorTitle { get; private set; }
        public string? ErrorMessage { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is ErrorDialogViewModel e) { ErrorTitle = e.Title; ErrorMessage = e.Message; }
            return Task.FromResult(default(TResult));
        }
        public void Close(object? result) { }
    }

    [AvaloniaFact]
    public async Task Import_replaces_a_dynamic_variants_value_map_and_clears_its_history()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();   // dynamic 8x8 ingredient
        var pngPath = WritePng(8, 8, 180, 180, 180);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new OpenPicker(pngPath));
            // Paint first so there IS history to be cleared by the import.
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 50;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.UndoCommand.CanExecute(null));

            await vm.ImportImageCommand.ExecuteAsync(null);

            Assert.Equal(180, vm.ValueAt(4, 4));
            Assert.True(vm.IsDirty);
            Assert.False(vm.UndoCommand.CanExecute(null));   // history cleared by the import
            vm.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Import_rejects_a_size_mismatch()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();   // 8x8 canvas
        var pngPath = WritePng(4, 4, 100, 100, 100);                             // wrong size
        try
        {
            var dialogs = new RecordingDialogs();
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, dialogs, new OpenPicker(pngPath));

            await vm.ImportImageCommand.ExecuteAsync(null);

            Assert.Equal("Wrong size", dialogs.ErrorTitle);
            Assert.Equal(0, vm.ValueAt(4, 4));    // unchanged (blank fixture)
            Assert.False(vm.IsDirty);
            vm.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Import_of_an_unreadable_file_shows_an_error_and_changes_nothing()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        var dir = Directory.CreateTempSubdirectory().FullName;
        var badPath = Path.Combine(dir, "bad.png");
        File.WriteAllText(badPath, "not a png");
        try
        {
            var dialogs = new RecordingDialogs();
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, dialogs, new OpenPicker(badPath));

            await vm.ImportImageCommand.ExecuteAsync(null);

            Assert.Equal("Could not import", dialogs.ErrorTitle);
            Assert.Equal(0, vm.ValueAt(4, 4));
            Assert.False(vm.IsDirty);
            vm.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Canceled_import_changes_nothing()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new OpenPicker(null));

            await vm.ImportImageCommand.ExecuteAsync(null);

            Assert.Equal(0, vm.ValueAt(4, 4));
            Assert.False(vm.IsDirty);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // Reads the pixel at (x, y) off a rendered Bitmap of known size w×h.
    private static (byte r, byte g, byte b, byte a) ReadPixel(Avalonia.Media.Imaging.Bitmap bmp, int w, int h, int x, int y)
    {
        var buffer = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new PixelRect(0, 0, w, h), (nint)p, buffer.Length, w * 4);
        }
        int i = (y * w + x) * 4;
        return (buffer[i], buffer[i + 1], buffer[i + 2], buffer[i + 3]);
    }

    [AvaloniaFact]
    public async Task Import_into_a_custom_variant_keeps_full_color()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);   // a distinctly non-gray color
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new OpenPicker(pngPath));

            await vm.ImportImageCommand.ExecuteAsync(null);

            var (r, g, b, a) = ReadPixel(vm.Canvas, 8, 8, 4, 4);
            Assert.Equal(10, r); Assert.Equal(200, g); Assert.Equal(40, b); Assert.Equal(255, a);
            Assert.NotEqual(r, g);   // proves this is full color, not a grayscale value-map
            Assert.True(vm.IsDirty);
            vm.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true);
        }
    }

    /// <summary>Color mode replaced the old "custom is import-only" contract. A Custom layer now
    /// opens in color and paints; what it must NOT offer is grayscale, because its value-map never
    /// reaches an archive and those strokes would be invisible work.</summary>
    [AvaloniaFact]
    public void Custom_opens_in_color_and_paints_but_is_never_offered_grayscale()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new OpenPicker(null));

            Assert.True(vm.IsColorMode);
            Assert.False(vm.CanPaintGrayscale);

            // The refusal has to be real, not just a grayed button: driving the command directly
            // must leave the mode alone.
            vm.SetPaintGrayscaleCommand.Execute(null);
            Assert.True(vm.IsColorMode);

            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 120; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });

            Assert.True(vm.IsDirty);
            Assert.True(vm.UndoCommand.CanExecute(null));
            var painted = vm.ColorAt(4, 4);
            Assert.Equal(0, painted.R); Assert.Equal(255, painted.G); Assert.Equal(0, painted.B);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // The headline guarantee: a CUSTOM ingredient's imported image must survive Save in full color —
    // it must never round-trip through ValueMap (grayscale by construction). Re-reading the archive and
    // finding R != G != B at a known pixel proves no value-map round-trip happened.
    [AvaloniaFact]
    public async Task Custom_save_round_trips_full_color()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new OpenPicker(pngPath));
            await vm.ImportImageCommand.ExecuteAsync(null);
            Assert.True(vm.CanSave);

            await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.IsDirty);
            Assert.False(File.Exists(path + ".tmp"));

            using var reread = CookBookArchive.Read(path);
            var rip = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            var pixel = rip.VariantImages["glow"][4, 4];
            Assert.Equal(10, pixel.R); Assert.Equal(200, pixel.G); Assert.Equal(40, pixel.B); Assert.Equal(255, pixel.A);
            Assert.NotEqual(pixel.R, pixel.G);   // R != G != B — proves this did NOT go through ValueMap
            vm.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true);
        }
    }

    // ---- Review regressions (C2 whole-branch review) ----

    /// <summary>The per-variant image gate is gone: a Custom variant is born with a blank color
    /// raster, so it is paintable at once rather than blocked until an import. Assert the COMMAND's
    /// notification, not just the property — the property is computed live and passes even when the
    /// bound button is stale.</summary>
    [AvaloniaFact]
    public void A_custom_variant_added_this_session_is_paintable_immediately()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new OpenPicker(null));
            int notifications = 0;
            vm.SaveCommand.CanExecuteChanged += (_, _) => notifications++;

            vm.AddVariantCommand.Execute(null);   // selects the new variant
            Assert.True(vm.CanSave);              // it has a color raster, so there IS art to write
            Assert.True(notifications > 0, "SaveCommand never raised CanExecuteChanged, so the button stays stale");

            // The point of the raster existing is that a stroke lands on it without an import first.
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            var painted = vm.ColorAt(4, 4);
            Assert.Equal(255, painted.R); Assert.Equal(0, painted.G); Assert.Equal(0, painted.B);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>NextVariantId reuses the smallest free id, so a deleted variant's import must be dropped
    /// with it — otherwise the next added variant inherits the dead variant's art and Save writes it.</summary>
    [AvaloniaFact]
    public async Task Deleting_a_custom_variant_drops_its_art_so_a_reused_id_starts_blank()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new ConfirmingDialogsStub(), new OpenPicker(pngPath));
            vm.AddVariantCommand.Execute(null);
            await vm.ImportImageCommand.ExecuteAsync(null);   // the added variant now has art
            Assert.Equal(200, vm.ColorAt(4, 4).G);

            await vm.DeleteVariantCommand.ExecuteAsync(null); // delete it (frees its id)
            vm.AddVariantCommand.Execute(null);               // re-adds with the SAME id

            // Asserted on the PIXELS: the old save-blocked flag is gone, so an inherited ghost would
            // now be silently savable rather than loudly refused.
            var fresh = vm.ColorAt(4, 4);
            Assert.Equal(0, fresh.A);                         // fully transparent - no inherited ghost
            Assert.Equal(0, fresh.G);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true); }
    }

    /// <summary>Duplicate copies the draft's (grayscale) ValueMap, which is not where a custom variant's
    /// pixels live — so the copy must inherit the effective image, or it renders a gray ghost and
    /// silently blocks Save.</summary>
    [AvaloniaFact]
    public async Task Duplicating_a_custom_variant_carries_its_image()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new OpenPicker(pngPath));
            await vm.ImportImageCommand.ExecuteAsync(null);
            vm.DuplicateVariantCommand.Execute(null);

            var (r, g, b, _) = ReadPixel(vm.Canvas, 8, 8, 4, 4);   // the copy is selected
            Assert.Equal(10, r); Assert.Equal(200, g); Assert.Equal(40, b);   // color carried, not gray
            Assert.True(vm.CanSave);                                          // and Save stays available
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true); }
    }

    /// <summary>The loose (.igt) custom save path is the one whose export clones are DISPOSED rather
    /// than adopted — the seam the spec called sharp. Prove color survives, twice.</summary>
    [AvaloniaFact]
    public async Task Loose_custom_save_round_trips_full_color_twice()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var igt = Path.Combine(dir, "art.igt");
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var manifest = new IngredientManifest("art", "Art", LayerKind.Custom, null,
                new[] { new Variant("v1", "V1", 1) });
            var seed = new Dictionary<string, Image<Rgba32>> { ["v1"] = new(8, 8) };
            IngredientArchive.Write(igt, manifest, seed);
            foreach (var i in seed.Values) i.Dispose();

            for (int pass = 0; pass < 2; pass++)
            {
                var loaded = IngredientArchive.Read(igt);
                var book = LooseWorkspace.WrapIngredient(loaded);
                var vm = new IngredientEditorViewModel(loaded, book.Recipes[0], book, new ImageBridge(),
                    new FakeNav(), new CookBookSession(), new FakeDialogs(),
                    new OpenPicker(pngPath), looseSavePath: igt);
                await vm.ImportImageCommand.ExecuteAsync(null);
                await vm.SaveCommand.ExecuteAsync(null);
                vm.Dispose();

                using var reread = IngredientArchive.Read(igt);
                var px = reread.VariantImages["v1"][4, 4];
                Assert.Equal(10, px.R); Assert.Equal(200, px.G); Assert.Equal(40, px.B);
                Assert.NotEqual(px.R, px.G);   // never round-tripped through the grayscale ValueMap
            }
        }
        finally { Directory.Delete(dir, recursive: true); Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true); }
    }

    [AvaloniaFact]
    // A value-map stores lightness only, so a color source must be collapsed to one channel. Handing
    // it straight to ValueMap.FromImage would keep the RED channel - fine for round-tripping this
    // layer's own grayscale PNG, but arbitrary for foreign art. The import desaturates first, and says
    // that the color is gone.
    public async Task Importing_a_color_image_into_a_value_map_uses_luminance_and_says_so()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();   // dynamic 8x8
        // Pure green is the case that exposes the difference: the red channel calls it BLACK, while
        // it is the brightest of the three primaries to the eye.
        var pngPath = WritePng(8, 8, 0, 255, 0);
        var dialogs = new RecordingDialogs();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, dialogs, new OpenPicker(pngPath));

            await vm.ImportImageCommand.ExecuteAsync(null);

            Assert.Equal("Color flattened", dialogs.ErrorTitle);
            // BT.709 luminance of pure green is ~182. The red channel would have given 0.
            Assert.InRange(vm.ValueAt(4, 4), 175, 190);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    // The counterpart: a genuinely grayscale source loses nothing, so it must NOT nag - and must
    // still round-trip EXACTLY, since desaturating an already-gray pixel has to be a no-op.
    public async Task Importing_a_grayscale_image_into_a_value_map_does_not_warn()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        var pngPath = WritePng(8, 8, 180, 180, 180);
        var dialogs = new RecordingDialogs();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, dialogs, new OpenPicker(pngPath));

            await vm.ImportImageCommand.ExecuteAsync(null);

            Assert.Null(dialogs.ErrorTitle);
            Assert.Equal(180, vm.ValueAt(4, 4));
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    private sealed class ConfirmingDialogsStub : IDialogService
    {
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d) => Task.FromResult((TResult?)(object?)true);
        public void Close(object? result) { }
    }
}
