using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Generation;

public static class Generator
{
    public static GeneratedSet Generate(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyList<string>? existingDnas = null,
        int startNumber = 1)
    {
        var problems = Validator.Validate(book);
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid cookbook:\n" + string.Join("\n", problems));

        var recipeById = book.Recipes.ToDictionary(r => r.Manifest.Id);
        IReadOnlyDictionary<string, double> recipeWeights =
            opts.RecipeId is null
                ? book.Manifest.RecipeWeights
                : new Dictionary<string, double> { [opts.RecipeId] = 1 };
        if (opts.RecipeId is not null && !recipeById.ContainsKey(opts.RecipeId))
            throw new InvalidOperationException($"Recipe '{opts.RecipeId}' not found in cookbook.");

        var rng = new SplitMix64Rng(SeedHash.ToUlong(opts.Seed));
        var seen = new HashSet<string>(existingDnas ?? Array.Empty<string>());
        var assets = new List<GeneratedAsset>();
        int number = startNumber;

        for (int i = 0; i < opts.Count; i++)
        {
            GeneratedAsset? asset = null;
            for (int attempt = 0; attempt < opts.MaxRerollsPerAsset; attempt++)
            {
                string recipeId = WeightedRoller.Roll(recipeWeights, rng);
                var candidate = RollOne(book.Manifest.Canvas, recipeById[recipeId], rng, number);
                if (candidate is null) continue;             // rule violation → reroll
                if (seen.Add(candidate.Dna)) { asset = candidate; break; }
                candidate.Image.Dispose();                   // duplicate → discard
            }

            if (asset is null)
                throw new InvalidOperationException(
                    $"Could not produce a unique asset after {opts.MaxRerollsPerAsset} attempts; "
                    + $"generated {assets.Count} of {opts.Count}. The unique/legal space is likely exhausted.");

            assets.Add(asset);
            number++;
        }

        return new GeneratedSet(
            book.Manifest.Collection.Name,
            book.Manifest.Collection.Description,
            book.Manifest.Collection.Symbol,
            opts.Seed,
            assets);
    }

    private static GeneratedAsset? RollOne(Dimensions canvas, LoadedRecipe recipe, IRng rng, int number)
    {
        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id);
        var selection = new Dictionary<string, string>();
        var traits = new List<TraitSelection>();
        var colorRolls = new List<ColorRoll>();
        var dnaParts = new List<LayerSelection>();
        var images = new List<Image<Rgba32>>();

        foreach (var ingId in recipe.Manifest.LayerOrder)
        {
            var ing = ingById[ingId];
            var weights = ing.Manifest.Variants.ToDictionary(v => v.Id, v => v.Weight);
            string variantId = WeightedRoller.Roll(weights, rng);
            var variant = ing.Manifest.Variants.First(v => v.Id == variantId);
            selection[ingId] = variantId;
            traits.Add(new TraitSelection(ingId, ing.Manifest.Name, variantId, variant.Name));

            var srcImage = ing.VariantImages[variantId];
            if (ing.Manifest.Kind == LayerKind.Dynamic && ing.Manifest.Colorization is { } col)
            {
                var rolled = ColorRoller.Roll(col, rng);
                colorRolls.Add(new ColorRoll(ingId, col.Model, rolled.H, rolled.S));
                images.Add(Colorizer.Apply(srcImage, rolled.H, rolled.S, col.Model));
                dnaParts.Add(new LayerSelection(ingId, variantId, rolled.H, rolled.S, col.HueQuantize, col.SatQuantize));
            }
            else
            {
                images.Add(srcImage.Clone());
                dnaParts.Add(new LayerSelection(ingId, variantId, null, null, 1, 1));
            }
        }

        if (!RulesEngine.IsLegal(selection, recipe.Manifest.Rules))
        {
            foreach (var img in images) img.Dispose();
            return null;
        }

        var composed = Compositor.Composite(canvas, images);
        foreach (var img in images) img.Dispose();

        return new GeneratedAsset
        {
            SetNumber = number,
            Dna = Dna.Compute(recipe.Manifest.Id, dnaParts),
            RecipeId = recipe.Manifest.Id,
            RecipeName = recipe.Manifest.Name,
            Image = composed,
            Traits = traits,
            ColorRolls = colorRolls,
        };
    }
}
