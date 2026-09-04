using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Kitchen shelf: one row of cards, paged by kind.
/// </summary>
/// <remarks>
/// The behaviour that matters is the paging — one flat sequence across the three kinds, so crossing
/// from the last CookBook page into the first Recipe page is the same gesture as moving within a
/// kind — and the fact that the row is one shape whatever is on it: a short final page keeps its
/// empty slots rather than re-spacing the cards.
/// </remarks>
public class KitchenShelfTests
{
    private static KitchenCard Card(string name, KitchenItemKind kind) =>
        new($"C:/k/{name}", name, "meta", kind);

    private static IReadOnlyList<KitchenCard> Cards(int books, int recipes, int ingredients)
    {
        var all = new List<KitchenCard>();
        for (int i = 1; i <= books; i++) all.Add(Card($"book{i}", KitchenItemKind.CookBook));
        for (int i = 1; i <= recipes; i++) all.Add(Card($"rcp{i}", KitchenItemKind.Recipe));
        for (int i = 1; i <= ingredients; i++) all.Add(Card($"igt{i}", KitchenItemKind.Ingredient));
        return all;
    }

    private static KitchenShelfViewModel Shelf(int books, int recipes, int ingredients, int pageSize = 3)
    {
        var vm = new KitchenShelfViewModel { PageSize = pageSize };
        vm.Load("Studio", Cards(books, recipes, ingredients));
        return vm;
    }

    // ---------------- paging ----------------

    [Fact]
    public void Paging_runs_as_one_flat_sequence_across_the_three_kinds()
    {
        var vm = Shelf(books: 5, recipes: 2, ingredients: 4);   // pages: 2 + 1 + 2 = 5

        var seen = new List<string>();
        for (int i = 0; ; i++)
        {
            seen.Add($"{vm.WhereText} | {vm.PageText}");
            if (!vm.CanNext) break;
            vm.Page(1);
            Assert.True(i < 20, "paging never reached the end");
        }

        Assert.Equal(new[]
        {
            "· Studio · CookBooks | page 1 of 2",
            "· Studio · CookBooks | page 2 of 2",
            "· Studio · Recipes | page 1 of 1",
            "· Studio · Ingredients | page 1 of 2",
            "· Studio · Ingredients | page 2 of 2",
        }, seen);
    }

    [Fact]
    public void A_kind_with_nothing_in_it_contributes_no_page()
    {
        var vm = Shelf(books: 2, recipes: 0, ingredients: 2);

        var kinds = new List<string> { vm.WhereText };
        while (vm.CanNext) { vm.Page(1); kinds.Add(vm.WhereText); }

        Assert.DoesNotContain(kinds, k => k.Contains("Recipes", StringComparison.Ordinal));
        Assert.Equal(2, kinds.Count);
    }

    [Fact]
    public void The_ends_are_clamped_not_wrapped()
    {
        var vm = Shelf(books: 4, recipes: 0, ingredients: 0);   // 2 pages

        Assert.False(vm.CanPrev);
        vm.Page(-1);                       // already at the start
        Assert.Equal(0, vm.PageIndex);

        vm.Page(1);
        Assert.False(vm.CanNext);
        vm.Page(1);                        // already at the end — must not wrap to the beginning
        Assert.Equal(1, vm.PageIndex);
    }

    /// <summary>The row is one shape whatever is on it. A short final page keeps its empty slots, so
    /// the cards do not re-space themselves as you page through — the no-reflow rule, applied to
    /// content rather than to chrome.</summary>
    [Fact]
    public void A_short_final_page_keeps_its_empty_slots()
    {
        var vm = Shelf(books: 4, recipes: 0, ingredients: 0, pageSize: 3);

        Assert.Equal(3, vm.Row.Count);
        Assert.All(vm.Row, c => Assert.NotNull(c));

        vm.Page(1);                        // page 2 holds one card

        Assert.Equal(3, vm.Row.Count);     // still three slots
        Assert.NotNull(vm.Row[0]);
        Assert.Null(vm.Row[1]);
        Assert.Null(vm.Row[2]);
    }

    /// <summary>The guarantee is that the card you were looking at is STILL ON SCREEN, not that it is
    /// first: repaginating at a different page size necessarily reshuffles which card leads a page.
    /// Snapping to the start on every resize is the bug this prevents.</summary>
    [Fact]
    public void Resizing_the_row_keeps_the_card_the_reader_was_looking_at_on_screen()
    {
        var vm = Shelf(books: 9, recipes: 0, ingredients: 0, pageSize: 3);
        vm.Page(2);                                     // page 3 of 3: book7, book8, book9
        Assert.Equal("book7", vm.Row[0]!.Name);

        vm.PageSize = 4;                                // repaginate: [1-4] [5-8] [9]

        Assert.Contains(vm.Row, c => c?.Name == "book7");
        Assert.NotEqual(0, vm.PageIndex);               // and emphatically not back at the start
    }

    [Fact]
    public void A_narrower_row_than_one_card_still_shows_one()
    {
        var vm = Shelf(books: 3, recipes: 0, ingredients: 0, pageSize: 1);
        vm.PageSize = 0;                                // what a mid-layout measurement can hand it

        Assert.Single(vm.Row);
        Assert.NotNull(vm.Row[0]);
    }

    // ---------------- the three states, one box ----------------

