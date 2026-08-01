using System.Collections.Generic;
using System.IO;
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

public class LandingImportRcpTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static (LandingViewModel vm, FakeNav nav, CookBookSession session) Landing(IFilePickerService picker)
    {
        var nav = new FakeNav(); var dialogs = new FakeDialogs(); var notify = new FakeNotYetWired();
        var session = new CookBookSession();
        var vm = new LandingViewModel(nav, dialogs, notify, picker,
            new RecentsService(Directory.CreateTempSubdirectory().FullName), session,
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService()),
            set => new SetBrowserViewModel(set),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs));
        return (vm, nav, session);
    }

    private static string WriteRcp()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "cat.rcp");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Bg", LayerKind.Dynamic, null, new[] { new Variant("day", "Day", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["day"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        RecipeArchive.Write(path, recipe.Manifest, recipe.Ingredients);
        ing.Dispose();
        return path;
    }

    [AvaloniaFact]
    public async Task Import_rcp_opens_a_read_only_explorer()
    {
        var path = WriteRcp();
        var (vm, nav, session) = Landing(new StubPicker(path));
        try
        {
            await vm.ImportCommand.ExecuteAsync(null);
            var explorer = Assert.IsType<ExplorerViewModel>(nav.Current);
            Assert.NotNull(session.Current);
            Assert.Null(session.SourcePath);                 // no .cbk source → read-only
            Assert.Equal("cat", explorer.Root.Children[0].Id);   // the loose recipe is in the tree
            explorer.ToggleLockCommand.Execute(null);            // edit mode on
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);
            Assert.False(explorer.DeleteSelectedCommand.CanExecute(null));   // no source → still disabled
            explorer.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
