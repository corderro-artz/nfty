using System.Numerics;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Ticket;
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
///
/// <para><b>The roll itself lives in <see cref="Ticketer"/>.</b> This class projects the book into
/// a <see cref="MenuBook"/> (<see cref="MenuProjection"/>) and keeps only the render — resolving a
/// ticket's ids to the actual decoded images, colorizing, compositing. There is exactly one
/// definition of the order in which layers consume the RNG.</para>
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
    /// ticket: resolve each layer's image, colorize, composite, done. Nothing it does can observe
    /// another asset, the thread it runs on, or how many threads there are.</para>
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
        var (recipeById, menu) = Prepare(book, opts);
        var rolls = RollAll(book, opts, menu, existingDnas, startNumber, cancellationToken);

        var canvas = book.Manifest.Canvas;
        var ingByRecipe = IngredientsByRecipe(recipeById);
        var assets = new GeneratedAsset[rolls.Count];
        var parallelism = opts.Parallelism ?? Parallelism.Default;
        var imageConfig = parallelism.ImageConfiguration(insideParallelBody: !parallelism.IsSequential);
        var progressGate = new Lock();
        int done = 0;
        try
        {
            ParallelWork.For(rolls.Count, parallelism, cancellationToken, i =>
            {
                var (ticket, number) = rolls[i];
                assets[i] = Render(
                    canvas, recipeById[ticket.RecipeId], ingByRecipe[ticket.RecipeId], ticket, number,
                    imageConfig);

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
            book.SourceSha256,
            opts.EnforceUniqueDna);
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
        var (recipeById, menu) = Prepare(book, opts);
        return Stream(book, opts, recipeById, menu, existingDnas, startNumber, progress, cancellationToken);
    }

    /// <summary>
    /// The checks and lookups both entry points share, run eagerly before any work.
    /// </summary>
    /// <param name="book">The cookbook.</param>
    /// <param name="opts">The run's options.</param>
    /// <returns>The recipes by id (for resolving a ticket's ids to images), and the prepared Menu
    /// this run rolls through.</returns>
    private static (IReadOnlyDictionary<string, LoadedRecipe> ById, PreparedMenu Menu)
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

        var recipeById = book.Recipes.ToDictionary(r => r.Manifest.Id, StringComparer.Ordinal);
        if (opts.RecipeId is not null && !recipeById.ContainsKey(opts.RecipeId))
            throw new InvalidOperationException($"Recipe '{opts.RecipeId}' not found in cookbook.");

        var projected = MenuProjection.From(book);
        if (opts.RecipeId is not null)
        {
            // Restricting to one recipe overrides the weights entirely, so that recipe is in play
            // whatever the book weights it — the same override the old recipeWeights table applied.
            var only = projected.Recipes.Single(r => r.Id == opts.RecipeId);
            projected = projected with { Recipes = [only with { Weight = 1 }] };
        }

        return (recipeById, Ticketer.Prepare(projected));
    }

    /// <summary>
    /// Every recipe's ingredients by id, resolved once per run rather than once per asset — a fixed
    /// property of the cookbook that <see cref="Render"/> needs to turn a ticket's ids into images.
    /// </summary>
    private static Dictionary<string, IReadOnlyDictionary<string, LoadedIngredient>> IngredientsByRecipe(
        IReadOnlyDictionary<string, LoadedRecipe> recipeById) =>
        recipeById.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyDictionary<string, LoadedIngredient>)
                kv.Value.Ingredients.ToDictionary(i => i.Manifest.Id, StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>
    /// The roll phase for the whole run, in order, on one thread.
    /// </summary>
    /// <remarks>
    /// This is the half that can never be parallel, and the reason is not performance: it consumes
    /// a single RNG stream and rejects duplicates against a running set, so the Nth asset's
    /// decisions depend on every decision before it. Splitting it would change the collection.
    /// It touches no pixels, which is why doing it all up front is cheap.
    /// </remarks>
    private static List<(AssetTicket Ticket, int Number)> RollAll(
        LoadedCookBook book,
        GenerateOptions opts,
        PreparedMenu menu,
        IReadOnlyList<string>? existingDnas,
        int startNumber,
        CancellationToken cancellationToken)
    {
        var rng = new SplitMix64Rng(SeedHash.ToUlong(opts.Seed));
        var seen = new HashSet<string>(existingDnas ?? Array.Empty<string>());

        var rolls = new List<(AssetTicket, int)>(opts.Count);
        int number = startNumber;

        for (int i = 0; i < opts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AssetTicket? rolled = null;
            for (int attempt = 0; attempt < opts.MaxRerollsPerAsset; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ticket = Ticketer.TryRoll(menu, rng);
                if (ticket is null) continue;                          // rule violation → reroll
                if (opts.EnforceUniqueDna && !seen.Add(ticket.Dna)) continue;

                rolled = ticket;
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
        PreparedMenu menu,
        IReadOnlyList<string>? existingDnas,
        int startNumber,
        IProgress<GenerationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rng = new SplitMix64Rng(SeedHash.ToUlong(opts.Seed));
        var seen = new HashSet<string>(existingDnas ?? Array.Empty<string>());
        int number = startNumber;
        var canvas = book.Manifest.Canvas;
        var ingByRecipe = IngredientsByRecipe(recipeById);

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

                var ticket = Ticketer.TryRoll(menu, rng);
                if (ticket is null) continue;                // rule violation → reroll

                // Dedup BEFORE rendering. The DNA is a function of the selection and the rolled
                // colors, all of which the roll phase already has, so a collision can be detected
                // without touching a pixel. Rendering first meant every duplicate threw away a
                // fully colorized and composited canvas — and minting near the top of the unique
                // space is the normal case, where coupon-collector says most attempts collide. The
                // file already applied exactly this reasoning to the rules check one phase earlier.
                //
                // With uniqueness off, a repeated DNA is accepted: identity comes from the token id,
                // not the DNA, so there is nothing to dedup and nothing to exhaust.
                if (opts.EnforceUniqueDna && !seen.Add(ticket.Dna)) continue;

                asset = Render(
                    canvas, recipeById[ticket.RecipeId], ingByRecipe[ticket.RecipeId], ticket, number,
                    Parallelism.Sequential.ImageConfiguration(insideParallelBody: false));
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
        // Written as a predicate rather than a property pattern because Combos is a BigInteger and
        // a pattern cannot match one against a constant — there is no BigInteger literal.
        var dead = inPlay.Where(id => space[id] is { IsCountable: true } s && s.Combos.IsZero).ToList();
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

        // Summed by the counter itself: the saturating add and the certainty fold are one rule, and
        // this used to be the second copy of it. Only the recipes in play, so a shelved recipe never
        // inflates a maximum the run could not have reached.
        var pool = space.Over(inPlay);
        BigInteger available = pool.Total;
        string scope = opts.RecipeId is null ? "this cookbook" : $"recipe '{opts.RecipeId}'";
        string ran = $"after {opts.MaxRerollsPerAsset} attempts ({produced} of {opts.Count} generated)";

        // FOUR sentences, because there are four things that can be true, and the middle two are
        // opposites. "more than N" on an upper bound is the one that shipped, and it told an author
        // whose rules admit four hundred assets that their book allowed more than a million.
        string message = pool.Certainty switch
        {
            SpaceCertainty.Exact =>
                $"Could not produce a unique asset: {scope} allows exactly {SpaceText.Exact(available)} "
                + $"unique DNA, but {opts.Count} were requested ({produced} generated).",

            SpaceCertainty.AtLeast =>
                $"Could not produce a unique asset {ran}. {scope} allows more than "
                + $"{SpaceText.Exact(available)} unique DNA, so the reroll budget ran out before the "
                + "space did.",

            SpaceCertainty.AtMost =>
                $"Could not produce a unique asset {ran}. {scope} has too many combinations to count "
                + $"exactly; its rules allow at most {SpaceText.Exact(available)} unique DNA, and "
                + $"{opts.Count} were requested.",

            _ =>
                $"Could not produce a unique asset {ran}. {scope} has a unique DNA space that cannot "
                + "be counted — run validate.",
        };

        return new UniqueSpaceExhaustedException(available, pool.Certainty, opts.Count, produced, message);
    }

    private static string Describe(IReadOnlyList<string> recipeIds) =>
        recipeIds.Count == 1
            ? $"recipe '{recipeIds[0]}'"
            : "recipes " + string.Join(", ", recipeIds.Select(r => $"'{r}'"));

    /// <summary>
    /// The render phase: no RNG, only pixels. Resolves a ticket's ids to the recipe's decoded
    /// images, colorizes Dynamic/Static layers with the ticket's rolled (H,S), composites Custom
    /// layers as-is, and rebuilds the rich metadata (<see cref="TraitSelection"/>,
    /// <see cref="ColorRoll"/>, <see cref="AbsentLayer"/>) the roll itself has no reason to carry.
    /// </summary>
    /// <param name="canvas">The book's canvas — every variant is validated against it.</param>
    /// <param name="recipe">The rolled recipe, decoded.</param>
    /// <param name="ingById">That recipe's ingredients by id.</param>
    /// <param name="ticket">The roll to render.</param>
    /// <param name="number">The asset's number within the Set.</param>
    /// <param name="imageConfiguration">The capped ImageSharp configuration to composite under.</param>
    /// <remarks>
    /// Runs concurrently on many threads, and every body reads the SAME decoded
    /// <c>VariantImages</c>. That is safe only because they are read-only here: <c>Clone</c> and
    /// <c>Colorizer.Apply</c> read pixels and write to new images. Never <c>Mutate</c> a source image.
    /// </remarks>
    private static GeneratedAsset Render(
        Dimensions canvas,
        LoadedRecipe recipe,
        IReadOnlyDictionary<string, LoadedIngredient> ingById,
        AssetTicket ticket,
        int number,
        SixLabors.ImageSharp.Configuration imageConfiguration)
    {
        var images = new List<Image<Rgba32>>(ticket.Layers.Count);
        var traits = new List<TraitSelection>(ticket.Layers.Count);
        var colorRolls = new List<ColorRoll>(ticket.Layers.Count);
        Image<Rgba32>? composed = null;
        try
        {
            // Resolved BEFORE anything is composited: a lookup that can throw must not run while
            // a composed canvas exists with no owner.
            var present = new HashSet<string>(ticket.Layers.Select(l => l.IngredientId), StringComparer.Ordinal);
            var absentLayers = recipe.Manifest.LayerOrder
                .Where(id => !present.Contains(id))
                .Select(id => new AbsentLayer(id, ingById[id].Manifest.Name))
                .ToList();

            foreach (var layer in ticket.Layers)
            {
                var ing = ingById[layer.IngredientId];
                var variant = ing.Manifest.Variants.First(v => v.Id == layer.VariantId);
                traits.Add(new TraitSelection(layer.IngredientId, ing.Manifest.Name, layer.VariantId, variant.Name));

                var source = ing.VariantImages[layer.VariantId];                // Reading Kind/Model off the TICKET here is the one place in the product that does
                // - everywhere else (Plating.Plate, chiefly) they come from the Menu/book, never
                // from the ticket, because a wire ticket's kind and model are not covered by
                // Dna.Compute and a tampered ticket could otherwise keep its provable identity while
                // rendering different pixels (see Plating.Plate's own remarks). That risk does not
                // exist here: this ticket was produced IN-PROCESS, earlier in this same Generate
                // call, by Ticketer rolling this exact LoadedCookBook (via MenuProjection) - there is
                // no wire, no second party and no chance the ticket disagrees with the book it came
                // from. This is the one sanctioned exemption from that rule, not a second copy of the
                // mistake it guards against.
                images.Add(layer.Model is ColorModel model
                    ? Colorizer.Apply(source, layer.Hue!.Value, layer.Sat!.Value, model)
                    : source.Clone());

                colorRolls.Add(new ColorRoll(layer.IngredientId, layer.Kind, layer.Model, layer.Hue, layer.Sat));
            }

            composed = Compositor.Composite(canvas, images, imageConfiguration);
            foreach (var img in images) img.Dispose();
            images.Clear();

            // AbsentLayers (above) is the ticket's present layers subtracted from the recipe's whole
            // layerOrder — an omission in the ticket rather than a marker, exactly as the roll
            // itself treats it (see Ticketer.TryRoll).
            return new GeneratedAsset
            {
                SetNumber = number,
                Dna = ticket.Dna,
                RecipeId = recipe.Manifest.Id,
                RecipeName = recipe.Manifest.Name,
                Image = composed,
                Traits = traits,
                ColorRolls = colorRolls,
                AbsentLayers = absentLayers,
            };
        }
        catch
        {
            // Colorizer.Apply or Compositor.Composite can throw mid-stack; every layer image
            // already cloned/colorized into `images` has no other owner yet, so it must be
            // disposed here before the original exception propagates.
            foreach (var img in images) img.Dispose();
            composed?.Dispose();
            throw;
        }
    }
}
