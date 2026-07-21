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
