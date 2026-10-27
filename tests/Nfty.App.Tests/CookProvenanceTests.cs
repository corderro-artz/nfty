using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Extending a Set with a CookBook that is not the one it was cooked from is legitimate and
/// surprising in the same breath, so the cook card says so - in the CLI's words, as a warning, with
/// the extend carried out all the same.
/// </summary>
public class CookProvenanceTests
{
    private sealed class FixedFolder(string dir) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(dir);
    }

    private sealed class NoReveal : IFolderRevealer { public void Reveal(string path) { } }

    /// <summary>Writes the book to disk and reads it back, so it carries the hash of its own file.</summary>
    private static LoadedCookBook FromFile(string dir, string file, string? renamedTo = null)
    {
        using var src = ExplorerViewModelTests.TwoRecipeBook();
        var manifest = renamedTo is null ? src.Manifest : src.Manifest with { Name = renamedTo };
        var path = Path.Combine(dir, file);
        CookBookArchive.Write(path, manifest, src.Recipes);
        return CookBookArchive.Read(path);
    }

    private static async Task<CookDialogViewModel> CookInto(LoadedCookBook book, string setDir, string seed)
    {
        var vm = new CookDialogViewModel(book, new FixedFolder(setDir), new NoReveal(), new FakeDialogs())
        {
            Count = 2, Seed = seed, Unlimited = true,
        };
        await vm.ScanTask;
        await vm.CookCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        return vm;
    }

    [AvaloniaFact]
    public async Task Extending_over_a_changed_book_says_so_and_still_extends()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var setDir = Path.Combine(root, "set");
            using var original = FromFile(root, "a.cbk");
            using var edited = FromFile(root, "b.cbk", renamedTo: "Edited");
            Assert.NotEqual(original.SourceSha256, edited.SourceSha256);

            await CookInto(original, setDir, "first");
            var vm = await CookInto(edited, setDir, "second");

            Assert.True(vm.IsExtending);
            Assert.Equal(SetProvenance.Warning(original.SourceSha256, edited.SourceSha256), vm.ProvenanceWarning);
            Assert.True(vm.ProvenanceShown);
            Assert.Equal("warning: this CookBook is not the one this Set was cooked from.", vm.ProvenanceHeadline);
            Assert.Equal("Added +2 assets — 4 total in the collection.", vm.ResultText);   // a warning, not a refusal
            Assert.Equal(4, SetWriter.ReadExisting(setDir).Dnas.Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Extending_over_the_same_book_says_nothing()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var setDir = Path.Combine(root, "set");
            using var book = FromFile(root, "a.cbk");
            await CookInto(book, setDir, "first");
            var vm = await CookInto(book, setDir, "second");

            Assert.True(vm.IsExtending);
            Assert.Equal("", vm.ProvenanceWarning);
            Assert.False(vm.ProvenanceShown);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_fresh_cook_never_warns()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = FromFile(root, "a.cbk");
            var vm = await CookInto(book, Path.Combine(root, "set"), "first");
            Assert.False(vm.IsExtending);
            Assert.Equal("", vm.ProvenanceWarning);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
