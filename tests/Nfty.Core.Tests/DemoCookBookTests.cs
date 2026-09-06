using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

/// <summary>
/// The built-in demo, checked against what it claims to be.
/// </summary>
/// <remarks>
/// <para>The archive is a build output that is committed — <c>tools/demo/build-demo.py</c> draws the
/// art and drives the CLI's own authoring commands, and the resulting bytes are embedded in this
/// assembly. Nothing in the build regenerates it, so without these tests the script and the shipped
/// file could disagree indefinitely and the only symptom would be a user opening a broken demo. That
/// is the same arrangement, and the same hazard, as <c>Icons.axaml</c> versus <c>assets/icons</c>,
/// which <c>IconSourceTests</c> guards the same way.</para>
///
/// <para>These read the EMBEDDED bytes, never <c>tools/demo</c> or a file beside the test — the
/// thing that ships is the thing under test.</para>
/// </remarks>
public class DemoCookBookTests
{
    private static LoadedCookBook Read()
    {
        // Through a real file, because that is the only way in: CookBookArchive.Read takes a path,
        // and unpacking to a path is what the app itself does with these bytes.
        var dir = Directory.CreateTempSubdirectory().FullName;
        return CookBookArchive.Read(DemoCookBook.WriteTo(dir));
    }

    [Fact]
    public void The_demo_is_embedded_in_the_assembly()
    {
        var bytes = DemoCookBook.Bytes();

        Assert.NotEmpty(bytes);
        // A .cbk is a renamed zip, so the local file header signature is the cheapest proof that
        // what came out of the assembly is an archive rather than, say, a build placeholder.
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, bytes.Take(4));
    }

    [Fact]
    public void The_demo_reads_and_is_valid()
    {
        using var book = Read();

        // Validator REPORTS rather than throws, so an invalid demo would otherwise ship happily and
        // only refuse at the moment the user pressed Cook.
        Assert.Empty(Validator.Validate(book));
        Assert.Equal("Chest Demo", book.Manifest.Name);
        Assert.Equal(DemoCookBook.DisplayName, book.Manifest.Name);
        Assert.Equal(new Dimensions(32, 32), book.Manifest.Canvas);
        Assert.Equal("CHST", book.Manifest.Collection.Symbol);
    }

    [Fact]
    public void The_demo_shows_all_three_layer_kinds_and_two_recipes()
    {
        // This is the whole reason the demo exists rather than a one-recipe hello-world: a reader
        // who opens it should meet every kind of layer nfty has, in one screen.
        using var book = Read();

        Assert.Equal(2, book.Recipes.Count);
        Assert.Equal(new[] { "chest", "strongbox" },
            book.Recipes.Select(r => r.Manifest.Id).Order(StringComparer.Ordinal));
        Assert.NotEqual(book.Manifest.RecipeWeights["chest"], book.Manifest.RecipeWeights["strongbox"]);

        var chest = book.Recipes.Single(r => r.Manifest.Id == "chest");
        var kinds = chest.Ingredients.Select(i => i.Manifest.Kind).ToHashSet();
        Assert.Contains(LayerKind.Dynamic, kinds);
        Assert.Contains(LayerKind.Static, kinds);
        Assert.Contains(LayerKind.Custom, kinds);

        // A Custom layer is composited as-is and MUST carry no colorization; a Dynamic one cannot
        // roll without it. Asserted here because the demo is the file people copy their next book's
        // shape from.
        foreach (var ing in chest.Ingredients)
        {
            if (ing.Manifest.Kind == LayerKind.Custom) Assert.Null(ing.Manifest.Colorization);
            else Assert.NotNull(ing.Manifest.Colorization);
        }
    }

    [Fact]
    public void The_demo_uses_optional_layers_a_weighted_colorization_and_a_rule()
    {
        using var book = Read();
        var chest = book.Recipes.Single(r => r.Manifest.Id == "chest");

        Assert.True(chest.Manifest.HasOptionalLayers);
        Assert.True(chest.Manifest.AbsentPercentOf("glow") > 0);

        // Two weighted bands, not one: "dynamic" has to read as "rolls inside ranges you set",
        // and a single range cannot show that a roll picks a range first.
        var body = chest.Ingredients.Single(i => i.Manifest.Id == "chestbody");
        Assert.Equal(2, body.Manifest.Colorization!.Entries.Count);
        Assert.All(body.Manifest.Colorization.Entries, e => Assert.NotNull(e.Range));

        var rule = Assert.Single(chest.Manifest.Rules);
        Assert.Equal(RuleType.Exclude, rule.Type);
        Assert.Equal(new RuleTarget("chestbody", "stone"), rule.When);
        Assert.Equal(new RuleTarget("lock", "keypad"), Assert.Single(rule.Targets));
    }

    [Fact]
    public void The_demo_dna_space_is_counted_exactly()
    {
        // NOT a check that the number is large - a check that it is KNOWN. UniqueSpace stops
        // enumerating at a million and reports "more than 1000000", and a demo whose headline
        // figure is a floor teaches the reader that nfty cannot count its own space. The quantize
        // steps in build-demo.py are chosen to stay under that cap; this is what notices when a
        // later edit pushes them over it.
        using var book = Read();
        var space = UniqueSpace.Count(book);

        Assert.True(space.IsExact,
            $"the demo's DNA space saturated at {space.Cap:N0}; coarsen a quantize step in tools/demo/build-demo.py");
        Assert.InRange(space.Total, 100_000, UniqueSpace.DefaultCap - 1);
    }

    [Fact]
    public void The_demo_cooks()
    {
        // The end of the line, and the only assertion here that a manifest cannot pass on its own:
        // every variant PNG has to decode, match the canvas, colorize and composite.
        using var book = Read();
        using var set = Generator.Generate(book, new GenerateOptions(Count: 12, Seed: "demo"));

        Assert.Equal(12, set.Assets.Count);
        Assert.Equal(12, set.Assets.Select(a => a.Dna).Distinct(StringComparer.Ordinal).Count());
        Assert.All(set.Assets, a =>
        {
            Assert.Equal(32, a.Image.Width);
            Assert.Equal(32, a.Image.Height);
        });
    }

    [Fact]
    public void Writing_the_demo_twice_keeps_the_first_copy()
    {
        // The demo is for editing. If a second "open the demo" restored the shipped bytes it would
        // silently throw away whatever the user had done in it, which is the one thing a sample
        // collection must not do.
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = DemoCookBook.WriteTo(dir);
        File.WriteAllText(path, "edited");

        Assert.Equal(path, DemoCookBook.WriteTo(dir));
        Assert.Equal("edited", File.ReadAllText(path));

        DemoCookBook.WriteTo(dir, overwrite: true);
        Assert.NotEqual("edited", File.ReadAllText(path));
        Assert.Equal(DemoCookBook.Bytes(), File.ReadAllBytes(path));
    }

    [Fact]
    public void Writing_the_demo_creates_the_folder_and_leaves_no_staging_file()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "not", "there", "yet");

        var path = DemoCookBook.WriteTo(dir);

        Assert.True(File.Exists(path));
        // The write goes through a .tmp and is moved into place, so a leftover would mean the move
        // did not happen - and a half-written .cbk found by the next launch is a demo that is
        // permanently corrupt rather than one that failed once.
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }
}
