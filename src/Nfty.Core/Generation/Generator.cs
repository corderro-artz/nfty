using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Generation;

/// <summary>
/// The generation pipeline. Per asset: roll a recipe, roll each layer's variant, apply the recipe's
/// rules, compute the DNA, reject a duplicate, and only then render — colorize, composite, emit.
///
/// <para>Rolling and rendering are separate deliberately: the DNA is a function of the selection
/// alone, so a collision is discarded without paying for the pixels that would have been thrown
/// away with it.</para>
/// </summary>
public static class Generator
{
    /// <summary>
    /// Generates the whole collection into memory. The returned set owns every asset image —
    /// dispose it when done. For large runs prefer <see cref="GenerateStreaming"/>, which never
    /// holds more than one asset at a time.
    /// </summary>
    /// <remarks>
    /// <para><b>Two phases, and only the second is parallel.</b> Rolling consumes the RNG and does
    /// the duplicate check, and both depend on the exact order every asset is decided in — so the
    /// roll phase is sequential, always, and produces the identical stream of decisions this method
    /// has always produced. Rendering is then a pure function of one asset's own already-decided
    /// roll: colorize each layer, composite them, done. Nothing it does can observe another asset,
    /// the thread it runs on, or how many threads there are.</para>
    ///
    /// <para>That is what makes this safe for reproducibility, and it is a stronger claim than
    /// "same machine": results cannot vary with core count or scheduling because no parallel step
    /// aggregates anything. There is no summing, no ordering, no shared accumulator — each asset is
    /// written to its own slot in a pre-sized array, so the collection comes out in roll order on
    /// any machine. <c>GeneratorParallelismTests</c> asserts this against
    /// <see cref="GenerateStreaming"/>, which is the sequential oracle: same seed, byte-identical
    /// pixels, identical DNA, identical order.</para>
    /// </remarks>
    /// <inheritdoc cref="Generate(LoadedCookBook, GenerateOptions, IReadOnlyList{string}, int, IProgress{GenerationProgress}, CancellationToken)" path="/summary"/>
    public static GeneratedSet Generate(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyList<string>? existingDnas = null,
        int startNumber = 1,
        IProgress<GenerationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var (recipeById, recipeWeights) = Prepare(book, opts);
        var rolls = RollAll(book, opts, recipeById, recipeWeights, existingDnas, startNumber,
            cancellationToken);

        var canvas = book.Manifest.Canvas;
        var assets = new GeneratedAsset[rolls.Count];
        var progressGate = new Lock();
        int done = 0;
        try
        {
            ParallelWork.For(rolls.Count, cancellationToken, i =>
            {
                assets[i] = Render(canvas, rolls[i].Rolled, rolls[i].Number);

                // A caller's IProgress handler is still invoked ONE AT A TIME, in increasing order,
                // exactly as it was when this loop was sequential. That is a contract, not a
                // nicety: handlers here append to plain lists and update UI state, and calling them
                // concurrently would corrupt both. Counting and reporting happen together inside
                // the lock, or two threads could take numbers 1 and 2 and report them in the other
                // order. The lock is negligible beside a colorize-and-composite.
                if (progress is not null)
                    lock (progressGate)
                        progress.Report(new GenerationProgress(++done, opts.Count));
            });
        }
        catch
        {
            foreach (var a in assets) a?.Dispose();   // nothing else owns these yet
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
        var (recipeById, recipeWeights) = Prepare(book, opts);
        return Stream(book, opts, recipeById, recipeWeights, existingDnas, startNumber,
            progress, cancellationToken);
    }

    /// <summary>
    /// The checks and lookups both entry points share, run eagerly before any work.
    /// </summary>
    /// <param name="book">The cookbook.</param>
    /// <param name="opts">The run's options.</param>
    /// <returns>The recipes by id, and the weights this run rolls them by.</returns>
    private static (IReadOnlyDictionary<string, LoadedRecipe> ById,
                    IReadOnlyDictionary<string, double> Weights)
        Prepare(LoadedCookBook book, GenerateOptions opts)
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

        return (recipeById, recipeWeights);
    }

