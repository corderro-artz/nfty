using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingOpenFlowTests
{
    private sealed class StubPicker : IFilePickerService
    {
        private readonly string? _path;
        public StubPicker(string? path) => _path = path;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(_path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Make(string? pickerPath, out FakeNav nav, out FakeDialogs dialogs,
        out FakeNotYetWired notify, out CookBookSession session)
    {
        nav = new FakeNav(); dialogs = new FakeDialogs(); notify = new FakeNotYetWired(); session = new CookBookSession();
        var s = session; var n = nav; var d = dialogs; var no = notify;
        return new LandingViewModel(n, d, no, new StubPicker(pickerPath), new RecentsService(), s,
            book => new ExplorerViewModel(book, n, d, no));
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

    [Fact]
    public void Import_of_a_loose_igt_reports_the_kitchen_message()
    {
        var vm = Make("thing.igt", out _, out _, out var notify, out _);
        vm.ImportCommand.Execute(null);
        Assert.Contains("Kitchen", notify.Last);
    }

    private static void WriteTinyCookBook(string path)
    {
        // reuse the in-memory book builder + CookBookArchive.Write
        var book = ExplorerViewModelTests.TwoRecipeBook();
        CookBookArchive.Write(path, book.Manifest, book.Recipes);
    }
}
