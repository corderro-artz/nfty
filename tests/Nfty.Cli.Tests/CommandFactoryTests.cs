using System.CommandLine;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

public class CommandFactoryTests
{
    [Fact]
    public void Root_has_expected_subcommands()
    {
        var root = CommandFactory.Build();
        var names = root.Subcommands.Select(c => c.Name).ToHashSet();
        foreach (var expected in new[] { "inspect", "validate", "stats", "preview", "generate", "extend", "move" })
            Assert.Contains(expected, names);
    }

    [Fact]
    public void Unknown_command_is_a_parse_error()
    {
        var result = CommandFactory.Build().Parse("bogus-command");
        Assert.NotEmpty(result.Errors);
    }

    // --- preview: --variant still required, --color now conditional (fix for defect 4) ---

    [Fact]
    public void Preview_variant_option_is_still_required()
    {
        var result = CommandFactory.Build().Parse("preview thing.igt --color hex:112233");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Preview_color_option_is_no_longer_required_at_parse_time()
    {
        // Whether --color is actually needed depends on the ingredient's LayerKind, which isn't
        // known until the archive is opened inside the action — so it can no longer be a parser-
        // level Required option. A custom-kind ingredient must be previewable without it.
        var result = CommandFactory.Build().Parse("preview thing.igt --variant glow");
        Assert.Empty(result.Errors);
    }

    // --- preview --model: defaults to the ingredient's own model; override is validated (defect 1) ---

    [Theory]
    [InlineData("hsv")]
    [InlineData("hsl")]
    [InlineData("HSV")]
    [InlineData("HSL")]
    public void Preview_model_option_accepts_hsv_or_hsl_case_insensitively(string value)
    {
        var result = CommandFactory.Build().Parse(
            $"preview thing.igt --variant glow --color hex:112233 --model {value}");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Preview_model_option_rejects_an_unknown_value_instead_of_guessing()
    {
        var result = CommandFactory.Build().Parse(
            "preview thing.igt --variant glow --color hex:112233 --model neon");

        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Message.Contains("--model") && e.Message.Contains("neon"));
    }

    [Fact]
    public void Preview_model_option_is_optional_since_it_now_only_overrides_the_stored_model()
    {
        var result = CommandFactory.Build().Parse("preview thing.igt --variant glow --color hex:112233");
        Assert.Empty(result.Errors);
    }

    // --- inspect: ids now printed alongside names (defect 3) ---

    [Fact]
    public void Inspect_prints_recipe_and_variant_ids_alongside_their_names()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string cookbook = Path.Combine(tmp.FullName, "VaporPets.cbk");
            WriteSelfContainedCookBook(cookbook);

            string output = RunAndCaptureStdout(() =>
                CommandFactory.Build().Parse(["inspect", cookbook]).Invoke(NonThrowingConfig));

            // The recipe declares id "cat"; its aura ingredient's variants declare ids "glow"
            // and "spark" — both are what --recipe / --variant actually take, and neither was
            // printed before the fix.
            Assert.Contains("[cat]", output);
            Assert.Contains("[glow]", output);
            Assert.Contains("[spark]", output);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // --- preview: an unknown --variant id names the ingredient and lists the real ids (defect 2) ---

    [Fact]
    public void Preview_reports_a_named_error_for_an_unknown_variant_instead_of_a_raw_KeyNotFoundException()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ingredient = Path.Combine(tmp.FullName, "aura.igt");
            WriteAuraIngredient(ingredient);

            string outPath = Path.Combine(tmp.FullName, "preview.png");
            var parse = CommandFactory.Build().Parse(
                ["preview", ingredient, "--variant", "no-such-id", "--color", "hex:112233", "--out", outPath]);

            var ex = Assert.Throws<InvalidOperationException>(() => parse.Invoke(NonThrowingConfig));

            Assert.Contains("no-such-id", ex.Message);
            Assert.Contains("Aura", ex.Message);
            Assert.Contains("glow", ex.Message);
            Assert.Contains("spark", ex.Message);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // --- preview: a custom-kind layer renders as-is and never asks for a color (defect 4) ---

    [Fact]
    public void Preview_of_a_custom_layer_needs_no_color_and_is_not_colorized()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string ingredientPath = Path.Combine(tmp.FullName, "backdrop.igt");
            WriteSingleVariantCustomIngredient(ingredientPath);

            string outPath = Path.Combine(tmp.FullName, "preview.png");
            var parse = CommandFactory.Build().Parse(
                ["preview", ingredientPath, "--variant", "only", "--out", outPath]);

            string output = RunAndCaptureStdout(() => parse.Invoke(NonThrowingConfig));

            Assert.True(File.Exists(outPath));
            Assert.Contains("not colorized", output);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // --- generate/extend: --max-rerolls exposes the reroll budget the error messages tell users to raise ---

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Generate_max_rerolls_rejects_a_non_positive_budget(string value)
    {
        var result = CommandFactory.Build().Parse(
            $"generate book.cbk --count 5 --out out --max-rerolls {value}");

        Assert.Contains(result.Errors, e => e.Message.Contains("--max-rerolls"));
    }

    [Fact]
    public void Generate_max_rerolls_accepts_a_positive_budget()
    {
        var result = CommandFactory.Build().Parse(
            "generate book.cbk --count 5 --out out --max-rerolls 500");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Generate_max_rerolls_is_optional_and_defaults_to_the_library_default()
    {
        var result = CommandFactory.Build().Parse("generate book.cbk --count 5 --out out");

        Assert.Empty(result.Errors);
        Assert.Equal(Nfty.Core.Generation.GenerateOptions.DefaultMaxRerolls,
            result.GetValue<int>("--max-rerolls"));
    }

    [Fact]
    public void Extend_accepts_max_rerolls_and_pack()
    {
        var result = CommandFactory.Build().Parse("extend book.cbk out --to 10 --max-rerolls 200 --pack");

        Assert.Empty(result.Errors);
    }

    // --- stats: surfaces the unique-DNA space, otherwise reachable only through a failed generate ---

    [Fact]
    public void Stats_reports_the_unique_dna_space()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string cookbook = Path.Combine(tmp.FullName, "VaporPets.cbk");
            WriteSelfContainedCookBook(cookbook);

            string output = RunAndCaptureStdout(() =>
                CommandFactory.Build().Parse(["stats", cookbook]).Invoke(NonThrowingConfig));

            Assert.Contains("Unique DNA space:", output);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // --- inspect: a dangling layerOrder entry is named, not crashed on ---

    [Fact]
    public void Inspect_names_a_dangling_layerOrder_entry_instead_of_throwing()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            using var glow = new Image<Rgba32>(1, 1, new Rgba32(200, 200, 200, 255));
            var aura = new LoadedIngredient
            {
                Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                    [new Variant("glow", "Glow", 1)]),
                VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = glow },
            };
            // layerOrder names "ghost", which no ingredient supplies — a malformed archive, and
            // exactly the kind of file a user runs inspect on to find out what is wrong.
            var recipe = new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", ["aura", "ghost"], []),
                Ingredients = [aura],
            };
            string rcp = Path.Combine(tmp.FullName, "cat.rcp");
            RecipeArchive.Write(rcp, recipe.Manifest, recipe.Ingredients);

            string output = RunAndCaptureStdout(() =>
                CommandFactory.Build().Parse(["inspect", rcp]).Invoke(NonThrowingConfig));

            Assert.Contains("missing", output);
            Assert.Contains("ghost", output);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // --- preview: the .rcp form, chosen by the file's extension, alongside the .igt one ------------

    /// <summary>
    /// The recipe form rolls every layer's variant from --seed, so the option that names ONE variant
    /// must stop being required — without becoming optional for the .igt form, where an Ingredient
    /// holds several and exactly one has to be picked. Which form applies is the extension's answer.
    /// </summary>
    [Fact]
    public void Preview_of_a_rcp_needs_no_variant_because_the_stack_is_rolled()
    {
        var result = CommandFactory.Build().Parse("preview cat.rcp --seed alpha --out preview.png");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Preview_of_a_rcp_parses_only_and_with_and_with_variant()
    {
        var result = CommandFactory.Build().Parse(
            "preview cat.rcp --seed alpha --only body,shades --with hat.igt --with-variant cap");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Preview_seed_has_a_default_so_the_recipe_form_works_without_one()
    {
        var result = CommandFactory.Build().Parse("preview cat.rcp");

        Assert.Empty(result.Errors);
        Assert.Equal("nfty", result.GetValue<string>("--seed"));
    }

    /// <summary>
    /// Options that belong to the other form are REJECTED rather than ignored. Silently dropping a
    /// flag is how a user comes to believe --color did something to a stack whose colors came from
    /// the seed.
    /// </summary>
    [Theory]
    [InlineData("preview cat.rcp --variant round", "--variant")]
    [InlineData("preview cat.rcp --color hex:112233", "--color")]
    [InlineData("preview cat.rcp --model hsl", "--model")]
    [InlineData("preview thing.igt --variant glow --seed alpha", "--seed")]
    [InlineData("preview thing.igt --variant glow --only body", "--only")]
    [InlineData("preview thing.igt --variant glow --with hat.igt", "--with")]
    public void Preview_rejects_an_option_that_belongs_to_the_other_form(string args, string option)
    {
        var result = CommandFactory.Build().Parse(args);

        Assert.Contains(result.Errors, e => e.Message.Contains(option, StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_rejects_with_variant_when_there_is_no_with_layer_to_name()
    {
        var result = CommandFactory.Build().Parse("preview cat.rcp --with-variant cap");

        Assert.Contains(result.Errors,
            e => e.Message.Contains("--with-variant", StringComparison.Ordinal)
                 && e.Message.Contains("--with", StringComparison.Ordinal));
    }

    /// <summary>
    /// A file that is NEITHER form is told so, once. "Not a .rcp" used to be read as "an .igt", so a
    /// <c>.cbk</c> was told <c>--variant</c> was required — advice that, followed, produced a
    /// different error from a different layer.
    /// </summary>
    [Theory]
    [InlineData("preview book.cbk")]
    [InlineData("preview studio.ktn")]
    [InlineData("preview notes.txt")]
    [InlineData("preview noextension")]
    public void Preview_says_which_two_forms_it_reads_when_given_neither(string args)
    {
        var result = CommandFactory.Build().Parse(args);

        Assert.Contains(result.Errors, e =>
            e.Message.Contains(".igt", StringComparison.Ordinal)
            && e.Message.Contains(".rcp", StringComparison.Ordinal));
        // And NOT the ingredient form's advice, which is what it used to give.
        Assert.DoesNotContain(result.Errors,
            e => e.Message.Contains("--variant is required", StringComparison.Ordinal));
    }

    /// <summary>
    /// An --only id the recipe does not stack must arrive as an nfty error naming the id and listing
    /// the real stack — the usual cause is a display name typed where an id belongs — rather than as
    /// a raw framework exception with a trace.
    /// </summary>
    [Fact]
    public void Preview_reports_a_named_error_for_an_unknown_only_id_and_lists_the_real_layers()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string rcp = Path.Combine(tmp.FullName, "cat.rcp");
            WriteAuraRecipe(rcp);

            var parse = CommandFactory.Build().Parse(
                ["preview", rcp, "--seed", "alpha", "--only", "no-such-layer"]);

            var ex = Assert.Throws<InvalidOperationException>(() => parse.Invoke(NonThrowingConfig));

            Assert.Contains("no-such-layer", ex.Message);
            Assert.Contains("aura", ex.Message);
            Assert.Contains("depth 1", ex.Message);
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // --- move ingredient: the destination flags are mutually exclusive -----------------------------

    [Theory]
    [InlineData("move ingredient cat.rcp --id body --to 1")]
    [InlineData("move ingredient cat.rcp --id body --up")]
    [InlineData("move ingredient cat.rcp --id body --down")]
    public void Move_ingredient_accepts_exactly_one_destination(string args)
    {
        var result = CommandFactory.Build().Parse(args);

        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// Each flag names a different destination depth, so a combination is refused rather than
    /// silently resolved: preferring one would move a layer somewhere the author did not ask for and
    /// then report success.
    /// </summary>
    [Theory]
    [InlineData("move ingredient cat.rcp --id body --up --down")]
    [InlineData("move ingredient cat.rcp --id body --to 2 --up")]
    [InlineData("move ingredient cat.rcp --id body --to 2 --down")]
    [InlineData("move ingredient cat.rcp --id body --to 2 --up --down")]
    public void Move_ingredient_refuses_two_destinations_at_once(string args)
    {
        var result = CommandFactory.Build().Parse(args);

        Assert.Contains(result.Errors, e => e.Message.Contains("cannot be combined", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_ingredient_refuses_no_destination_at_all()
    {
        var result = CommandFactory.Build().Parse("move ingredient cat.rcp --id body");

        Assert.Contains(result.Errors, e => e.Message.Contains("needs a destination", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    public void Move_ingredient_rejects_a_depth_below_one_because_depth_is_1_based(string depth)
    {
        var result = CommandFactory.Build().Parse($"move ingredient cat.rcp --id body --to {depth}");

        Assert.Contains(result.Errors, e => e.Message.Contains("--to", StringComparison.Ordinal));
    }

    [Fact]
    public void Move_ingredient_requires_an_id()
    {
        var result = CommandFactory.Build().Parse("move ingredient cat.rcp --up");

        Assert.NotEmpty(result.Errors);
    }

    private static readonly InvocationConfiguration NonThrowingConfig = new() { EnableDefaultExceptionHandler = false };

    private static string RunAndCaptureStdout(Action act)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { act(); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    /// <summary>
    /// A minimal CookBook (id "cb") with one recipe (id "cat") wrapping one dynamic aura
    /// ingredient (id "aura", variants "glow"/"spark") — just enough for inspect to have a
    /// recipe id and variant ids to print. Built fresh per test instead of reading the shared
    /// tests/fixtures/VaporPets.cbk, so this test's expectations live next to the archive that
    /// produces them rather than coupling to a fixture other tests must not change.
    /// </summary>
    private static void WriteSelfContainedCookBook(string path)
    {
        using var glow = new Image<Rgba32>(1, 1, new Rgba32(200, 200, 200, 255));
        using var spark = new Image<Rgba32>(1, 1, new Rgba32(220, 220, 220, 255));

        var aura = new LoadedIngredient
        {
            Manifest = new IngredientManifest(
                Id: "aura",
                Name: "Aura",
                Kind: LayerKind.Dynamic,
                Colorization: new Colorization(
                    Model: ColorModel.Hsv,
                    HueQuantize: 12,
                    SatQuantize: 4,
                    Entries: [new ColorEntry(Weight: 1, Range: new ColorRange(0, 360, 40, 100), Fixed: null)]),
                Variants: [new Variant("glow", "Glow", Weight: 1), new Variant("spark", "Spark", Weight: 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = glow, ["spark"] = spark },
        };

        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", LayerOrder: ["aura"], Rules: []),
            Ingredients = [aura],
        };

        var cookbook = new CookBookManifest(
            Id: "cb",
            Name: "VaporPets",
            Canvas: new Dimensions(1, 1),
            Collection: new Collection("VaporPets", "test fixture", "VP"),
            RecipeWeights: new Dictionary<string, double> { ["cat"] = 100 });

        CookBookArchive.Write(path, cookbook, [recipe]);
    }

    /// <summary>
    /// A standalone .igt (id "aura", name "Aura") with variants "glow"/"spark" — the ingredient
    /// half of <see cref="WriteSelfContainedCookBook"/>, built directly so the unknown-variant
    /// test doesn't need a whole CookBook around it.
    /// </summary>
    private static void WriteAuraIngredient(string path)
    {
        var manifest = new IngredientManifest(
            Id: "aura",
            Name: "Aura",
            Kind: LayerKind.Dynamic,
            Colorization: new Colorization(
                Model: ColorModel.Hsv,
                HueQuantize: 12,
                SatQuantize: 4,
                Entries: [new ColorEntry(Weight: 1, Range: new ColorRange(0, 360, 40, 100), Fixed: null)]),
            Variants: [new Variant("glow", "Glow", Weight: 1), new Variant("spark", "Spark", Weight: 1)]);

        using var glow = new Image<Rgba32>(1, 1, new Rgba32(200, 200, 200, 255));
        using var spark = new Image<Rgba32>(1, 1, new Rgba32(220, 220, 220, 255));

        IngredientArchive.Write(path, manifest,
            new Dictionary<string, Image<Rgba32>> { ["glow"] = glow, ["spark"] = spark });
    }

    /// <summary>
    /// A one-layer .rcp (recipe id "cat") wrapping <see cref="WriteAuraIngredient"/>'s aura layer —
    /// enough for the recipe form of preview to have a real stack to name in an error.
    /// </summary>
    private static void WriteAuraRecipe(string path)
    {
        using var glow = new Image<Rgba32>(1, 1, new Rgba32(200, 200, 200, 255));
        using var spark = new Image<Rgba32>(1, 1, new Rgba32(220, 220, 220, 255));

        var aura = new LoadedIngredient
        {
            Manifest = new IngredientManifest(
                Id: "aura",
                Name: "Aura",
                Kind: LayerKind.Dynamic,
                Colorization: new Colorization(
                    Model: ColorModel.Hsv,
                    HueQuantize: 12,
                    SatQuantize: 4,
                    Entries: [new ColorEntry(Weight: 1, Range: new ColorRange(0, 360, 40, 100), Fixed: null)]),
                Variants: [new Variant("glow", "Glow", Weight: 1), new Variant("spark", "Spark", Weight: 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = glow, ["spark"] = spark },
        };

        RecipeArchive.Write(path, new RecipeManifest("cat", "Cat", ["aura"], []), [aura]);
    }

    private static void WriteSingleVariantCustomIngredient(string path)
    {
        var manifest = new IngredientManifest(
            Id: "backdrop",
            Name: "Backdrop",
            Kind: LayerKind.Custom,
            Colorization: null,
            Variants: [new Variant("only", "Only", Weight: 1)]);

        using var image = new Image<Rgba32>(1, 1);
        image[0, 0] = new Rgba32(10, 20, 30, 255);

        IngredientArchive.Write(path, manifest, new Dictionary<string, Image<Rgba32>> { ["only"] = image });
    }
}