    /// <summary>
    /// The roll phase for the whole run, in order, on one thread.
    /// </summary>
    /// <remarks>
    /// This is the half that can never be parallel, and the reason is not performance: it consumes
    /// a single RNG stream and rejects duplicates against a running set, so the Nth asset's
    /// decisions depend on every decision before it. Splitting it would change the collection.
    /// It touches no pixels, which is why doing it all up front is cheap.
    /// </remarks>
    private static List<(RolledAsset Rolled, int Number)> RollAll(
        LoadedCookBook book,
        GenerateOptions opts,
        IReadOnlyDictionary<string, LoadedRecipe> recipeById,
        IReadOnlyDictionary<string, double> recipeWeights,
        IReadOnlyList<string>? existingDnas,
        int startNumber,
        CancellationToken cancellationToken)
    {
        var rng = new SplitMix64Rng(SeedHash.ToUlong(opts.Seed));
        var seen = new HashSet<string>(existingDnas ?? Array.Empty<string>());
        var recipeTable = WeightedRoller.Prepare(recipeWeights);
        var layerPlans = recipeById.ToDictionary(kv => kv.Key, kv => PlanLayers(kv.Value));

        var rolls = new List<(RolledAsset, int)>(opts.Count);
        int number = startNumber;

        for (int i = 0; i < opts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RolledAsset? rolled = null;
            for (int attempt = 0; attempt < opts.MaxRerollsPerAsset; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string recipeId = WeightedRoller.Roll(recipeTable, rng);
                var candidate = RollOne(recipeById[recipeId], layerPlans[recipeId], rng);
                if (candidate is null) continue;                       // rule violation → reroll
                if (opts.EnforceUniqueDna && !seen.Add(candidate.Dna)) continue;

                rolled = candidate;
                break;
            }

            if (rolled is null) throw DescribeFailure(book, opts, produced: i);

            rolls.Add((rolled, number));
            number++;
        }

        return rolls;
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
                var rolled = RollOne(recipeById[recipeId], layerPlans[recipeId], rng);
                if (rolled is null) continue;                // rule violation → reroll

                // Dedup BEFORE rendering. The DNA is a function of the selection and the rolled
                // colors, all of which the roll phase already has, so a collision can be detected
                // without touching a pixel. Rendering first meant every duplicate threw away a
                // fully colorized and composited canvas — and minting near the top of the unique
                // space is the normal case, where coupon-collector says most attempts collide. The
                // file already applied exactly this reasoning to the rules check one phase earlier.
                //
                // With uniqueness off, a repeated DNA is accepted: identity comes from the token id,
                // not the DNA, so there is nothing to dedup and nothing to exhaust.
                if (opts.EnforceUniqueDna && !seen.Add(rolled.Dna)) continue;

                asset = Render(book.Manifest.Canvas, rolled, number);
                break;
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
        // layer has no reachable color buckets, which is not a rule conflict and must not be
        // reported as one (a rules-free recipe would otherwise be blamed on rules that do not exist).
        var dead = inPlay.Where(id => space[id].Combos == 0).ToList();
        if (dead.Count == inPlay.Count && dead.Count > 0)
            return new RuleConflictException(dead,
                $"No legal variant combination exists for {Describe(dead)}: "
                + "the incompatibility rules exclude every combination.");

        // With uniqueness off, a repeated DNA is never rejected, so exhausting the reroll budget
        // can only mean the incompatibility rules rejected every roll — the unique space is not the
        // constraint and must not be blamed. (An empty space was already caught as a rule conflict.)
        if (!opts.EnforceUniqueDna)
        {
            string ruleScope = opts.RecipeId is null ? "this cookbook" : $"recipe '{opts.RecipeId}'";
            return new InvalidOperationException(
                $"Could not produce a legal asset for {ruleScope} after {opts.MaxRerollsPerAsset} "
                + $"attempts ({produced} of {opts.Count} generated): the incompatibility rules "
                + "rejected every roll. Loosen the rules or raise the reroll budget.");
        }

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
    /// <param name="Ingredient">The layer.</param>
    /// <param name="Weights">Its prepared variant weights.</param>
    /// <param name="VariantById">Its variants by id.</param>
    /// <param name="AbsentWeight">Weight of leaving the layer out, resolved from the recipe's
    /// percent ONCE per run rather than per roll — it is a property of the cookbook, like the table
    /// beside it. Zero for a layer that always appears.</param>
    /// <param name="AlwaysAbsent">The layer never appears and is not rolled at all. Distinct from a
    /// large <paramref name="AbsentWeight"/>, which still costs a draw: at 100% there is nothing to
    /// choose between, and the weight has no finite value anyway.</param>
    private sealed record LayerPlan(
        LoadedIngredient Ingredient,
        WeightedRoller.WeightTable Weights,
        IReadOnlyDictionary<string, Variant> VariantById,
        double AbsentWeight,
        bool AlwaysAbsent);

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
                var table = WeightedRoller.Prepare(ing.Manifest.Variants.ToDictionary(v => v.Id, v => v.Weight));
                double percent = recipe.Manifest.AbsentPercentOf(id);
                bool never = WeightedRoller.AlwaysAbsent(percent);
                return new LayerPlan(
                    ing,
                    table,
                    ing.Manifest.Variants.ToDictionary(v => v.Id),
                    // Zero for the overwhelmingly common case of a layer that always appears, and
                    // zero is what makes the roll below bit-identical to the one that shipped
                    // before optional layers existed.
                    never ? 0 : WeightedRoller.AbsentWeight(percent, ing.Manifest.Variants.Sum(v => v.Weight)),
                    never);
            })
            .ToList();
    }

    /// <summary>
    /// One asset's decisions, complete and costing no pixels. Holds everything the DNA is computed
    /// from, which is what lets the caller detect a duplicate before paying to render one.
    /// </summary>
    /// <param name="Dna">The identity this roll would produce.</param>
    /// <param name="Recipe">The recipe rolled.</param>
    /// <param name="Plan">Per layer: the source image, its rolled color, and the color model —
    /// a null model marks a Custom layer, cloned as-is rather than colorized.</param>
    /// <param name="Traits">The trait selections, in layer order.</param>
    /// <param name="ColorRolls">The per-layer color record for the rich metadata.</param>
    /// <param name="AbsentLayers">The layers this roll left out entirely.</param>
    private sealed record RolledAsset(
        string Dna,
        LoadedRecipe Recipe,
        IReadOnlyList<(Image<Rgba32> Source, RolledColor Color, ColorModel? Model)> Plan,
        IReadOnlyList<TraitSelection> Traits,
        IReadOnlyList<ColorRoll> ColorRolls,
        IReadOnlyList<AbsentLayer> AbsentLayers);

    /// <summary>
    /// The roll phase: consumes the RNG, decides the whole asset, and touches no pixels. Returns
    /// null when the recipe's rules reject the selection.
    /// </summary>
    private static RolledAsset? RollOne(
        LoadedRecipe recipe,
        IReadOnlyList<LayerPlan> layers,
        IRng rng)
    {
        var selection = new Dictionary<string, string>();
        var traits = new List<TraitSelection>();
        var colorRolls = new List<ColorRoll>();
        var dnaParts = new List<LayerSelection>();
        var absentLayers = new List<AbsentLayer>();

        // What each layer will draw, decided here and rendered later.
        // A null model means a Custom layer, which is cloned as-is rather than colorized.
        var plan = new List<(Image<Rgba32> Source, RolledColor Color, ColorModel? Model)>();

        {
            foreach (var layer in layers)
            {
                var ing = layer.Ingredient;
                string ingId = ing.Manifest.Id;

                // A LAYER MAY BE LEFT OUT. It contributes no trait, no pixels and — deliberately —
                // no DNA part at all, rather than a sentinel "absent" marker: a marker would have to
                // be a string no variant id could ever equal, and omitting the part is unambiguous
                // without one, because a layer that IS present adds a part that an absent one does
                // not. Nothing else in the loop runs, so an absent Dynamic layer rolls no color —
                // which is what stops two assets that both show nothing from having different DNA.
                string? rolledId = layer.AlwaysAbsent
                    ? null
                    : WeightedRoller.Roll(layer.Weights, layer.AbsentWeight, rng);
                if (rolledId is null)
                {
                    // Recorded, not merely skipped. Everything else about an absent layer is an
                    // omission — no trait, no pixels, no DNA part — which leaves a finished Set
                    // unable to tell "this asset has no hat" from "this collection has no hat".
                    absentLayers.Add(new AbsentLayer(ingId, ing.Manifest.Name));
                    continue;
                }

                string variantId = rolledId;
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
                        // difference is where that color comes from: dynamic rolls it per asset,
                        // static resolves its single fixed color and consumes NO RNG (spec 5.3).
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

            return new RolledAsset(
                Dna.Compute(recipe.Manifest.Id, dnaParts), recipe, plan, traits, colorRolls, absentLayers);
        }
    }

    /// <summary>
    /// The render phase: no RNG, only pixels. Separated from the roll so a duplicate DNA can be
    /// discarded without ever paying for the colorize and composite that produced it.
    /// </summary>
    private static GeneratedAsset Render(Dimensions canvas, RolledAsset rolled, int number)
    {
        var images = new List<Image<Rgba32>>(rolled.Plan.Count);
        try
        {
            foreach (var (source, color, model) in rolled.Plan)
                images.Add(model is null
                    ? source.Clone()
                    : Colorizer.Apply(source, color.H, color.S, model.Value));

            var composed = Compositor.Composite(canvas, images);
            foreach (var img in images) img.Dispose();

            return new GeneratedAsset
            {
                SetNumber = number,
                Dna = rolled.Dna,
                RecipeId = rolled.Recipe.Manifest.Id,
                RecipeName = rolled.Recipe.Manifest.Name,
                Image = composed,
                Traits = rolled.Traits,
                ColorRolls = rolled.ColorRolls,
                AbsentLayers = rolled.AbsentLayers,
            };
        }
        catch
        {
            // Colorizer.Apply or Compositor.Composite can throw mid-stack; every layer image
            // already cloned/colorized into `images` has no other owner yet, so it must be
            // disposed here before the original exception propagates.
            foreach (var img in images) img.Dispose();
            throw;
        }
    }
}
