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
        // Count is a per-run parameter, not a property of the book, so it is checked here rather
        // than in Validator — but it must still be checked before any rolling/decoding/output
        // work, and before the cookbook is even validated. Non-positive is rejected outright:
        // unlike extend's --to (an absolute target that can legally already be met), Count is a
        // direct request with no benign zero/negative reading, so both fail the same way.
        if (opts.Count <= 0)
            throw new InvalidOperationException(
                $"Asset count must be positive, but {opts.Count} was requested.");

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

        // Resolved once per run, not per roll attempt: draw order, running weight totals, and the
        // per-layer lookups are all fixed properties of the cookbook. Built here — inside the
        // iterator, off local state — so nothing is shared between calls or between enumerations.
        var recipeTable = WeightedRoller.Prepare(recipeWeights);
        var layerPlans = recipeById.ToDictionary(kv => kv.Key, kv => PlanLayers(kv.Value));

        for (int i = 0; i < opts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GeneratedAsset? asset = null;
            for (int attempt = 0; attempt < opts.MaxRerollsPerAsset; attempt++)
            {
                // Checked per ATTEMPT, not just per asset: one asset can burn the whole reroll
                // budget on roll+colorize+composite cycles, so a per-asset check alone lets a
                // cancel sit unobserved for minutes and lose the race to the budget — surfacing
                // as UniqueSpaceExhaustedException instead of OperationCanceledException.
                cancellationToken.ThrowIfCancellationRequested();

                string recipeId = WeightedRoller.Roll(recipeTable, rng);
                var candidate = RollOne(
                    book.Manifest.Canvas, recipeById[recipeId], layerPlans[recipeId], rng, number);
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

        // Only the recipes THIS run can roll, which is not every recipe in the book: a recipe
        // weighted zero is shelved, never rolled, and counting its space would report a maximum
        // the run could never reach. Restricting to one recipe overrides the weights entirely,
        // so that recipe is in play whatever the book weights it.
        var inPlay = opts.RecipeId is null
            ? book.Recipes.Select(r => r.Manifest.Id)
                  .Where(id => book.Manifest.RecipeWeights.GetValueOrDefault(id) > 0)
                  .ToList()
            : new List<string> { opts.RecipeId };

        // Every recipe this run could roll is ruled out entirely: the space is empty, not small.
        // Keyed off legal COMBINATIONS, never the total — a total can also hit zero because a
        // layer has no reachable colour buckets, which is not a rule conflict and must not be
        // reported as one (a rules-free recipe would otherwise be blamed on rules that do not exist).
        var dead = inPlay.Where(id => space[id].Combos == 0).ToList();
        if (dead.Count == inPlay.Count && dead.Count > 0)
            return new RuleConflictException(dead,
                $"No legal variant combination exists for {Describe(dead)}: "
                + "the incompatibility rules exclude every combination.");

        long available = 0;
        bool exact = true;
        foreach (var id in inPlay)
        {
            available += space[id].Total;
            exact &= space[id].IsExact;
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

    /// <summary>
    /// One recipe layer with everything the roll needs already resolved: the ingredient, its
    /// prepared weight table, and its variants by id. All three are fixed properties of the
    /// cookbook, so they are built once per run rather than rebuilt on every roll attempt.
    /// </summary>
    private sealed record LayerPlan(
        LoadedIngredient Ingredient,
        WeightedRoller.WeightTable Weights,
        IReadOnlyDictionary<string, Variant> VariantById);

    /// <summary>
    /// Resolves a recipe's layers in <c>layerOrder</c>. Safe to index and to build dictionaries
    /// from: <see cref="Validator"/> has already rejected unknown or repeated layerOrder entries
    /// and duplicate variant ids before any of this runs.
    /// </summary>
    private static IReadOnlyList<LayerPlan> PlanLayers(LoadedRecipe recipe)
    {
        var ingById = recipe.Ingredients.ToDictionary(i => i.Manifest.Id);
        return recipe.Manifest.LayerOrder
            .Select(id =>
            {
                var ing = ingById[id];
                return new LayerPlan(
                    ing,
                    WeightedRoller.Prepare(ing.Manifest.Variants.ToDictionary(v => v.Id, v => v.Weight)),
                    ing.Manifest.Variants.ToDictionary(v => v.Id));
            })
            .ToList();
    }

    private static GeneratedAsset? RollOne(
        Dimensions canvas,
        LoadedRecipe recipe,
        IReadOnlyList<LayerPlan> layers,
        IRng rng,
        int number)
    {
        var selection = new Dictionary<string, string>();
        var traits = new List<TraitSelection>();
        var colorRolls = new List<ColorRoll>();
        var dnaParts = new List<LayerSelection>();

        // What each layer will draw, decided in the roll phase and rendered in the render phase.
        // A null model means a Custom layer, which is cloned as-is rather than colorized.
        var plan = new List<(Image<Rgba32> Source, RolledColor Color, ColorModel? Model)>();
        var images = new List<Image<Rgba32>>();

        try
        {
            // --- Roll phase: consumes the RNG, touches no pixels. ---
            foreach (var layer in layers)
            {
                var ing = layer.Ingredient;
                string ingId = ing.Manifest.Id;
                string variantId = WeightedRoller.Roll(layer.Weights, rng);
                var variant = layer.VariantById[variantId];
                selection[ingId] = variantId;
                traits.Add(new TraitSelection(ingId, ing.Manifest.Name, variantId, variant.Name));

                var srcImage = ing.VariantImages[variantId];
                var kind = ing.Manifest.Kind;
                switch (kind)
                {
                    case LayerKind.Dynamic or LayerKind.Static:
                    {
                        // Both kinds are grayscale value-maps colorized with an (H,S). The ONLY
                        // difference is where that colour comes from: dynamic rolls it per asset,
                        // static resolves its single fixed colour and consumes NO RNG (spec 5.3).
                        var col = ing.Manifest.Colorization!;
                        var color = kind == LayerKind.Dynamic
                            ? ColorRoller.Roll(col, rng)
                            : ColorRoller.FromFixed(col.Entries[0].Fixed!, col.Model);

                        colorRolls.Add(new ColorRoll(ingId, kind, col.Model, color.H, color.S));
                        plan.Add((srcImage, color, col.Model));
                        dnaParts.Add(new LayerSelection(ingId, variantId, color.H, color.S, col.HueQuantize, col.SatQuantize));
                        break;
                    }
                    default: // LayerKind.Custom
                    {
                        // Full-color image composited as-is; never colorized.
                        colorRolls.Add(new ColorRoll(ingId, LayerKind.Custom, null, null, null));
                        plan.Add((srcImage, default, null));
                        dnaParts.Add(new LayerSelection(ingId, variantId, null, null, 1, 1));
                        break;
                    }
                }
            }

            // Legality is a function of the variant selection alone, so it is decided before any
            // pixel is touched: a roll the rules reject costs only the rolls themselves. The
            // rolls above must stay above this line — they consume the RNG, and the draw order
            // is part of the determinism contract.
            if (!RulesEngine.IsLegal(selection, recipe.Manifest.Rules))
                return null;

            // --- Render phase: no RNG, only pixels. ---
            foreach (var (source, color, model) in plan)
                images.Add(model is null
                    ? source.Clone()
                    : Colorizer.Apply(source, color.H, color.S, model.Value));

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
        catch
        {
            // Colorizer.Apply or Compositor.Composite can throw mid-stack; every layer image
            // already decoded/cloned/colorized into `images` has no other owner yet, so it must
            // be disposed here before the original exception propagates.
            foreach (var img in images) img.Dispose();
            throw;
        }
    }
}