    [Fact]
    public void No_kitchen_open_is_its_own_state()
    {
        var vm = new KitchenShelfViewModel();
        vm.Load(null, Array.Empty<KitchenCard>());

        Assert.True(vm.ShowNoKitchen);
        Assert.False(vm.ShowEmptyKitchen);
        Assert.False(vm.HasCards);
        Assert.Equal("· no workspace open", vm.WhereText);
        Assert.Equal("", vm.PageText);
        Assert.False(vm.CanPrev);
        Assert.False(vm.CanNext);
    }

    [Fact]
    public void An_open_but_empty_kitchen_is_a_different_state_from_no_kitchen()
    {
        var vm = new KitchenShelfViewModel();
        vm.Load("Studio", Array.Empty<KitchenCard>());

        Assert.False(vm.ShowNoKitchen);
        Assert.True(vm.ShowEmptyKitchen);
        Assert.Equal("· Studio · empty", vm.WhereText);
    }

    [Fact]
    public void Loading_a_kitchen_returns_to_the_first_page()
    {
        var vm = Shelf(books: 9, recipes: 0, ingredients: 0);
        vm.Page(2);
        Assert.Equal(2, vm.PageIndex);

        vm.Load("Other", Cards(2, 0, 0));

        Assert.Equal(0, vm.PageIndex);
        Assert.Equal("· Other · CookBooks", vm.WhereText);
    }

    [Fact]
    public void Activating_a_card_opens_that_card_and_a_padding_slot_opens_nothing()
    {
        KitchenCard? opened = null;
        var vm = new KitchenShelfViewModel(c => opened = c) { PageSize = 3 };
        vm.Load("Studio", Cards(1, 0, 0));

        vm.OpenCardCommand.Execute(vm.Row[0]);
        Assert.Equal("book1", opened!.Name);

        opened = null;
        vm.OpenCardCommand.Execute(vm.Row[1]);   // an empty slot
        Assert.Null(opened);
    }

    // ---------------- cards from a real workspace ----------------

    [Fact]
    public void Cards_are_built_by_peeking_each_archive_and_grouped_by_kind()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            WriteBook(Path.Combine(dir, "VaporPets.cbk"), "cat", "dog");
            WriteIngredient(Path.Combine(dir, "aura.igt"), LayerKind.Dynamic, 4);
            var ktn = Path.Combine(dir, "Studio.ktn");
            Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

            var cards = KitchenShelfViewModel.CardsFor(Kitchen.Open(ktn));

            var book = Assert.Single(cards, c => c.Kind == KitchenItemKind.CookBook);
            Assert.Equal("VaporPets", book.Name);
            Assert.Equal("2 recipes · 8×8", book.Meta);
            Assert.False(book.IsLoose);

            var ing = Assert.Single(cards, c => c.Kind == KitchenItemKind.Ingredient);
            Assert.Equal("Aura", ing.Name);
            Assert.Equal("dynamic · 4 variants", ing.Meta);
            Assert.True(ing.IsLoose);

            // CookBooks before loose parts, which is the order the shelf pages in.
            Assert.Equal(KitchenItemKind.CookBook, cards[0].Kind);
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>An unreadable file in a workspace is a thing to SEE in the listing, not a reason for
    /// the listing to fail. The card stays and says so.</summary>
    [Fact]
    public void A_file_that_will_not_peek_keeps_its_card_and_loses_only_its_subtitle()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            WriteBook(Path.Combine(dir, "Good.cbk"), "cat");
            File.WriteAllText(Path.Combine(dir, "Broken.cbk"), "not a zip at all");
            var ktn = Path.Combine(dir, "Studio.ktn");
            Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

            var cards = KitchenShelfViewModel.CardsFor(Kitchen.Open(ktn));

            Assert.Equal(2, cards.Count);

            var broken = Assert.Single(cards, c => !c.Readable);
            Assert.Equal("Broken", broken.Name);          // the FILE name, since there is no manifest to name it
            Assert.Equal("could not be read", broken.Meta);

            // The readable one is untouched by its neighbour, and carries its MANIFEST's name.
            var good = Assert.Single(cards, c => c.Readable);
            Assert.Equal("VaporPets", good.Name);
            Assert.Equal("1 recipe · 8×8", good.Meta);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void WriteBook(string path, params string[] recipeIds)
    {
        var recipes = recipeIds.Select(rid => new LoadedRecipe
        {
            Manifest = new RecipeManifest(rid, rid, new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ingredient(LayerKind.Custom, 1) },
        }).ToList();
        CookBookArchive.Write(path, new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
            new Collection("VaporPets", "", "VP"), recipeIds.ToDictionary(r => r, _ => 100.0)), recipes);
        foreach (var r in recipes) r.Dispose();
    }

    private static void WriteIngredient(string path, LayerKind kind, int variants)
    {
        using var ing = Ingredient(kind, variants);
        IngredientArchive.Write(path, ing.Manifest, ing.VariantImages);
    }

    private static LoadedIngredient Ingredient(LayerKind kind, int variants)
    {
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal);
        var vs = new List<Variant>();
        for (int i = 1; i <= variants; i++)
        {
            images[$"v{i}"] = new Image<Rgba32>(8, 8, new Rgba32(1, 2, 3, 255));
            vs.Add(new Variant($"v{i}", $"V{i}", 1));
        }
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", kind,
                kind == LayerKind.Custom ? null
                    : new Colorization(ColorModel.Hsv, 12, 4,
                        new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
                vs),
            VariantImages = images,
        };
    }
}
