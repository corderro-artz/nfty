using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>The two text reports the CLI's <c>stats</c> and <c>inspect</c> print.
///
/// They live in Core so the GUI can show and copy the SAME text rather than re-deriving something
/// similar. That is the property most of these pin: the content, its culture-independence, and — for
/// inspect — that it shows the ids, which is the entire reason the command exists.</summary>
public class ReportTests
{
    private static LoadedIngredient Ing(string id, string name, LayerKind kind,
        params (string Id, string Name, double Weight)[] variants) => new()
    {
        Manifest = new IngredientManifest(id, name, kind, null,
            variants.Select(v => new Variant(v.Id, v.Name, v.Weight)).ToArray()),
        VariantImages = variants.ToDictionary(v => v.Id, _ => new Image<Rgba32>(2, 2)),
    };

    private static LoadedCookBook Book()
    {
        var bg = Ing("bg-id", "Background", LayerKind.Custom, ("day-id", "Daylight", 3), ("night-id", "Nightfall", 1));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat-id", "Cat", new[] { "bg-id" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { bg },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb-id", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat-id"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    // ---- inspect ------------------------------------------------------------------------------

    /// <summary>The whole point of the command: ids, which the GUI never shows and which are what
    /// --recipe and --variant expect. Names alone would make the report useless.</summary>
    [Fact]
    public void The_identity_report_shows_ids_not_just_names()
    {
        using var book = Book();
        var text = IdentityReport.Render(book);

        Assert.Contains("[cb-id]", text);
        Assert.Contains("[cat-id]", text);
        Assert.Contains("[bg-id]", text);
        Assert.Contains("[day-id]", text);
        Assert.Contains("[night-id]", text);

        // And still the names, or you cannot tell which id is which.
        Assert.Contains("VaporPets", text);
        Assert.Contains("Daylight", text);
    }

    [Fact]
    public void The_identity_report_shows_the_layer_kind_and_weights()
    {
        using var book = Book();
        var text = IdentityReport.Render(book);

        Assert.Contains("custom", text);
        Assert.Contains("weight 3", text);
    }

    /// <summary>A layer named in the order but absent from the archive is shown, not skipped.
    /// Validator flags it; a report that quietly omitted it would hide the very thing an author
    /// opened the report to find.</summary>
    [Fact]
    public void A_missing_layer_is_reported_rather_than_skipped()
    {
        using var src = Book();
        using var broken = new LoadedCookBook
        {
            Manifest = src.Manifest,
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat-id", "Cat", new[] { "bg-id", "ghost-id" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = src.Recipes[0].Ingredients,
                },
            },
        };

        var text = IdentityReport.Render(broken);

