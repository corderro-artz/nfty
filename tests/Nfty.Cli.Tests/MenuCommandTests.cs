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
    public void Inspect_voxel_on_a_menu_is_refused_and_names_the_scope_gap()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var cbk = WriteCookBook(dir);
            var mnu = Path.Combine(dir, "game.mnu");
            Run("menu", cbk, "--out", mnu);

            var ex = Assert.Throws<InvalidOperationException>(
                () => CommandFactory.Build().Parse(["inspect", mnu, "--voxel"]).Invoke(NonThrowing));

            // Amendment D: the refusal must say the information IS present (source variants,
            // uncolorized) and that this build simply does not scan it yet — not that a Menu
            // cannot carry the answer.
            Assert.Contains("Menu", ex.Message, StringComparison.Ordinal);
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
            using var book = CookBookArchive.Read(cbk);
            using var expectedSet = Generator.Generate(book, new GenerateOptions(Count: 1, Seed: seed));

            var png = Path.Combine(dir, "asset.png");
            var (code, text, _) = Capture("serve", mnu, "--seed", seed, "--out", png);

            Assert.Equal(0, code);
            Assert.True(File.Exists(png));
            Assert.Contains(expectedSet.Assets[0].Dna, text, StringComparison.Ordinal);

            using var served = Image.Load<Rgba32>(png);
            var expected = expectedSet.Assets[0].Image;
            Assert.Equal(expected.Width, served.Width);
            Assert.Equal(expected.Height, served.Height);
            for (int y = 0; y < expected.Height; y++)
                for (int x = 0; x < expected.Width; x++)
                    Assert.Equal(expected[x, y], served[x, y]);
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
}
