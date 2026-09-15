using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// THE COMBINED CHANCE IS EXACT WHEN THE SOURCE COOKBOOK IS REACHABLE, AND SAYS SO WHEN IT IS NOT.
/// </summary>
/// <remarks>
/// <para>The product of the rarity table is an estimate twice over: it assumes the layers roll
/// independently, and it is built from the shares this SET happened to produce rather than from the
/// weights the book asked for. <c>CookBookLocator</c> already finds the book by hash, and
/// <c>SelectionOdds</c> prices an asset from it — so the caveat only has to stand when there is
/// genuinely nothing to compute from.</para>
///
/// <para>The book here makes the two answers differ BY CONSTRUCTION rather than by luck: a layer
/// weighted one-to-nine, cooked into a two-asset Set with unique DNA on, so each variant is exactly
/// half the collection. The estimate must say 1 in 2 and the truth is 1 in 10.</para>
/// </remarks>
public class ExactCombinedChanceTests
{
    /// <summary>A one-layer book whose weights are nothing like the shares a small Set shows.</summary>
    private static LoadedCookBook Skewed() => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("VaporCats", "desc", "VC"), new Dictionary<string, double> { ["cat"] = 100 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" },
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = new[]
                {
                    new LoadedIngredient
                    {
                        Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                            new[] { new Variant("a", "A", 1), new Variant("b", "B", 9) }),
                        VariantImages = new Dictionary<string, Image<Rgba32>>
                        {
                            ["a"] = new Image<Rgba32>(8, 8, new Rgba32(1, 0, 0, 255)),
                            ["b"] = new Image<Rgba32>(8, 8, new Rgba32(0, 1, 0, 255)),
                        },
                    },
                },
            },
        },
    };

    /// <summary>
    /// Writes the book to <paramref name="root"/> and cooks a Set into a folder beside it — the
    /// arrangement <c>CookBookLocator.Nearby</c> is written for, and the one a real author has.
    /// </summary>
    private static LoadedSet CookBesideTheBook(out string root, out string bookPath)
    {
        root = Directory.CreateTempSubdirectory().FullName;
        bookPath = Path.Combine(root, "skewed.cbk");

        using (var authored = Skewed())
            CookBookArchive.Write(bookPath, authored.Manifest, authored.Recipes);

        // Read it BACK: SourceSha256 is populated by the reader, and that is what set.json records
        // as cookbookSha256. Cooking the in-memory book would write a Set that cannot name its book.
        using var book = CookBookArchive.Read(bookPath);
        string setDir = Path.Combine(root, "set");
        using (var set = Generator.Generate(book, new GenerateOptions(2, "skew")))
            SetWriter.Write(set, setDir, pack: false);

        return SetReader.Read(setDir);
    }

    private static void PumpUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Points the rail at the asset wearing the rare variant, whichever number it got.</summary>
    private static void ShowRareAsset(SetBrowserViewModel vm) =>
        vm.Select(vm.Items.First(i => i.Item.Rarity.Any(r => r.Value == "A")));

    [AvaloniaFact]
    public void The_exact_chance_replaces_the_estimate_when_the_book_is_beside_the_set()
    {
        var loaded = CookBesideTheBook(out var root, out var bookPath);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            ShowRareAsset(vm);
            vm.ShowRarityOddsCommand.Execute(null);
            PumpUntil(() => !vm.CombinedText.Contains('~', StringComparison.Ordinal));

            // One in TEN, from the book's own 1:9 — not one in two, which is all the two-asset
            // collection in front of the reader can say on its own.
            Assert.Equal("this combo 1 in 10", vm.CombinedText);
            Assert.Contains(Path.GetFileName(bookPath), vm.CombinedTip, StringComparison.Ordinal);
            Assert.DoesNotContain("Estimated", vm.CombinedTip, StringComparison.Ordinal);
        }
        finally { vm.Dispose(); Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// THE PROBE. Same Set, same recorded hash, book moved out of reach — so the estimate is all
    /// there is, and the figure must both change and admit it. Without this the test above could
    /// pass on a browser that simply never estimated anything.
    /// </summary>
    [AvaloniaFact]
    public void With_the_book_out_of_reach_the_estimate_stands_and_is_marked()
    {
        var loaded = CookBesideTheBook(out var root, out var bookPath);
        File.Delete(bookPath);

        var vm = new SetBrowserViewModel(loaded);
        try
        {
            ShowRareAsset(vm);
            vm.ShowRarityOddsCommand.Execute(null);
            PumpUntil(() => false, timeoutMs: 150);       // give the search every chance to land

            Assert.Equal("this combo ~1 in 2", vm.CombinedText);
            Assert.StartsWith("Estimated:", vm.CombinedTip, StringComparison.Ordinal);
        }
        finally { vm.Dispose(); Directory.Delete(root, recursive: true); }
    }

    /// <summary>
    /// A BOOK THAT IS NOT THE SOURCE IS NOT USED. The locator matches on the hash, so an unrelated
    /// book sitting beside the Set produces no answer rather than a wrong one — which is the whole
    /// argument for looking around at all.
    /// </summary>
    [AvaloniaFact]
    public void A_different_book_beside_the_set_is_not_mistaken_for_the_source()
    {
        var loaded = CookBesideTheBook(out var root, out var bookPath);
        File.Delete(bookPath);
        using (var other = CoreTestBook.Tiny())
            CookBookArchive.Write(Path.Combine(root, "impostor.cbk"), other.Manifest, other.Recipes);

        var vm = new SetBrowserViewModel(loaded);
        try
        {
            ShowRareAsset(vm);
            vm.ShowRarityOddsCommand.Execute(null);
            PumpUntil(() => false, timeoutMs: 150);

            Assert.Equal("this combo ~1 in 2", vm.CombinedText);
        }
        finally { vm.Dispose(); Directory.Delete(root, recursive: true); }
    }

    /// <summary>The page's rarity unit governs the exact figure exactly as it governs the estimate —
    /// one toggle, every rarity on the screen.</summary>
    [AvaloniaFact]
    public void The_unit_toggle_governs_the_exact_figure_too()
    {
        var loaded = CookBesideTheBook(out var root, out _);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            ShowRareAsset(vm);
            PumpUntil(() => !vm.CombinedText.Contains('~', StringComparison.Ordinal));

            Assert.Equal("this combo 10%", vm.CombinedText);
            vm.ShowRarityOddsCommand.Execute(null);
            Assert.Equal("this combo 1 in 10", vm.CombinedText);
            vm.ShowRarityPercentCommand.Execute(null);
            Assert.Equal("this combo 10%", vm.CombinedText);
        }
        finally { vm.Dispose(); Directory.Delete(root, recursive: true); }
    }
}
