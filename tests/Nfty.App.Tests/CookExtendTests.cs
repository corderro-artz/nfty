using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Cooking into a folder that already holds a Set must ADD to it.
///
/// The GUI never passed <c>existingDnas</c>/<c>startNumber</c> to the generator, which was not just
/// the `extend` command being unavailable from the UI. SetWriter names files by
/// <c>asset.SetNumber</c>, so generating from 1 into a folder that already contained 0001.png
/// silently OVERWROTE the previous assets — and with no existing DNAs supplied, the replacements
/// could duplicate them. The writer was already extend-aware; only the generator was told nothing.
///
/// These tests are written against the observable artifacts on disk, because that is where the data
/// loss would have happened.</summary>
public class CookExtendTests
{
    private sealed class FixedFolder : IFilePickerService
    {
        private readonly string _dir;
        public FixedFolder(string dir) => _dir = dir;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(_dir);
    }

    private sealed class NoReveal : IFolderRevealer { public void Reveal(string path) { } }

    private static CookDialogViewModel Cook(Nfty.Core.Formats.LoadedCookBook book, string dir) =>
        new(book, new FixedFolder(dir), new NoReveal(), new FakeDialogs());

    private static int PngCount(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories).Length : 0;

    private static string[] Dnas(string dir) =>
        Directory.GetFiles(Path.Combine(dir, "nfty"), "*.json")
            .Select(File.ReadAllText)
            .Select(t => System.Text.Json.JsonDocument.Parse(t).RootElement.GetProperty("dna").GetString()!)
            .ToArray();

    [AvaloniaFact]
    public async Task A_second_cook_adds_to_the_set_instead_of_overwriting_it()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using (var book = ExplorerViewModelTests.TwoRecipeBook())
            {
                var first = Cook(book, dir);
                first.Count = 1; first.Seed = "seed-a";
                await first.CookCommand.ExecuteAsync(null);
                Assert.True(first.IsDone);
                Assert.False(first.IsExtending);      // nothing was there
            }

            var afterFirst = PngCount(dir);
            var dnasAfterFirst = Dnas(dir);
            Assert.Equal(1, afterFirst);

            using (var book = ExplorerViewModelTests.TwoRecipeBook())
            {
                var second = Cook(book, dir);
                second.Count = 1; second.Seed = "seed-b";
                await second.CookCommand.ExecuteAsync(null);
                Assert.True(second.IsDone);
                Assert.True(second.IsExtending);      // it noticed
            }

            // The assertion the old code would have failed: the first asset is still there.
            Assert.Equal(afterFirst + 1, PngCount(dir));

            var all = Dnas(dir);
            Assert.Equal(2, all.Length);
            Assert.All(dnasAfterFirst, d => Assert.Contains(d, all));   // originals survived
            Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());   // and no duplicate DNA
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Extending_numbers_the_new_assets_after_the_existing_ones()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using (var book = ExplorerViewModelTests.TwoRecipeBook())
            {
                var first = Cook(book, dir);
                first.Count = 1; first.Seed = "s1";
                await first.CookCommand.ExecuteAsync(null);
            }
            using (var book = ExplorerViewModelTests.TwoRecipeBook())
            {
                var second = Cook(book, dir);
                second.Count = 1; second.Seed = "s2";
                await second.CookCommand.ExecuteAsync(null);
                // Reported as an addition, not as a fresh set of one.
                Assert.Contains("+1", second.ResultText);
                Assert.Contains("2 total", second.ResultText);
                // And it counts in the singular when it added one. The sentence was
                // "Added 1 assets" for exactly as long as nothing read it.
                Assert.Contains("1 asset ", second.ResultText);
            }

            var numbers = Directory.GetFiles(Path.Combine(dir, "nfty"), "*.json")
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            // Distinct numbering is the thing that stops the second write clobbering the first.
            Assert.Equal(2, numbers.Length);
            Assert.Equal(numbers.Length, numbers.Distinct(StringComparer.Ordinal).Count());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_first_cook_into_an_empty_folder_is_not_reported_as_an_extend()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = ExplorerViewModelTests.TwoRecipeBook();
            var vm = Cook(book, dir);
            vm.Count = 1; vm.Seed = "only";
            await vm.CookCommand.ExecuteAsync(null);

            Assert.False(vm.IsExtending);
            Assert.DoesNotContain("+", vm.ResultText);
            Assert.DoesNotContain("total", vm.ResultText);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
