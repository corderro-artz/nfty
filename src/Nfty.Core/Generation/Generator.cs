using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Generation;

public static class Generator
{
    /// <summary>
    /// Generates the whole collection into memory. The returned set owns every asset image —
    /// dispose it when done. For large runs prefer <see cref="GenerateStreaming"/>, which never
    /// holds more than one asset at a time.
    /// </summary>
    public static GeneratedSet Generate(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyList<string>? existingDnas = null,
        int startNumber = 1,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var assets = new List<GeneratedAsset>();
        try
        {
            foreach (var asset in GenerateStreaming(
                         book, opts, existingDnas, startNumber, progress, cancellationToken))
                assets.Add(asset);
        }
        catch
        {
            // Nothing else owns these yet, so a run that fails part-way must not strand them.
            foreach (var asset in assets) asset.Dispose();
            throw;
        }

        return new GeneratedSet(
            book.Manifest.Collection.Name,
            book.Manifest.Collection.Description,
            book.Manifest.Collection.Symbol,
            opts.Seed,
            assets,
            book.SourceSha256);
    }

    /// <summary>
    /// Generation offloaded to a background thread. The work is CPU-bound, so this is a
    /// <see cref="Task.Run(Action)"/> over the sync core rather than genuine async — it exists
    /// so a UI thread stays responsive, and reports through <paramref name="progress"/>.
    /// </summary>
    public static Task<GeneratedSet> GenerateAsync(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyList<string>? existingDnas = null,
        int startNumber = 1,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Generate(book, opts, existingDnas, startNumber, progress, cancellationToken),
            cancellationToken);

    /// <summary>
    /// Yields assets one at a time so a caller can write or draw each and dispose it before the
    /// next is rolled. **The caller owns every asset yielded** — an abandoned enumeration leaves
    /// the last asset undisposed. The cookbook is validated eagerly, before enumeration starts.
    /// </summary>
    public static IEnumerable<GeneratedAsset> GenerateStreaming(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyList<string>? existingDnas = null,
        int startNumber = 1,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var problems = Validator.Validate(book);
        if (problems.Count > 0)
            throw new InvalidOperationException("Invalid cookbook:\n" + string.Join("\n", problems));

        var recipeById = book.Recipes.ToDictionary(r => r.Manifest.Id);
        if (opts.RecipeId is not null && !recipeById.ContainsKey(opts.RecipeId))
            throw new InvalidOperationException($"Recipe '{opts.RecipeId}' not found in cookbook.");

        IReadOnlyDictionary<string, double> recipeWeights =
            opts.RecipeId is null
                ? book.Manifest.RecipeWeights
                : new Dictionary<string, double> { [opts.RecipeId] = 1 };

        return Stream(book, opts, recipeById, recipeWeights, existingDnas, startNumber,
            progress, cancellationToken);
    }

    private static IEnumerable<GeneratedAsset> Stream(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyDictionary<string, LoadedRecipe> recipeById,
        IReadOnlyDictionary<string, double> recipeWeights,
        IReadOnlyList<string>? existingDnas,
        int startNumber,
        IProgress<GenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rng = new SplitMix64Rng(SeedHash.ToUlong(opts.Seed));
        var seen = new HashSet<string>(existingDnas ?? Array.Empty<string>());
        int number = startNumber;

        for (int i = 0; i < opts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GeneratedAsset? asset = null;
            for (int attempt = 0; attempt < opts.MaxRerollsPerAsset; attempt++)
            {
                string recipeId = WeightedRoller.Roll(recipeWeights, rng);
                var candidate = RollOne(book.Manifest.Canvas, recipeById[recipeId], rng, number);
                if (candidate is null) continue;             // rule violation → reroll
                if (seen.Add(candidate.Dna)) { asset = candidate; break; }
                candidate.Dispose();                         // duplicate → discard
            }

            if (asset is null)
                throw DescribeFailure(book, opts, produced: i);

            number++;
            progress?.Report(new GenerationProgress(i + 1, opts.Count));
            yield return asset;
        }
    }

