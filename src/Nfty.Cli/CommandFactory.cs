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
    public static RootCommand Build()
    {
        var root = new RootCommand("nfty — layered NFT asset generator");
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
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var cmd = new Command("inspect", "Print the tree of a cookbook") { path };
        cmd.SetAction(parse =>
        {
            var cb = CookBookArchive.Read(parse.GetValue(path)!);
            Console.WriteLine($"CookBook: {cb.Manifest.Name} ({cb.Manifest.Canvas.Width}x{cb.Manifest.Canvas.Height})");
            foreach (var r in cb.Recipes)
            {
                double w = cb.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id);
                Console.WriteLine($"  Recipe: {r.Manifest.Name} (weight={w})");
                var byId = r.Ingredients.ToDictionary(i => i.Manifest.Id);
                foreach (var layerId in r.Manifest.LayerOrder)
                {
                    var ing = byId[layerId];
                    Console.WriteLine($"    Ingredient: {ing.Manifest.Name} [{ing.Manifest.Kind}]");
                    foreach (var v in ing.Manifest.Variants)
                        Console.WriteLine($"      Variant: {v.Name} (w={v.Weight})");
                }
            }
            return 0;
        });
        return cmd;
    }

    private static Command Validate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file" };
        var cmd = new Command("validate", "Validate a cookbook") { path };
        cmd.SetAction(parse =>
        {
            var problems = Validator.Validate(CookBookArchive.Read(parse.GetValue(path)!));
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
            var report = RarityCalculator.Compute(CookBookArchive.Read(parse.GetValue(path)!));
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
            var ing = IngredientArchive.Read(parse.GetValue(path)!);
            var image = ing.VariantImages[parse.GetValue(variant)!];
            var rgb = ColorSpec.Parse(parse.GetValue(color)!);
            var m = parse.GetValue(model)!.Equals("hsl", StringComparison.OrdinalIgnoreCase)
                ? ColorModel.Hsl : ColorModel.Hsv;
            var (h, s) = m == ColorModel.Hsv
                ? (ColorConvert.RgbToHsv(rgb).H, ColorConvert.RgbToHsv(rgb).S)
                : (ColorConvert.RgbToHsl(rgb).H, ColorConvert.RgbToHsl(rgb).S);
            using var img = Colorizer.Apply(image, h, s, m);
            img.Save(parse.GetValue(outp)!, new PngEncoder());
            Console.WriteLine($"Wrote {parse.GetValue(outp)}");
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
            var book = CookBookArchive.Read(parse.GetValue(path)!);
            var opts = new GenerateOptions(parse.GetValue(count), parse.GetValue(seed)!, parse.GetValue(recipe));
            var set = Generator.Generate(book, opts);
            SetWriter.Write(set, parse.GetValue(outDir)!, parse.GetValue(pack));
            Console.WriteLine($"Generated {set.Assets.Count} → {parse.GetValue(outDir)}");
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
            var book = CookBookArchive.Read(parse.GetValue(path)!);
            var existing = SetWriter.ReadExisting(parse.GetValue(dir)!);
            int have = existing.NextNumber - 1;
            int need = parse.GetValue(to) - have;
            if (need <= 0) { Console.WriteLine($"Already at {have}."); return 0; }
            var more = Generator.Generate(book, new GenerateOptions(need, parse.GetValue(seed)!),
                existing.Dnas, existing.NextNumber);
            SetWriter.Write(more, parse.GetValue(dir)!, pack: false);
            Console.WriteLine($"Extended by {need} → {parse.GetValue(to)} total.");
            return 0;
        });
        return cmd;
    }
}
