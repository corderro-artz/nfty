using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>The `new` command group: create an archive from a manifest JSON plus the artifacts
    /// one level down, resolved by the {id} filename convention.</summary>
    public static Command NewGroup()
    {
        var group = new Command("new", "Create a new .igt / .rcp / .cbk from a manifest and its parts.");
        group.Subcommands.Add(NewIngredient());
        group.Subcommands.Add(NewRecipe());
        group.Subcommands.Add(NewCookbook());
        return group;
    }

    private static Command NewIngredient()
    {
        var outPath = new Argument<string>("out") { Description = "Output .igt path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to an IngredientManifest JSON (id, name, kind, colorization, variants).",
            Required = true,
        };
        var images = new Option<string>("--images")
        {
            Description = "Directory of variant PNGs; each variant's image is <dir>/{variantId}.png.",
            Required = true,
        };
        var cmd = new Command("ingredient",
            "Build an .igt from an ingredient manifest and one PNG per variant (named {variantId}.png).")
            { outPath, manifest, images };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<IngredientManifest>(parse.GetValue(manifest)!);
            string imagesDir = parse.GetValue(images)!;
            var loaded = LoadVariantImages(m, imagesDir);
            try
            {
                var ing = new LoadedIngredient { Manifest = m, VariantImages = loaded };
                var problems = Validator.ValidateIngredient(ing);
                if (problems.Count > 0) { Report(problems); return 1; }

                IngredientArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} variants)");
                return 0;
            }
            finally
            {
                // These images have no other owner: the LoadedIngredient above only wraps them.
                foreach (var img in loaded.Values) img.Dispose();
            }
        });
        return cmd;
    }

    private static Command NewRecipe()
    {
        var outPath = new Argument<string>("out") { Description = "Output .rcp path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to a RecipeManifest JSON (id, name, layerOrder, rules).",
            Required = true,
        };
        var ingredients = new Option<string>("--ingredients")
        {
            Description = "Directory of .igt files; each layerOrder id resolves to <dir>/{id}.igt.",
            Required = true,
        };
        var cmd = new Command("recipe",
            "Build a .rcp from a recipe manifest and one .igt per layerOrder id (named {id}.igt).")
            { outPath, manifest, ingredients };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<RecipeManifest>(parse.GetValue(manifest)!);
            string dir = parse.GetValue(ingredients)!;
            var loaded = new List<LoadedIngredient>();
            try
            {
                foreach (var id in m.LayerOrder.Distinct(StringComparer.Ordinal))
                {
                    string igt = Path.Combine(dir, $"{id}.igt");
                    if (!File.Exists(igt))
                        throw new FileNotFoundException($"No ingredient for layer '{id}': expected {igt}", igt);
                    loaded.Add(IngredientArchive.Read(igt));
                }

                var recipe = new LoadedRecipe { Manifest = m, Ingredients = loaded };
                var problems = Validator.ValidateRecipe(recipe);
                if (problems.Count > 0) { Report(problems); return 1; }

                RecipeArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} ingredients)");
                return 0;
            }
            finally
            {
                foreach (var ing in loaded) ing.Dispose();
            }
        });
        return cmd;
    }

    private static Command NewCookbook()
    {
        var outPath = new Argument<string>("out") { Description = "Output .cbk path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to a CookBookManifest JSON (id, name, canvas, collection, recipeWeights).",
            Required = true,
        };
        var recipes = new Option<string>("--recipes")
        {
            Description = "Directory of .rcp files; each recipeWeights key resolves to <dir>/{id}.rcp.",
            Required = true,
        };
        var force = new Option<bool>("--force")
        {
            Description = "Write even if validation reports problems (they are printed as warnings). "
                + "Use only for deliberate work-in-progress; generate will still refuse the book.",
        };
        var cmd = new Command("cookbook",
            "Assemble a .cbk from a cookbook manifest and one .rcp per recipeWeights key (named {id}.rcp).")
            { outPath, manifest, recipes, force };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<CookBookManifest>(parse.GetValue(manifest)!);
            string dir = parse.GetValue(recipes)!;
            var loaded = new List<LoadedRecipe>();
            try
            {
                foreach (var id in m.RecipeWeights.Keys.Distinct(StringComparer.Ordinal))
                {
                    string rcp = Path.Combine(dir, $"{id}.rcp");
                    if (!File.Exists(rcp))
                        throw new FileNotFoundException($"No recipe '{id}': expected {rcp}", rcp);
                    loaded.Add(RecipeArchive.Read(rcp));
                }

                var book = new LoadedCookBook { Manifest = m, Recipes = loaded, SourceSha256 = null };
                var problems = Validator.Validate(book);
                if (problems.Count > 0)
                {
                    Report(problems);
                    if (!parse.GetValue(force)) return 1;
                    Console.Error.WriteLine("--force: writing despite the problems above.");
                }

                CookBookArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} recipes)");
                return 0;
            }
            finally
            {
                foreach (var r in loaded) r.Dispose();
            }
        });
        return cmd;
    }

    /// <summary>The `add` command group: append a single item into an existing archive.</summary>
    public static Command AddGroup()
    {
        var group = new Command("add", "Append a variant / ingredient / recipe to an existing archive.");
        group.Subcommands.Add(AddVariant());
        return group;
    }

    private static Command AddVariant()
    {
        var igtPath = new Argument<string>("igt") { Description = "Path to the .igt to modify in place." };
        var id = new Option<string>("--id") { Description = "New variant id (must be unique in the ingredient).", Required = true };
        var name = new Option<string?>("--name") { Description = "Display name (defaults to the id)." };
        var weight = new Option<double>("--weight") { Description = "Variant weight (zero or greater).", Required = true };
        var image = new Option<string>("--image") { Description = "PNG for this variant.", Required = true };
        var cmd = new Command("variant", "Add one variant (id, weight, image) to an existing .igt.")
            { igtPath, id, name, weight, image };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(igtPath)!;
            string vid = parse.GetValue(id)!;
            using var existing = IngredientArchive.Read(path);
            if (existing.Manifest.Variants.Any(v => string.Equals(v.Id, vid, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Ingredient '{existing.Manifest.Id}' already has a variant '{vid}'.");

            var newImg = Image.Load<Rgba32>(parse.GetValue(image)!);
            try
            {
                var images = new Dictionary<string, Image<Rgba32>>(existing.VariantImages) { [vid] = newImg };
                var variants = existing.Manifest.Variants
                    .Append(new Variant(vid, parse.GetValue(name) ?? vid, parse.GetValue(weight)))
                    .ToList();
                var manifest = existing.Manifest with { Variants = variants };

                var merged = new LoadedIngredient { Manifest = manifest, VariantImages = images };
                var problems = Validator.ValidateIngredient(merged);
                if (problems.Count > 0) { Report(problems); return 1; }

                // Read(path) closed its file handle before returning, so it's safe to replace the
                // file now. IngredientArchive.Write opens in ZipArchiveMode.Create, which throws
                // if the target already exists, so the old file must go first.
                File.Delete(path);
                IngredientArchive.Write(path, manifest, images);
                Console.WriteLine($"Added variant '{vid}' to {path}");
                return 0;
            }
            finally { newImg.Dispose(); }
        });
        return cmd;
    }

    /// <summary>Loads one PNG per distinct variant id, by the {id}.png convention.</summary>
    private static Dictionary<string, Image<Rgba32>> LoadVariantImages(IngredientManifest m, string imagesDir)
    {
        var loaded = new Dictionary<string, Image<Rgba32>>();
        try
        {
            foreach (var v in m.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal))
            {
                string png = Path.Combine(imagesDir, $"{v.Id}.png");
                if (!File.Exists(png))
                    throw new FileNotFoundException(
                        $"No image for variant '{v.Id}': expected {png}", png);
                loaded[v.Id] = Image.Load<Rgba32>(png);
            }
        }
        catch
        {
            foreach (var img in loaded.Values) img.Dispose();
            throw;
        }
        return loaded;
    }

    /// <summary>Prints each validation problem to stderr, as `validate` does.</summary>
    private static void Report(IReadOnlyList<string> problems)
    {
        foreach (var p in problems) Console.Error.WriteLine(p);
    }
}