    /// <summary>
    /// Explains a run that could not fill its quota. Counting the space is only worth its cost
    /// on this path, so it happens here rather than up front.
    /// </summary>
    private static InvalidOperationException DescribeFailure(
        LoadedCookBook book, GenerateOptions opts, int produced)
    {
        var space = UniqueSpace.Count(book);
        var inPlay = opts.RecipeId is null
            ? book.Recipes.Select(r => r.Manifest.Id).ToList()
            : new List<string> { opts.RecipeId };

        // Every recipe this run could roll is ruled out entirely: the space is empty, not small.
        // Keyed off legal COMBINATIONS, never the total — a total can also hit zero because a
        // layer has no reachable colour buckets, which is not a rule conflict and must not be
        // reported as one (a rules-free recipe would otherwise be blamed on rules that do not exist).
        var dead = inPlay.Where(id => space.PerRecipeCombos.GetValueOrDefault(id) == 0).ToList();
        if (dead.Count == inPlay.Count && dead.Count > 0)
            return new RuleConflictException(dead,
                $"No legal variant combination exists for {Describe(dead)}: "
                + "the incompatibility rules exclude every combination.");

        long available = 0;
        bool exact = true;
        foreach (var id in inPlay)
        {
            available += space.PerRecipe.GetValueOrDefault(id);
            exact &= space.IsRecipeExact(id);
        }
        if (available >= space.Cap) { available = space.Cap; exact = false; }

        string scope = opts.RecipeId is null ? "this cookbook" : $"recipe '{opts.RecipeId}'";
        string message = exact
            ? $"Could not produce a unique asset: {scope} allows exactly {available} unique DNA, "
              + $"but {opts.Count} were requested ({produced} generated)."
            : $"Could not produce a unique asset after {opts.MaxRerollsPerAsset} attempts "
              + $"({produced} of {opts.Count} generated). {scope} allows more than {available} unique DNA, "
              + "so the reroll budget ran out before the space did.";

        return new UniqueSpaceExhaustedException(available, exact, opts.Count, produced, message);
    }

    private static string Describe(IReadOnlyList<string> recipeIds) =>
        recipeIds.Count == 1
            ? $"recipe '{recipeIds[0]}'"
            : "recipes " + string.Join(", ", recipeIds.Select(r => $"'{r}'"));

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
            switch (ing.Manifest.Kind)
            {
                case LayerKind.Dynamic:
                {
                    // Value-map recolored by a per-asset RNG roll over the layer's colorization.
                    var col = ing.Manifest.Colorization!;
                    var rolled = ColorRoller.Roll(col, rng);
                    colorRolls.Add(new ColorRoll(ingId, LayerKind.Dynamic, col.Model, rolled.H, rolled.S));
                    images.Add(Colorizer.Apply(srcImage, rolled.H, rolled.S, col.Model));
                    dnaParts.Add(new LayerSelection(ingId, variantId, rolled.H, rolled.S, col.HueQuantize, col.SatQuantize));
                    break;
                }
                case LayerKind.Static:
                {
                    // Value-map colorized with exactly one fixed color, resolved WITHOUT consuming RNG.
                    var col = ing.Manifest.Colorization!;
                    var fixedColor = ColorRoller.FromFixed(col.Entries[0].Fixed!, col.Model);
                    colorRolls.Add(new ColorRoll(ingId, LayerKind.Static, col.Model, fixedColor.H, fixedColor.S));
                    images.Add(Colorizer.Apply(srcImage, fixedColor.H, fixedColor.S, col.Model));
                    dnaParts.Add(new LayerSelection(ingId, variantId, fixedColor.H, fixedColor.S, col.HueQuantize, col.SatQuantize));
                    break;
                }
                default: // LayerKind.Custom
                {
                    // Full-color image composited as-is; never colorized.
                    colorRolls.Add(new ColorRoll(ingId, LayerKind.Custom, null, null, null));
                    images.Add(srcImage.Clone());
                    dnaParts.Add(new LayerSelection(ingId, variantId, null, null, 1, 1));
                    break;
                }
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
