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
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(pngPath));
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
                new FakeNav(), new FakeNotYetWired(), session, dialogs, new OpenPicker(pngPath));

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
                new FakeNav(), new FakeNotYetWired(), session, dialogs, new OpenPicker(badPath));

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
    public async Task Cancelled_import_changes_nothing()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(null));

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
    public async Task Import_into_a_custom_variant_keeps_full_colour()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);   // a distinctly non-gray colour
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(pngPath));

            await vm.ImportImageCommand.ExecuteAsync(null);

            var (r, g, b, a) = ReadPixel(vm.Canvas, 8, 8, 4, 4);
            Assert.Equal(10, r); Assert.Equal(200, g); Assert.Equal(40, b); Assert.Equal(255, a);
            Assert.NotEqual(r, g);   // proves this is full colour, not a grayscale value-map
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

    [AvaloniaFact]
    public void Custom_cannot_be_painted()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(null));

            Assert.False(vm.CanPaint);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });   // must be a no-op for a custom layer

            Assert.False(vm.IsDirty);
            Assert.False(vm.UndoCommand.CanExecute(null));
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // The headline guarantee: a CUSTOM ingredient's imported image must survive Save in full colour —
    // it must never round-trip through ValueMap (grayscale by construction). Re-reading the archive and
    // finding R != G != B at a known pixel proves no value-map round-trip happened.
    [AvaloniaFact]
    public async Task Custom_save_round_trips_full_colour()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(pngPath));
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

    [AvaloniaFact]
    public void Custom_save_is_blocked_when_a_variant_has_no_image()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(null));
            vm.AddVariantCommand.Execute(null);   // the new variant has no original and no import
            Assert.True(vm.IsDirty);
            Assert.False(vm.CanSave);             // gated: not every custom variant has an image
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // ---- Review regressions (C2 whole-branch review) ----

    /// <summary>Custom Save depends on the per-variant image set, which import/add/duplicate/delete all
    /// change while IsDirty is already true — so the [NotifyCanExecuteChangedFor(IsDirty)] wiring never
    /// fires. Assert the COMMAND's notification, not just the CanSave property: the property is computed
    /// live and passes even when the bound button is stale.</summary>
    [AvaloniaFact]
    public async Task Custom_save_availability_is_notified_when_the_image_set_changes()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(pngPath));
            int notifications = 0;
            vm.SaveCommand.CanExecuteChanged += (_, _) => notifications++;

            vm.AddVariantCommand.Execute(null);          // image-less custom variant → Save blocked
            Assert.False(vm.CanSave);
            Assert.NotNull(vm.SaveBlockedReason);        // and the UI can say which variant
            notifications = 0;

            await vm.ImportImageCommand.ExecuteAsync(null);   // now it has an image → Save becomes valid
            Assert.True(vm.CanSave);
            Assert.Null(vm.SaveBlockedReason);
            Assert.True(notifications > 0, "SaveCommand never raised CanExecuteChanged, so the button stays stale");
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true); }
    }

    /// <summary>NextVariantId reuses the smallest free id, so a deleted variant's import must be dropped
    /// with it — otherwise the next added variant inherits the dead variant's art and Save writes it.</summary>
    [AvaloniaFact]
    public async Task Deleting_a_custom_variant_drops_its_import_so_a_reused_id_starts_blank()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new ConfirmingDialogsStub(), new OpenPicker(pngPath));
            vm.AddVariantCommand.Execute(null);
            await vm.ImportImageCommand.ExecuteAsync(null);   // the added variant now has art
            Assert.True(vm.CanSave);

            await vm.DeleteVariantCommand.ExecuteAsync(null); // delete it (frees its id)
            vm.AddVariantCommand.Execute(null);               // re-adds with the SAME id
            Assert.False(vm.CanSave);                         // must be blank again, not the ghost
            Assert.NotNull(vm.SaveBlockedReason);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true); }
    }

    /// <summary>Duplicate copies the draft's (grayscale) ValueMap, which is not where a custom variant's
    /// pixels live — so the copy must inherit the effective image, or it renders a grey ghost and
    /// silently blocks Save.</summary>
    [AvaloniaFact]
    public async Task Duplicating_a_custom_variant_carries_its_image()
    {
        var (path, session, recipe, ing) = IngredientEditorSaveTests.OnDisk(LayerKind.Custom);
        var pngPath = WritePng(8, 8, 10, 200, 40);
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), new OpenPicker(pngPath));
            await vm.ImportImageCommand.ExecuteAsync(null);
            vm.DuplicateVariantCommand.Execute(null);

            var (r, g, b, _) = ReadPixel(vm.Canvas, 8, 8, 4, 4);   // the copy is selected
            Assert.Equal(10, r); Assert.Equal(200, g); Assert.Equal(40, b);   // colour carried, not grey
            Assert.True(vm.CanSave);                                          // and Save stays available
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); Directory.Delete(Path.GetDirectoryName(pngPath)!, recursive: true); }
    }

    /// <summary>The loose (.igt) custom save path is the one whose export clones are DISPOSED rather
    /// than adopted — the seam the spec called sharp. Prove colour survives, twice.</summary>
    [AvaloniaFact]
    public async Task Loose_custom_save_round_trips_full_colour_twice()
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
                    new FakeNav(), new FakeNotYetWired(), new CookBookSession(), new FakeDialogs(),
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

    private sealed class ConfirmingDialogsStub : IDialogService
    {
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d) => Task.FromResult((TResult?)(object?)true);
        public void Close(object? result) { }
    }
}
