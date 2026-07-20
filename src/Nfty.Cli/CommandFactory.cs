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
        var path = new Argument<string>("file") { Description = "Path to a .cbk, .rcp or .igt file." };
        var cmd = new Command("inspect",
            "Print the tree of a CookBook, Recipe or Ingredient, showing each Recipe's and "
                + "Variant's [id] alongside its name. Those ids — not the display names — are "
                + "what --recipe and --variant expect elsewhere on this command line, so inspect "
                + "is how you find them.")
        { path };
        cmd.SetAction(parse =>
        {
            string file = parse.GetValue(path)!;
            var kind = Archives.KindOf(file);
            switch (kind)
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
                default:
                    // Archives.KindOf already rejects an unknown extension before we get here,
                    // so this only guards against a future ArchiveKind case added without a
                    // matching inspect branch.
                    throw new NotSupportedException($"inspect does not know how to print archive kind '{kind}'.");
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
        Console.WriteLine($"{indent}Recipe: {recipe.Manifest.Name} [{recipe.Manifest.Id}]{suffix}");

        var byId = recipe.Ingredients.ToDictionary(i => i.Manifest.Id);
        foreach (var layerId in recipe.Manifest.LayerOrder)
            PrintIngredient(byId[layerId], indent + "  ");
    }

    private static void PrintIngredient(LoadedIngredient ing, string indent)
    {
        Console.WriteLine($"{indent}Ingredient: {ing.Manifest.Name} [{ing.Manifest.Kind}]");
        foreach (var v in ing.Manifest.Variants)
            Console.WriteLine($"{indent}  Variant: {v.Name} [{v.Id}] (w={v.Weight})");
    }

    private static Command Validate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file." };
        var cmd = new Command("validate",
            "Check a CookBook for problems and report every one found (id collisions, rule "
                + "conflicts, canvas mismatches, etc.) instead of stopping at the first. "
                + "generate runs this same check itself and refuses to run over a broken "
                + "CookBook, so this command exists for humans who want to see what's wrong "
                + "before generating.")
        { path };
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
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file." };
        var cmd = new Command("stats",
            "Show the odds this CookBook's weights imply: percent share per Recipe, and percent "
                + "share per Variant across the whole collection — computed from the CookBook's "
                + "configured weights, not from an actual generated Set.")
        { path };
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
        var path = new Argument<string>("ingredient") { Description = "Path to a .igt file (an Ingredient — one layer's variants)." };
        var variant = new Option<string>("--variant")
        {
            Description = "Variant id to render — an id, not its display name. Run inspect on "
                + "this .igt (or its parent Recipe/CookBook) to list the ids and names side by "
                + "side.",
            Required = true,
        };
        var color = new Option<string?>("--color")
        {
            Description = "Color spec to colorize with. Required for Dynamic and Static "
                + "ingredients; omit it for Custom ingredients, which are full-color already and "
                + "are always rendered as-is, never colorized. Must carry an explicit hex:, rgb:, "
                + "hsl:, or hsv: prefix, e.g. hex:d6249f or hsv:200,70,80 — a missing or "
                + "unrecognized prefix is a validation error, never guessed.",
        };
        var model = new Option<string?>("--model")
        {
            Description = "Override the ingredient's own stored colorization model. Valid "
                + "values: hsv, hsl (case-insensitive). Defaults to whatever the ingredient "
                + "declares, since that is what generation actually uses; pass this only to "
                + "preview the variant as if it had been authored with the other model.",
        };
        model.Validators.Add(result =>
        {
            string? v = result.GetValueOrDefault<string?>();
            if (v is not null
                && !v.Equals("hsv", StringComparison.OrdinalIgnoreCase)
                && !v.Equals("hsl", StringComparison.OrdinalIgnoreCase))
                result.AddError($"--model must be 'hsv' or 'hsl', not '{v}'.");
        });
        var outp = new Option<string>("--out")
        {
            Description = "Output PNG path.",
            DefaultValueFactory = _ => "preview.png",
        };
        var cmd = new Command("preview",
            "Render one Variant of an Ingredient to a PNG, exactly as generation would render "
                + "it: colorized from its value-map for Dynamic/Static ingredients, or passed "
                + "through untouched for Custom ingredients.")
        { path, variant, color, model, outp };
        cmd.SetAction(parse =>
        {
            using var ing = IngredientArchive.Read(parse.GetValue(path)!);
            string variantId = parse.GetValue(variant)!;
            if (!ing.VariantImages.TryGetValue(variantId, out var image))
            {
                string validIds = string.Join(", ", ing.Manifest.Variants.Select(v => v.Id));
                throw new InvalidOperationException(
                    $"Ingredient '{ing.Manifest.Name}' has no variant '{variantId}'. "
                    + $"Valid variant ids: {validIds}.");
            }

            string outPath = parse.GetValue(outp)!;

            if (ing.Manifest.Kind == LayerKind.Custom)
            {
                // Custom layers are full-color RGBA composited as-is and are never colorized —
                // their Colorization is always null. A preview that "shows exactly what
                // generation would render" must do the same: pass the raw variant through
                // rather than applying a color that generation never applies. `image` is owned
                // by `ing` and freed when it's disposed above, so nothing new to dispose here.
                image.Save(outPath, new PngEncoder());
                Console.WriteLine($"Wrote {outPath} (custom layer — rendered as-is, not colorized)");
                return 0;
            }

            string? colorSpec = parse.GetValue(color);
            if (colorSpec is null)
                throw new InvalidOperationException(
                    $"--color is required to preview '{ing.Manifest.Name}': it is a "
                    + $"{ing.Manifest.Kind.ToString().ToLowerInvariant()} layer, colorized from "
                    + "a value-map at generation time.");

            var col = ing.Manifest.Colorization!;
            string? modelOverride = parse.GetValue(model);
            var m = modelOverride is null
                ? col.Model
                : modelOverride.Equals("hsl", StringComparison.OrdinalIgnoreCase) ? ColorModel.Hsl : ColorModel.Hsv;

            // The same spec→(H,S) resolution a static layer gets, so a preview shows exactly
            // what generation would render rather than a second, drifting implementation.
            var (h, s) = ColorRoller.FromFixed(colorSpec, m);

            using var img = Colorizer.Apply(image, h, s, m);
            img.Save(outPath, new PngEncoder());
            Console.WriteLine($"Wrote {outPath}");
            return 0;
        });
        return cmd;
    }

    private static Command Generate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file." };
        var count = new Option<int>("--count") { Description = "Number of assets to generate.", Required = true };
        var seed = new Option<string>("--seed")
        {
            Description = "RNG seed driving generation. The same CookBook plus the same seed "
                + "always produces byte-identical output, including image bytes — pass a "
                + "different seed to get a different roll of the same CookBook.",
            DefaultValueFactory = _ => "nfty",
        };
        var outDir = new Option<string>("--out")
        {
            Description = "Directory to write the generated Set (images + metadata) into.",
            Required = true,
        };
        var pack = new Option<bool>("--pack") { Description = "Also package the output directory into a single .set archive." };
        var recipe = new Option<string?>("--recipe")
        {
            Description = "Restrict generation to one Recipe id (an id, not its display name — "
                + "run inspect on the CookBook to find it), instead of rolling a Recipe per "
                + "asset by the CookBook's own weights.",
        };
        var cmd = new Command("generate", "Generate a new Set of assets from a CookBook.") { path, count, seed, outDir, pack, recipe };
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
        var path = new Argument<string>("cookbook") { Description = "Path to the .cbk file the existing Set was generated from." };
        var dir = new Argument<string>("set-dir") { Description = "Directory of an existing Set, previously written by generate." };
        var to = new Option<int>("--to")
        {
            Description = "Target total asset count for the Set. extend rolls only the new, "
                + "non-colliding assets needed to reach it, then recomputes rarity across the "
                + "whole collection — including rewriting existing assets' rarity field, since "
                + "rarity is collection-wide.",
            Required = true,
        };
        var seed = new Option<string>("--seed")
        {
            Description = "RNG seed for the newly rolled assets. Existing assets and their DNA "
                + "are left untouched, aside from their recomputed rarity.",
            DefaultValueFactory = _ => "nfty-extend",
        };
        var cmd = new Command("extend", "Grow an existing Set to a new total asset count, using the same CookBook.") { path, dir, to, seed };
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
