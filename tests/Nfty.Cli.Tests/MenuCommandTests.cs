using System.CommandLine;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// The three new command surfaces over a <c>.mnu</c> Menu: <c>menu</c> writes one from a CookBook,
/// <c>serve</c> rolls or renders one asset from it through Core's own render, and <c>inspect</c>
/// reads its tree.
/// </summary>
public class MenuCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    // The brief's own draft calls a `CommandFactory.Create()` that does not exist anywhere in this
    // repo — every existing test in this assembly builds the root command with `Build()`. Matched
    // to the real API rather than the brief's copy of it, per the task's own instruction to follow
    // "the existing test file style in tests/Nfty.Cli.Tests/ for how CommandFactory is invoked."
    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static (int code, string outText, string errText) Capture(params string[] args)
    {
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        using var outW = new StringWriter();
        using var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        int code;
        try { code = Run(args); }
        finally { Console.SetOut(prevOut); Console.SetError(prevErr); }
        return (code, outW.ToString(), errW.ToString());
    }

    // --- parsing (from the brief) ---

    [Fact]
    public void The_menu_command_parses()
    {
        var result = CommandFactory.Build().Parse("menu book.cbk --out game.mnu --art loose");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void An_unknown_art_mode_is_refused()
    {
        var result = CommandFactory.Build().Parse("menu book.cbk --out game.mnu --art sideways");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Serve_takes_a_seed_or_a_ticket_but_not_both()
    {
        // The same rule `preview` keeps: an option belonging to the other form is REJECTED, never
        // ignored, because an option that silently does nothing is worse than one that says no.
        Assert.Empty(CommandFactory.Build().Parse("serve game.mnu --seed abc --out a.png").Errors);
        Assert.Empty(CommandFactory.Build().Parse("serve game.mnu --ticket v1|x --out a.png").Errors);
        Assert.NotEmpty(CommandFactory.Build()
            .Parse("serve game.mnu --seed abc --ticket v1|x --out a.png").Errors);
        Assert.NotEmpty(CommandFactory.Build().Parse("serve game.mnu --out a.png").Errors);
    }

    // --- fixture: a small CookBook with a Dynamic layer and a Custom layer, so a render exercises
    // both the colorize path and the as-is path in one asset. ---

    private static string WriteCookBook(string dir)
    {
        using var glow = new Image<Rgba32>(2, 2, new Rgba32(200, 200, 200, 255));
        using var spark = new Image<Rgba32>(2, 2, new Rgba32(220, 220, 220, 255));

        // "mark" used to be an OPAQUE fill over the whole 2x2 canvas, which made the composite
        // always equal to badge's own pixels regardless of what aura rolled — a colorize bug in
        // aura (wrong hue, wrong quantize, wrong model) could not be SEEN by any test built on
        // this fixture. Only the top-left texel carries mark's color; the rest are fully
        // transparent, so the colorized aura layer beneath shows through the other three texels
        // and a wrong hue there changes the composite's pixels.
        using var mark = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 0));
        mark[0, 0] = new Rgba32(10, 20, 30, 255);

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
        var badge = new LoadedIngredient
        {
            Manifest = new IngredientManifest(
                Id: "badge",
                Name: "Badge",
                Kind: LayerKind.Custom,
                Colorization: null,
                Variants: [new Variant("mark", "Mark", Weight: 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["mark"] = mark },
        };

        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", LayerOrder: ["aura", "badge"], Rules: []),
            Ingredients = [aura, badge],
        };

        var path = Path.Combine(dir, "book.cbk");
        var cookbook = new CookBookManifest(
            Id: "cb",
            Name: "VaporPets",
            Canvas: new Dimensions(2, 2),
            Collection: new Collection("VaporPets", "test fixture", "VP"),
            RecipeWeights: new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, cookbook, [recipe]);
        return path;
    }

    // --- menu writes a readable .mnu ---

    [Theory]
    [InlineData("atlas")]
    [InlineData("loose")]
    public void Menu_writes_a_dot_mnu_that_inspect_can_read(string art)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBook(dir);
            var mnu = Path.Combine(dir, "game.mnu");

            var (code, text, _) = Capture("menu", cbk, "--out", mnu, "--art", art);

            Assert.Equal(0, code);
            Assert.True(File.Exists(mnu));
            Assert.Contains(mnu, text, StringComparison.Ordinal);

            var (inspectCode, inspectText, _) = Capture("inspect", mnu);
            Assert.Equal(0, inspectCode);
            Assert.Contains("Menu:", inspectText, StringComparison.Ordinal);
            Assert.Contains("[cat]", inspectText, StringComparison.Ordinal);
            Assert.Contains("[aura]", inspectText, StringComparison.Ordinal);
            Assert.Contains("[badge]", inspectText, StringComparison.Ordinal);
            Assert.Contains(art, inspectText, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Inspect_voxel_on_a_menu_is_refused_and_points_to_the_source_cookbook()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBook(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            var ex = Assert.Throws<InvalidOperationException>(
                () => CommandFactory.Build().Parse(["inspect", mnu, "--voxel"]).Invoke(NonThrowing));

            // Substantive rather than a bare "does the word 'Menu' appear" check (that passed even
            // against a wrong, category-framed message a reviewer swapped in) — this pins the one
            // thing amendment D actually requires: the refusal redirects the reader to the source
            // CookBook, not merely that it mentions the word "Menu" somewhere.
            Assert.Contains("CookBook that produced this Menu", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- serve renders through Core's own Colorizer + Compositor, never a second renderer ---

    [Fact]
    public void Serve_with_a_seed_reproduces_generates_first_asset_pixel_for_pixel()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBook(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            const string seed = "hand-run-seed";

            // Compared against `generate --count 1`'s own PNG bytes — the actual CLI command a
            // cook runs — rather than an in-process Generator.Generate call, so this proves serve
            // and generate are the same product's two front doors rather than two renderers that
            // happen to agree today.
            var outDir = Path.Combine(dir, "cooked");
            Assert.Equal(0, Run("generate", cbk, "--count", "1", "--seed", seed, "--out", outDir));
            var expectedBytes = File.ReadAllBytes(Path.Combine(outDir, "images", "0001.png"));

            var png = Path.Combine(dir, "asset.png");
            var (code, text, _) = Capture("serve", mnu, "--seed", seed, "--out", png);

            Assert.Equal(0, code);
            Assert.True(File.Exists(png));

            var servedBytes = File.ReadAllBytes(png);
            Assert.Equal(expectedBytes, servedBytes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Serve_with_a_ticket_renders_the_same_asset_the_ticket_names()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBook(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            using var menu = MenuArchive.Open(mnu);
            var prepared = Ticketer.Prepare(menu.Book);
            var rng = new SplitMix64Rng(SeedHash.ToUlong("ticket-seed"));
            AssetTicket? ticket = null;
            for (int i = 0; i < 100 && ticket is null; i++) ticket = Ticketer.TryRoll(prepared, rng);
            Assert.NotNull(ticket);
            string wire = ticket!.ToWire();

            var pngFromSeed = Path.Combine(dir, "from-seed.png");
            var pngFromTicket = Path.Combine(dir, "from-ticket.png");

            // Re-rolling from the SAME seed reproduces the same ticket, so rendering that ticket
            // directly must produce byte-identical pixels to rolling it fresh.
            Run("serve", mnu, "--seed", "ticket-seed", "--out", pngFromSeed);
            Run("serve", mnu, "--ticket", wire, "--out", pngFromTicket);

            Assert.Equal(File.ReadAllBytes(pngFromSeed), File.ReadAllBytes(pngFromTicket));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tampered_ticket_is_refused_by_serve()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBook(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            Assert.Throws<FormatException>(() => CommandFactory.Build()
                .Parse(["serve", mnu, "--ticket", "v1|cat|not-real", "--out", Path.Combine(dir, "x.png")])
                .Invoke(NonThrowing));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Max_rerolls_is_refused_alongside_a_ticket()
    {
        // --max-rerolls governs a ROLL, and --ticket renders a selection that was already made —
        // there is nothing left to reroll, so this is refused rather than silently ignored, the
        // same rule the seed/ticket split above already keeps.
        var result = CommandFactory.Build()
            .Parse("serve game.mnu --ticket v1|x --max-rerolls 5 --out a.png");
        Assert.NotEmpty(result.Errors);
    }

    // --- fixture: a rule tight enough that roughly half of all rolls are illegal, so the
    // reroll-on-rule-violation loop is exercised for real rather than trivially passing on the
    // first attempt of every seed. ---

    private static string WriteCookBookWithTightRule(string dir)
    {
        using var glow = new Image<Rgba32>(2, 2, new Rgba32(200, 200, 200, 255));
        using var spark = new Image<Rgba32>(2, 2, new Rgba32(220, 220, 220, 255));
        using var mark = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255));

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
        var badge = new LoadedIngredient
        {
            Manifest = new IngredientManifest(
                Id: "badge",
                Name: "Badge",
                Kind: LayerKind.Custom,
                Colorization: null,
                Variants: [new Variant("mark", "Mark", Weight: 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["mark"] = mark },
        };

        var recipe = new LoadedRecipe
        {
            // "badge" has exactly one variant, "mark", so it is what every roll of that layer
            // produces. An Exclude rule forbidding badge:mark whenever aura rolls "glow" therefore
            // rejects every asset that rolls aura=glow outright — with the two aura variants
            // equally weighted, about half the roll space is illegal, which is tight enough to
            // force a reroll on most seeds without making every seed unrollable.
            Manifest = new RecipeManifest("cat", "Cat", LayerOrder: ["aura", "badge"],
                Rules: [new IncompatibilityRule(RuleType.Exclude, new RuleTarget("aura", "glow"),
                    [new RuleTarget("badge", "mark")])]),
            Ingredients = [aura, badge],
        };

        var path = Path.Combine(dir, "book.cbk");
        var cookbook = new CookBookManifest(
            Id: "cb",
            Name: "VaporPets",
            Canvas: new Dimensions(2, 2),
            Collection: new Collection("VaporPets", "test fixture", "VP"),
            RecipeWeights: new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, cookbook, [recipe]);
        return path;
    }

    /// <summary>
    /// A real, reproducible seed for which the FIRST roll against <paramref name="menu"/> is
    /// illegal (a rule violation) and the SECOND succeeds — found by trying candidates against the
    /// same <see cref="Ticketer"/> primitive <c>serve</c> itself rolls through, rather than guessed
    /// or asserted probabilistically. This is what lets the tests below force a real reroll
    /// deterministically instead of relying on chance.
    /// </summary>
    private static string FindSeedNeedingOneReroll(MenuArchive menu)
    {
        var prepared = Ticketer.Prepare(menu.Book);
        for (int i = 0; i < 10_000; i++)
        {
            string candidate = $"reroll-{i}";
            var rng = new SplitMix64Rng(SeedHash.ToUlong(candidate));
            if (Ticketer.TryRoll(prepared, rng) is null && Ticketer.TryRoll(prepared, rng) is not null)
                return candidate;
        }
        throw new InvalidOperationException("No seed needing exactly one reroll found in 10,000 candidates.");
    }

    [Fact]
    public void Serve_still_succeeds_when_the_first_roll_is_illegal_and_a_reroll_is_needed()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBookWithTightRule(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            string seed;
            using (var menu = MenuArchive.Open(mnu)) seed = FindSeedNeedingOneReroll(menu);

            var png = Path.Combine(dir, "asset.png");
            var (code, _, _) = Capture("serve", mnu, "--seed", seed, "--out", png);

            Assert.Equal(0, code);
            Assert.True(File.Exists(png));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_impossibly_low_max_rerolls_fails_and_names_the_rules()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBookWithTightRule(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            string seed;
            using (var menu = MenuArchive.Open(mnu)) seed = FindSeedNeedingOneReroll(menu);

            // 1 attempt is exactly what this seed's FIRST roll cannot satisfy (that is how the
            // seed was found), so this must fail — never succeed by luck — and name the rules.
            var ex = Assert.Throws<InvalidOperationException>(() => CommandFactory.Build()
                .Parse(["serve", mnu, "--seed", seed, "--max-rerolls", "1", "--out", Path.Combine(dir, "x.png")])
                .Invoke(NonThrowing));

            Assert.Contains("incompatibility rules", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- fixture: absent-percent layers, so PrintMenuLayer's two non-trivial branches (a partial
    // percent, and the exactly-100 "never appears" boundary) are both exercised. ---

    private static string WriteCookBookWithAbsentLayers(string dir)
    {
        using var glow = new Image<Rgba32>(2, 2, new Rgba32(200, 200, 200, 255));
        using var spark = new Image<Rgba32>(2, 2, new Rgba32(220, 220, 220, 255));
        using var mark = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255));

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
        var badge = new LoadedIngredient
        {
            Manifest = new IngredientManifest(
                Id: "badge",
                Name: "Badge",
                Kind: LayerKind.Custom,
                Colorization: null,
                Variants: [new Variant("mark", "Mark", Weight: 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["mark"] = mark },
        };

        var recipe = new LoadedRecipe
        {
            // "badge" at 30 exercises the partial-percent branch ("absent 30%"); "aura" at exactly
            // 100 exercises the boundary ("never appears").
            Manifest = new RecipeManifest("cat", "Cat", LayerOrder: ["aura", "badge"], Rules: [],
                AbsentPercent: new Dictionary<string, double> { ["badge"] = 30, ["aura"] = 100 }),
            Ingredients = [aura, badge],
        };

        var path = Path.Combine(dir, "book.cbk");
        var cookbook = new CookBookManifest(
            Id: "cb",
            Name: "VaporPets",
            Canvas: new Dimensions(2, 2),
            Collection: new Collection("VaporPets", "test fixture", "VP"),
            RecipeWeights: new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, cookbook, [recipe]);
        return path;
    }

    [Fact]
    public void Inspect_prints_a_partial_absent_percent_and_the_never_appears_boundary()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBookWithAbsentLayers(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            var (code, text, _) = Capture("inspect", mnu);

            Assert.Equal(0, code);
            Assert.Contains("absent 30%", text, StringComparison.Ordinal);
            Assert.Contains("never appears", text, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
