using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingOpenFlowTests
{
    [Fact]
    public async Task Stub_folder_picker_returns_null()
        => Assert.Null(await new FilePickerService().PickFolderAsync("x"));

    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Make(string? pickerPath, out FakeNav nav, out FakeDialogs dialogs,
        out FakeNotYetWired notify, out CookBookSession session)
    {
        nav = new FakeNav(); dialogs = new FakeDialogs(); notify = new FakeNotYetWired(); session = new CookBookSession();
        var s = session; var n = nav; var d = dialogs; var no = notify;
        return new LandingViewModel(n, d, no, new StubPicker(pickerPath),
            new RecentsService(Directory.CreateTempSubdirectory().FullName), s,
            book => new ExplorerViewModel(book, n, d, no, new ImageBridge(), ExplorerViewModelTests.EditorFactory(n),
                ExplorerViewModelTests.CookFactory(d), new CookBookSession(),
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(n, new CookBookSession(), d), new StatusService()),
            set => new SetBrowserViewModel(set),
                (_, _, _) => null!);
    }

    [Fact]
    public void Open_reads_the_cbk_opens_the_session_and_navigates_to_explorer()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(tmp.FullName, "VaporPets.cbk");
            WriteTinyCookBook(path);   // helper below
            var vm = Make(path, out var nav, out _, out _, out var session);
            vm.OpenCookBookCommand.Execute(null);
            Assert.NotNull(session.Current);
            Assert.IsType<ExplorerViewModel>(nav.Current);
        }
        finally { tmp.Delete(true); }
    }

    [Fact]
    public void Cancelled_picker_does_nothing()
    {
        var vm = Make(null, out var nav, out _, out _, out var session);
        vm.OpenCookBookCommand.Execute(null);
        Assert.Null(session.Current);
        Assert.Null(nav.Current);
    }

    [Fact]
    public void A_bad_path_shows_the_error_dialog_and_does_not_navigate()
    {
        var vm = Make("does-not-exist.cbk", out var nav, out var dialogs, out _, out var session);
        vm.OpenCookBookCommand.Execute(null);
        Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.Null(nav.Current);
        Assert.Null(session.Current);
    }

    // A loose .rcp now opens read-only into the Explorer (B2 — covered end-to-end with a real archive
    // in LandingImportRcpTests); here a bad/nonexistent path shows the error dialog and leaves the
    // session/nav untouched, same shape as the bad-.cbk-path case above.
    [Fact]
    public void Import_of_a_bad_rcp_path_shows_the_error_dialog_and_does_not_navigate()
    {
        var vm = Make("does-not-exist.rcp", out var nav, out var dialogs, out _, out var session);
        vm.ImportCommand.Execute(null);
        Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.Null(nav.Current);
        Assert.Null(session.Current);
    }

    /// <summary>
    /// The rest of the flow tests above use a throwaway in-memory book; this one proves the same
    /// path against the real fixture archive on disk, so a change to how Explorer builds its tree
    /// from a genuine multi-kind/multi-layer CookBook (not just the tests' own minimal builder)
    /// would be caught. Expectations are read from the archive itself, not hard-coded, so the test
    /// can't silently drift from tests/fixtures/VaporPets.cbk.
    /// </summary>
    [Fact]
    public void Opening_the_real_VaporPets_fixture_renders_its_actual_tree_in_Explorer()
    {
        string path = FixturePath("VaporPets.cbk");
        using var reference = CookBookArchive.Read(path);
        string expectedCookBookName = reference.Manifest.Name;
        var expectedRecipes = reference.Recipes.Select(r => new
        {
            r.Manifest.Id,
            r.Manifest.Name,
            Ingredients = r.Manifest.LayerOrder
                .Select(id => r.Ingredients.Single(i => i.Manifest.Id == id).Manifest)
                .ToList(),
        }).ToList();

        var vm = Make(path, out var nav, out _, out _, out var session);
        vm.OpenCookBookCommand.Execute(null);

        Assert.NotNull(session.Current);
        var explorer = Assert.IsType<ExplorerViewModel>(nav.Current);
        Assert.Equal(ExplorerNodeKind.CookBook, explorer.Root.Kind);
        Assert.Equal(expectedCookBookName, explorer.Root.Name);
        Assert.Equal(expectedRecipes.Select(r => r.Id), explorer.Root.Children.Select(c => c.Id));
        Assert.All(explorer.Root.Children, c => Assert.Equal(ExplorerNodeKind.Recipe, c.Kind));

        foreach (var (recipeNode, expected) in explorer.Root.Children.Zip(expectedRecipes))
        {
            Assert.Equal(expected.Name, recipeNode.Name);
            Assert.Equal(expected.Ingredients.Select(i => i.Id), recipeNode.Children.Select(c => c.Id));
            Assert.Equal(expected.Ingredients.Select(i => i.Name), recipeNode.Children.Select(c => c.Name));
            Assert.All(recipeNode.Children, c => Assert.Equal(ExplorerNodeKind.Ingredient, c.Kind));
        }
    }

    /// <summary>Walks up from the test assembly's output directory to find the repo's
    /// tests/fixtures folder, mirroring how Nfty.Core.Tests's FixtureArchiveTests resolves the
    /// same fixtures (there via a build-time content copy; here directly, since this project has
    /// no such copy). Keeps the test independent of the current working directory.</summary>
    private static string FixturePath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tests", "fixtures", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not locate tests/fixtures/{name} above {AppContext.BaseDirectory}");
    }

    private static void WriteTinyCookBook(string path)
    {
        // reuse the in-memory book builder + CookBookArchive.Write
        var book = ExplorerViewModelTests.TwoRecipeBook();
        CookBookArchive.Write(path, book.Manifest, book.Recipes);
    }
}
