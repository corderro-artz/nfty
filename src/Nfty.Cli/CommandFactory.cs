using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Nfty.Cli;

public static class CommandFactory
{
    /// <summary>
    /// Recursive, so it works on any subcommand rather than only before one. Read by
    /// <c>Program</c> when a command throws, to decide whether the trace is wanted.
    /// </summary>
    public static Option<bool> VerboseOption { get; } = new("--verbose")
    {
        Description = "On error, print the full stack trace as well as the message.",
        Recursive = true,
    };

    public static RootCommand Build()
    {
        var root = new RootCommand("nfty — layered NFT asset generator");
        root.Options.Add(VerboseOption);
        root.Subcommands.Add(Inspect());
        root.Subcommands.Add(Validate());
        root.Subcommands.Add(Stats());
        root.Subcommands.Add(Preview());
        root.Subcommands.Add(Generate());
        root.Subcommands.Add(Extend());
        return root;
    }

    private static Command Inspect()
    {
        var path = new Argument<string>("file") { Description = "Path to a .cbk, .rcp or .igt file" };
        var cmd = new Command("inspect", "Print the tree of a cookbook, recipe or ingredient") { path };
        cmd.SetAction(parse =>
        {
            string file = parse.GetValue(path)!;
            switch (Archives.KindOf(file))
            {
                case ArchiveKind.CookBook:
                {
                    using var cb = CookBookArchive.Read(file);
                    PrintCookBook(cb);
                    break;
                }
                case ArchiveKind.Recipe:
                {
                    using var recipe = RecipeArchive.Read(file);
                    PrintRecipe(recipe, weight: null, indent: "");
                    break;
                }
                case ArchiveKind.Ingredient:
                {
                    using var ing = IngredientArchive.Read(file);
                    PrintIngredient(ing, indent: "");
                    break;
                }
            }
            return 0;
        });
        return cmd;
    }

