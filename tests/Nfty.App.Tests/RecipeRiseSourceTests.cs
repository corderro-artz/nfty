using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class RecipeRiseSourceTests
{
    private static DepthMap Flat(byte front, byte back = 0) =>
        DepthMap.FromBytes(8, 8, Enumerable.Range(0, 64).SelectMany(_ => new[] { front, back }).ToArray());

    /// <summary>The first seed in 1..50 whose roll picks the oak body (weight 3 vs ghost 1).</summary>
    private static int OakSeed(LoadedCookBook book, LoadedRecipe recipe)
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            using var src = new RecipeRiseSource(book, recipe, seed, new ImageBridge());
            if (src.Layers[0].VariantName == "Oak") return seed;
        }
        throw new InvalidOperationException("No seed in 1..50 rolled oak.");
    }

    [AvaloniaFact]
    public void The_roll_is_the_recipe_panes_hero_for_the_same_seed()
    {
        using var book = RiseFixtures.ChestBook();
        var recipe = book.Recipes.Single(r => r.Manifest.Id == "chest");
        using var src = new RecipeRiseSource(book, recipe, seed: 7, new ImageBridge());
        using var hero = Generator.GenerateStreaming(book,
            new GenerateOptions(1, "7", "chest", EnforceUniqueDna: false)).First();
        var art = src.Open(src.Items[0]).Art;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.Equal(hero.Image[x, y], art[x, y]);
        Assert.Equal("seed 7", src.Items[0].Caption);
        Assert.Equal("Chest-7", src.Items[0].ExportBaseName);
        Assert.Equal(new[] { "seed 7", "canvas 8 × 8", "1 → paints first, furthest back" }, src.Facts);
    }

    [AvaloniaFact]
    public void Combined_depth_takes_the_topmost_solid_layer()
    {
        using var book = RiseFixtures.ChestBook(oakDepth: Flat(1), goldDepth: Flat(4));
        var recipe = book.Recipes.Single();
        int seed = OakSeed(book, recipe);
        using var src = new RecipeRiseSource(book, recipe, seed, new ImageBridge());

        var depth = src.Open(src.Items[0]).Depth;

        Assert.Equal(new DepthPixel(4, 0), depth.Get(3, 3));   // gold paints over oak here
        Assert.Equal(new DepthPixel(1, 0), depth.Get(1, 2));   // oak alone
        Assert.Equal("varied", src.Items[0].DepthChip);
    }

    [AvaloniaFact]
    public void Layer_rows_run_bottom_first_and_word_each_layers_own_depth()
    {
        using var book = RiseFixtures.ChestBook(oakDepth: Flat(1), goldDepth: null);
        var recipe = book.Recipes.Single();
        int seed = OakSeed(book, recipe);
        using var src = new RecipeRiseSource(book, recipe, seed, new ImageBridge());

        Assert.Equal(new[] { 1, 2 }, src.Layers.Select(l => l.Depth));
        Assert.Equal(new[] { "Body", "Lock" }, src.Layers.Select(l => l.Name));
        Assert.Equal(new[] { "Oak", "Gold" }, src.Layers.Select(l => l.VariantName));
        Assert.Equal(new[] { "1 · 0", "paper" }, src.Layers.Select(l => l.DepthText));
        Assert.All(src.Layers, l => { Assert.False(l.IsAbsent); Assert.NotNull(l.Thumbnail); Assert.Equal("C", l.KindMark); });
    }

    [AvaloniaFact]
    public void A_roll_with_a_partial_alpha_layer_is_paper_not_a_crash()
    {
        using var book = RiseFixtures.ChestBook();
        var recipe = book.Recipes.Single();
        int seed = Enumerable.Range(1, 50).First(s =>
        {
            using var probe = new RecipeRiseSource(book, recipe, s, new ImageBridge());
            return probe.Layers[0].VariantName == "Ghost";
        });
        using var src = new RecipeRiseSource(book, recipe, seed, new ImageBridge());

        // Gold paints over the one half-alpha pixel, so the composite is solid or clear: it is the
        // layer that cannot rise, and the stack rule refuses it by name, leaving paper.
        Assert.Equal("paper", src.Items[0].DepthChip);
        Assert.Contains("partly transparent", src.DepthNote);   // and it says why, not just "paper"
        Assert.Contains("no combined depth", src.DepthNote);

        src.Reroll();                                            // the reason belongs to the roll that had it
        Assert.Equal(src.Layers[0].VariantName == "Ghost", src.DepthNote.Contains("partly transparent"));
        Assert.True(src.Open(src.Items[0]).Depth.SameDepthAs(null));
    }

    [AvaloniaFact]
    public void Reroll_moves_the_seed_and_replaces_the_subject()
    {
        using var book = RiseFixtures.ChestBook();
        var recipe = book.Recipes.Single();
        using var src = new RecipeRiseSource(book, recipe, seed: 7, new ImageBridge());
        var before = src.Items[0].Subject;
        var rowsBefore = src.Layers;
        int raised = 0;
        src.Changed += () => raised++;

        src.Reroll();

        Assert.Equal(8, src.Seed);
        Assert.NotSame(before, src.Items[0].Subject);
        Assert.NotSame(rowsBefore, src.Layers);
        Assert.Equal(1, raised);
        Assert.Equal("seed 8", src.Items[0].Caption);
        Assert.Equal("Chest-8", src.Items[0].ExportBaseName);
        using var hero = Generator.GenerateStreaming(book,
            new GenerateOptions(1, "8", "chest", EnforceUniqueDna: false)).First();
        Assert.Equal(hero.Image[3, 3], src.Items[0].Subject!.Art[3, 3]);
    }

    [AvaloniaFact]
    public void A_recipe_source_is_read_only_and_says_so()
    {
        using var book = RiseFixtures.ChestBook();
        using var src = new RecipeRiseSource(book, book.Recipes.Single(), 7, new ImageBridge());

        Assert.False(src.IsEditable);
        Assert.False(src.CanSave);
        Assert.False(src.IsDirty);
        Assert.False(src.CanBatch);
        Assert.Equal("combined depth · read-only", src.ReadOnlyPill);
        Assert.Equal(RiseSourceKind.Recipe, src.Kind);
        Assert.Throws<NotSupportedException>(() => { _ = src.SaveAsync(); });
    }

    [AvaloniaFact]
    public void A_book_that_cannot_cook_shows_the_reason_and_a_clear_canvas()
    {
        using var book = RiseFixtures.ChestBook();
        // A recipe weight of zero everywhere: nothing can be rolled.
        var broken = new LoadedCookBook
        {
            Manifest = book.Manifest with { RecipeWeights = new System.Collections.Generic.Dictionary<string, double> { ["chest"] = 0 } },
            Recipes = book.Recipes,
        };
        using var src = new RecipeRiseSource(broken, broken.Recipes.Single(), 7, new ImageBridge());

        Assert.False(string.IsNullOrEmpty(src.Problem));
        Assert.Empty(src.Layers);
        var s = src.Open(src.Items[0]);
        Assert.Equal(0, s.Depth.SolidCount);
        Assert.Equal(8, s.Art.Width);
        Assert.Contains(src.Problem!, src.DepthNote);
    }
}