        Assert.Contains("ghost-id", text);
        Assert.Contains("MISSING", text);
    }

    [Fact]
    public void The_identity_report_follows_layer_order_not_collection_order()
    {
        var a = Ing("a-id", "Alpha", LayerKind.Custom, ("v", "V", 1));
        var b = Ing("b-id", "Beta", LayerKind.Custom, ("v", "V", 1));
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(2, 2),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["r"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    // Order deliberately the reverse of the Ingredients array: layer order is what
                    // composites, and that is what an author reading this is asking about.
                    Manifest = new RecipeManifest("r", "R", new[] { "b-id", "a-id" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { a, b },
                },
            },
        };

        var text = IdentityReport.Render(book);
        Assert.True(text.IndexOf("b-id", StringComparison.Ordinal) < text.IndexOf("a-id", StringComparison.Ordinal));
    }

    // ---- stats --------------------------------------------------------------------------------

    [Fact]
    public void The_collection_report_carries_recipes_traits_and_the_dna_space()
    {
        using var book = Book();
        var text = CollectionReport.Render(book);

        Assert.Contains("Recipes:", text);
        Assert.Contains("Traits (overall):", text);
        Assert.Contains("Unique DNA space:", text);
        Assert.Contains("Cat", text);
        Assert.Contains("Daylight", text);
    }

    /// <summary>A report is copied, pasted into an issue and compared against someone else's. A
    /// decimal comma on one machine and a point on another would make two identical collections look
    /// different, so every number is invariant.</summary>
    [Fact]
    public void The_collection_report_does_not_change_with_the_machine_locale()
    {
        using var book = Book();

        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var german = CollectionReport.Render(book);
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var invariant = CollectionReport.Render(book);

            Assert.Equal(invariant, german);
            Assert.DoesNotContain(",00", german);   // the comma-decimal a de-DE format would produce
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    /// <summary>The identity report needs the same guarantee, and did not have it: it was written
    /// beside the collection report and simply interpolated its weights, so under a comma-decimal
    /// locale it printed "(weight 2,5)". Fractional weights on purpose — an integer weight formats
    /// identically everywhere and would make this test pass without the fix.</summary>
    [Fact]
    public void The_identity_report_does_not_change_with_the_machine_locale()
    {
        var bg = Ing("bg-id", "Background", LayerKind.Custom,
            ("day-id", "Daylight", 1.5), ("night-id", "Nightfall", 0.25));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat-id", "Cat", new[] { "bg-id" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { bg },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb-id", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat-id"] = 2.5 }),
            Recipes = new[] { recipe },
        };

        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            var swedish = IdentityReport.Render(book);
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var invariant = IdentityReport.Render(book);

            Assert.Equal(invariant, swedish);
            Assert.Contains("(weight 2.5)", swedish);
            Assert.DoesNotContain("2,5", swedish);
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    /// <summary>Validator problem strings are copied and compared the same way reports are, so the
    /// numbers in them are invariant too. A comma decimal is the visible symptom; the subtler one is
    /// that several locales render the sign as U+2212 MINUS SIGN rather than an ASCII hyphen, which
    /// silently defeats a grep for "-1.5".</summary>
    [Fact]
    public void Validator_problem_numbers_do_not_change_with_the_machine_locale()
    {
        var bg = Ing("bg-id", "Background", LayerKind.Custom, ("a", "A", -1.5), ("b", "B", 2));
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat-id", "Cat", new[] { "bg-id" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { bg },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb-id", "VaporPets", new Dimensions(2, 2),
                new Collection("VaporPets", "d", "VP"),
                new Dictionary<string, double> { ["cat-id"] = 1 }),
            Recipes = new[] { recipe },
        };

        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            var swedish = Validator.Validate(book);
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var invariant = Validator.Validate(book);

            Assert.Equal(invariant, swedish);
            Assert.Contains(swedish, p => p.Contains("(-1.5)"));
            Assert.DoesNotContain(swedish, p => p.Contains('−'));
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    /// <summary>A report that dies on a broken book is useless exactly when it is most wanted, so
    /// the DNA-space line degrades instead of taking the report down. It must degrade to "cannot be
    /// counted" and NOT to "more than 0" — the space is undefined here, and a lower bound of zero
    /// reads like a real answer.</summary>
    [Fact]
    public void An_uncountable_book_still_produces_a_report()
    {
        // Dynamic with no colorization block - the shape UniqueSpace used to dereference.
        var broken = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Dynamic, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new Image<Rgba32>(2, 2) },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(2, 2),
                new Collection("B", "", "B"), new Dictionary<string, double> { ["r"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("r", "R", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { broken },
                },
            },
        };

        var text = CollectionReport.Render(book);   // must not throw

        Assert.Contains("Recipes:", text);
        Assert.Contains("cannot be counted", text);
        Assert.DoesNotContain("more than 0", text);

        // ...and it degrades because Count REPORTS the problem, not because the report catches a
        // throw. Pinning that here is the point: the catch in UniqueDnaLine is now belt-and-braces.
        Assert.False(UniqueSpace.Count(book).IsExact);
        Assert.Equal(0, UniqueSpace.Count(book).Total);
    }
}
