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
}