    private static void PrintCookBook(LoadedCookBook cb)
    {
        Console.WriteLine($"CookBook: {cb.Manifest.Name} ({cb.Manifest.Canvas.Width}x{cb.Manifest.Canvas.Height})");
        foreach (var r in cb.Recipes)
            PrintRecipe(r, cb.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id), "  ");
    }

    private static void PrintRecipe(LoadedRecipe recipe, double? weight, string indent)
    {
        string suffix = weight is double w ? $" (weight={w})" : string.Empty;
        Console.WriteLine($"{indent}Recipe: {recipe.Manifest.Name}{suffix}");

        var byId = recipe.Ingredients.ToDictionary(i => i.Manifest.Id);
        foreach (var layerId in recipe.Manifest.LayerOrder)
            PrintIngredient(byId[layerId], indent + "  ");
    }

    private static void PrintIngredient(LoadedIngredient ing, string indent)
    {
        Console.WriteLine($"{indent}Ingredient: {ing.Manifest.Name} [{ing.Manifest.Kind}]");
        foreach (var v in ing.Manifest.Variants)
            Console.WriteLine($"{indent}  Variant: {v.Name} (w={v.Weight})");
    }

    private static Command Validate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var cmd = new Command("validate", "Validate a cookbook") { path };
        cmd.SetAction(parse =>
        {
            using var cb = CookBookArchive.Read(parse.GetValue(path)!);
            var problems = Validator.Validate(cb);
            if (problems.Count == 0) { Console.WriteLine("OK — no problems."); return 0; }
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        });
        return cmd;
    }

    private static Command Stats()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var cmd = new Command("stats", "Show rarity breakdown") { path };
        cmd.SetAction(parse =>
        {
            using var cb = CookBookArchive.Read(parse.GetValue(path)!);
            var report = RarityCalculator.Compute(cb);
            Console.WriteLine("Recipes:");
            foreach (var r in report.Recipes)
                Console.WriteLine($"  {r.RecipeName,-16} {r.Percent,6:0.00}%");
            Console.WriteLine("Traits (overall):");
            foreach (var t in report.Traits)
                Console.WriteLine($"  {t.RecipeName,-12} {t.IngredientName,-14} {t.VariantName,-14} {t.OverallPercent,6:0.00}%");
            return 0;
        });
        return cmd;
    }

    private static Command Preview()
    {
        var path = new Argument<string>("ingredient") { Description = "Path to a .igt layer" };
        var variant = new Option<string>("--variant") { Description = "Variant id to render", Required = true };
        var color = new Option<string>("--color") { Description = "Color spec, e.g. hsv:200,70,80", Required = true };
        var model = new Option<string>("--model") { Description = "hsv or hsl", DefaultValueFactory = _ => "hsv" };
        var outp = new Option<string>("--out") { Description = "Output PNG path", DefaultValueFactory = _ => "preview.png" };
        var cmd = new Command("preview", "Render a value-map variant with a chosen color") { path, variant, color, model, outp };
        cmd.SetAction(parse =>
        {
            using var ing = IngredientArchive.Read(parse.GetValue(path)!);
            string outPath = parse.GetValue(outp)!;
            var image = ing.VariantImages[parse.GetValue(variant)!];
            var m = parse.GetValue(model)!.Equals("hsl", StringComparison.OrdinalIgnoreCase)
                ? ColorModel.Hsl : ColorModel.Hsv;

            // The same spec→(H,S) resolution a static layer gets, so a preview shows exactly
            // what generation would render rather than a second, drifting implementation.
            var (h, s) = ColorRoller.FromFixed(parse.GetValue(color)!, m);

            using var img = Colorizer.Apply(image, h, s, m);
            img.Save(outPath, new PngEncoder());
            Console.WriteLine($"Wrote {outPath}");
            return 0;
        });
        return cmd;
    }

    private static Command Generate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var count = new Option<int>("--count") { Description = "How many to generate", Required = true };
        var seed = new Option<string>("--seed") { Description = "RNG seed", DefaultValueFactory = _ => "nfty" };
        var outDir = new Option<string>("--out") { Description = "Output directory", Required = true };
        var pack = new Option<bool>("--pack") { Description = "Also produce a .set archive" };
        var recipe = new Option<string?>("--recipe") { Description = "Restrict to a single recipe id" };
        var cmd = new Command("generate", "Generate a set") { path, count, seed, outDir, pack, recipe };
        cmd.SetAction(parse =>
        {
            using var book = CookBookArchive.Read(parse.GetValue(path)!);
            string dir = parse.GetValue(outDir)!;
            var opts = new GenerateOptions(parse.GetValue(count), parse.GetValue(seed)!, parse.GetValue(recipe));
            using var set = Generator.Generate(book, opts);
            SetWriter.Write(set, dir, parse.GetValue(pack));
            Console.WriteLine($"Generated {set.Assets.Count} → {dir}");
            return 0;
        });
        return cmd;
    }

    private static Command Extend()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var dir = new Argument<string>("set-dir") { Description = "Existing set directory" };
        var to = new Option<int>("--to") { Description = "Target total count", Required = true };
        var seed = new Option<string>("--seed") { Description = "RNG seed", DefaultValueFactory = _ => "nfty-extend" };
        var cmd = new Command("extend", "Grow an existing set to a new count") { path, dir, to, seed };
        cmd.SetAction(parse =>
        {
            using var book = CookBookArchive.Read(parse.GetValue(path)!);
            string setDir = parse.GetValue(dir)!;
            int target = parse.GetValue(to);

            var existing = SetWriter.ReadExisting(setDir);
            int have = existing.NextNumber - 1;
            int need = target - have;
            if (need <= 0) { Console.WriteLine($"Already at {have}."); return 0; }

            using var more = Generator.Generate(book, new GenerateOptions(need, parse.GetValue(seed)!),
                existing.Dnas, existing.NextNumber);
            SetWriter.Write(more, setDir, pack: false);
            Console.WriteLine($"Extended by {need} → {target} total.");
            return 0;
        });
        return cmd;
    }
}
