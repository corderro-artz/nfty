using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingImportIgtTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Landing(FakeNav nav, IFilePickerService picker, IDialogService dialogs)
    {
        var session = new CookBookSession(); var notify = new FakeNotYetWired();
        return new LandingViewModel(nav, dialogs, notify, picker,
            new RecentsService(Directory.CreateTempSubdirectory().FullName), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs)),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
    }

    private static string WriteIgt()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "aura.igt");
        var manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
            new Colorization(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
            new[] { new Variant("glow", "Glow", 1) });
        var images = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8) };
        IngredientArchive.Write(path, manifest, images);
        foreach (var i in images.Values) i.Dispose();
        return path;
    }

    [AvaloniaFact]
    public async Task Import_igt_opens_the_editor()
    {
        var path = WriteIgt();
        var nav = new FakeNav();
        try
        {
            var vm = Landing(nav, new StubPicker(path), new FakeDialogs());
            await vm.ImportCommand.ExecuteAsync(null);
            Assert.IsType<IngredientEditorViewModel>(nav.Current);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
