using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// `add rule` / `remove rule`, end to end on a real archive. These are the first commands that can
/// author a rule at all: before them every `new` and `add` wrote an empty rule list, and the only
/// way a recipe got a rule was unzipping the .rcp and hand-editing the manifest JSON.
/// </summary>
public class RuleCommandsTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    /// <summary>A three-layer .rcp on disk, with no rules, ready to be given some. Three and not
    /// two because a multi-target require needs two real targets that are not on the trigger's
    /// own layer — a rule pointing a layer at itself is refused, correctly.</summary>
    private static string WriteRecipe(string dir)
    {
        LoadedIngredient Ing(string id, params string[] variants) => new()
        {
            Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
                variants.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = variants.ToDictionary(
                v => v, _ => new Image<Rgba32>(4, 4, new Rgba32(10, 20, 30, 255))),
        };

        var ingredients = new[]
        {
            Ing("bg", "day", "night"), Ing("aura", "none", "glow"), Ing("hat", "crown", "bare"),
        };
        var manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura", "hat" },
            Array.Empty<IncompatibilityRule>());

        string path = Path.Combine(dir, "cat.rcp");
        RecipeArchive.Write(path, manifest, ingredients);
        foreach (var i in ingredients) i.Dispose();
        return path;
    }

    [Fact]
    public void Add_rule_writes_a_rule_the_archive_reads_back()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);

            Assert.Equal(0, Run("add", "rule", path,
                "--type", "exclude", "--when", "bg:day", "--then", "aura:none"));

            using var loaded = RecipeArchive.Read(path);
            var rule = Assert.Single(loaded.Manifest.Rules);
            Assert.Equal(RuleType.Exclude, rule.Type);
            Assert.Equal(new RuleTarget("bg", "day"), rule.When);
            Assert.Equal(new RuleTarget("aura", "none"), Assert.Single(rule.Targets));
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void A_require_rule_takes_every_target_it_is_given()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);

            // Repeated --then, because require is a CONJUNCTION: all of them, not one of them.
            Assert.Equal(0, Run("add", "rule", path, "--type", "require", "--when", "bg:night",
                "--then", "aura:glow", "--then", "hat:crown"));

            using var loaded = RecipeArchive.Read(path);
            Assert.Equal(2, loaded.Manifest.Rules[0].Targets.Count);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void A_target_naming_nothing_is_refused_and_nothing_is_written()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);

            // Resolves as a pair, names a layer that does not exist — so RuleEdits lets it through
            // and Validator is what stops it. The refusal has to leave the file alone.
            Assert.Equal(1, Run("add", "rule", path,
                "--type", "exclude", "--when", "bg:day", "--then", "wings:big"));

            using var loaded = RecipeArchive.Read(path);
            Assert.Empty(loaded.Manifest.Rules);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void A_pair_that_contradicts_an_existing_rule_is_refused()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Assert.Equal(0, Run("add", "rule", path,
                "--type", "exclude", "--when", "bg:day", "--then", "aura:glow"));

            // Individually fine; together they make bg:day unrollable. Only Validator can see it,
            // because neither rule is wrong on its own.
            Assert.Equal(1, Run("add", "rule", path,
                "--type", "require", "--when", "bg:day", "--then", "aura:glow"));

            using var loaded = RecipeArchive.Read(path);
            Assert.Single(loaded.Manifest.Rules);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("bgday")]      // no separator
    [InlineData("bg:")]        // no variant
    [InlineData(":day")]       // no layer
    [InlineData("bg:day:x")]   // too many parts
    public void A_malformed_target_says_what_the_shape_should_be(string spec)
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            var ex = Assert.Throws<InvalidOperationException>(() => CommandFactory.Build()
                .Parse(new[] { "add", "rule", path, "--type", "exclude", "--when", spec, "--then", "aura:none" })
                .Invoke(NonThrowing));

            Assert.Contains("layer:variant", ex.Message);
            Assert.Contains("--when", ex.Message);   // says WHICH option was wrong
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Remove_rule_takes_the_one_at_that_position_and_shifts_the_rest_down()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Assert.Equal(0, Run("add", "rule", path, "--type", "exclude", "--when", "bg:day", "--then", "aura:none"));
            Assert.Equal(0, Run("add", "rule", path, "--type", "require", "--when", "bg:night", "--then", "aura:glow"));

            Assert.Equal(0, Run("remove", "rule", path, "--at", "1"));

            using var loaded = RecipeArchive.Read(path);
            var left = Assert.Single(loaded.Manifest.Rules);
            Assert.Equal(RuleType.Require, left.Type);   // the SECOND rule survived, now at position 1
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Removing_a_position_no_rule_lives_at_says_how_many_there_are()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Assert.Equal(0, Run("add", "rule", path, "--type", "exclude", "--when", "bg:day", "--then", "aura:none"));

            // Phrased for the command line, not for a caller: RuleEdits' own guard throws
            // ArgumentOutOfRangeException, whose Message appends "(Parameter 'index')" — and there
            // is no `index` on this command line.
            var ex = Assert.Throws<InvalidOperationException>(() => CommandFactory.Build()
                .Parse(new[] { "remove", "rule", path, "--at", "4" }).Invoke(NonThrowing));
            Assert.Contains("--at 4 is past the end", ex.Message);
            Assert.Contains("1 rule(s)", ex.Message);
            Assert.DoesNotContain("Parameter", ex.Message);

            using var loaded = RecipeArchive.Read(path);
            Assert.Single(loaded.Manifest.Rules);   // and the refusal left the file alone
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Inspect_lists_the_rules_with_the_positions_remove_expects()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            Run("add", "rule", path, "--type", "exclude", "--when", "bg:day", "--then", "aura:none");
            Run("add", "rule", path, "--type", "require", "--when", "bg:night", "--then", "aura:glow");

            var original = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try { Assert.Equal(0, Run("inspect", path)); }
            finally { Console.SetOut(original); }

            var text = sw.ToString();
            // The position is the whole point: it is the only handle `remove rule --at` has, and
            // before this listing existed there was no way to find it short of unzipping the file.
            Assert.Contains("Rule 1: bg:day never with aura:none", text);
            Assert.Contains("Rule 2: bg:night always with aura:glow", text);
        }
        finally { tmp.Delete(recursive: true); }
    }

    [Fact]
    public void Removing_from_a_recipe_with_no_rules_says_so_rather_than_naming_a_position()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteRecipe(tmp.FullName);
            var ex = Assert.Throws<InvalidOperationException>(() => CommandFactory.Build()
                .Parse(new[] { "remove", "rule", path, "--at", "1" }).Invoke(NonThrowing));
            Assert.Contains("no rules to remove", ex.Message);
        }
        finally { tmp.Delete(recursive: true); }
    }
}
