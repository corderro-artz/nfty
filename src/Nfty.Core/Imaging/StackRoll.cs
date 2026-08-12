using System.Globalization;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Imaging;

/// <summary>
/// Rolls a whole Recipe into the <see cref="PreviewLayer"/> stack <see cref="StackPreview.Render"/>
/// draws — one deterministic roll from a seed, in generation's own draw order.
///
/// <para><b>Why this is in Core.</b> The draw order <i>is</i> the determinism contract: a variant is
/// rolled per layer walking <c>layerOrder</c> bottom-to-top, a Dynamic layer then consumes its colour
/// draws and a Static one consumes none. A front-end that re-derived that walk would be a second copy
/// of the rule, and the first reorder or new layer kind would make the two disagree silently. So the
/// walk lives here, beside the roller it calls, and the CLI's <c>preview</c> and a GUI panel share
/// it.</para>
///
/// <para><b>What it does not reproduce.</b> A cooked asset also rolls <i>which recipe</i> it is, from
/// the CookBook's weights, before any of this — so previewing <c>cat.rcp</c> at seed <c>s</c> is not
/// the first asset of generating its CookBook at seed <c>s</c>, and is not meant to be. What it does
/// reproduce is what one Recipe's stack looks like when rolled: the same layers, in the same order,
/// with colours drawn from the same distribution.</para>
/// </summary>
public static class StackRoll
{
    /// <summary>
    /// The RNG a seed string drives — <see cref="SeedHash.ToUlong"/> into
    /// <see cref="SplitMix64Rng"/>, exactly as <c>Generator</c> seeds a run. Exposed so a caller
    /// rolling a recipe and then a loose extra layer draws both from one continuing stream instead of
    /// re-seeding (which would hand the two layers correlated colours), and so no front-end has to
    /// name the seeding scheme itself.
    /// </summary>
    /// <param name="seed">The user's seed string.</param>
    /// <returns>A fresh RNG positioned at the start of that seed's sequence.</returns>
    public static IRng RngFor(string seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return new SplitMix64Rng(SeedHash.ToUlong(seed));
    }

    /// <summary>
    /// Rolls every layer of a recipe, bottom-to-top, honouring its incompatibility rules.
    /// </summary>
    /// <param name="recipe">The recipe whose stack to roll.</param>
    /// <param name="rng">The run's RNG, from <see cref="RngFor"/>. Consumed in generation's order:
    /// one variant draw per layer, then that layer's colour draws.</param>
    /// <param name="maxAttempts">How many whole-stack rolls to try before giving up on the rules.
    /// Defaults to the same budget generation uses, so a recipe that previews is a recipe that
    /// cooks.</param>
    /// <returns>One layer per <c>layerOrder</c> entry, in paint order — ready for
    /// <see cref="StackPreview.Render"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recipe"/> or <paramref name="rng"/>
    /// is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is not
    /// positive.</exception>
    /// <exception cref="KeyNotFoundException">The layer order names an ingredient the recipe does not
    /// carry — a malformed recipe, which <c>Validator.ValidateRecipe</c> reports properly.</exception>
    /// <exception cref="InvalidOperationException">The recipe's rules rejected every attempt.</exception>
    public static IReadOnlyList<PreviewLayer> ForRecipe(
        LoadedRecipe recipe, IRng rng, int maxAttempts = GenerateOptions.DefaultMaxRerolls)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        // Tolerant of a duplicate ingredient id (last wins) rather than thrown on, like Validator
        // and inspect: a preview is something you reach for BECAUSE a recipe looks wrong, so it must
        // not be the thing that crashes on a malformed one.
        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var ing in recipe.Ingredients) byId[ing.Manifest.Id] = ing;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var layers = new List<PreviewLayer>(recipe.Manifest.LayerOrder.Count);
            var selection = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var layerId in recipe.Manifest.LayerOrder)
            {
                if (!byId.TryGetValue(layerId, out var ingredient))
                    throw new KeyNotFoundException(
                        $"Recipe '{recipe.Manifest.Id}' stacks an ingredient '{layerId}' that it does "
                        + "not carry, so there is nothing to roll for that layer.");

                var layer = ForIngredient(ingredient, rng);
                layers.Add(layer);
                selection[layerId] = layer.VariantId;
            }

            // The same gate generation applies, in the same place: legality is a function of the
            // variant selection alone, and an illegal roll is discarded and re-rolled. A preview that
            // skipped this would happily show a combination the collection can never contain — the
            // same class of lie as scaling a layer to fit.
            if (RulesEngine.IsLegal(selection, recipe.Manifest.Rules)) return layers;
        }

        throw new InvalidOperationException(
            $"Could not roll a legal stack for recipe '{recipe.Manifest.Id}' after {maxAttempts} "
            + "attempts: its incompatibility rules rejected every roll. Loosen the rules, or run "
            + "validate to see what the recipe allows.");
    }

    /// <summary>
    /// Rolls one layer: its variant by weight, then its colour by kind.
    ///
    /// <para>Kind decides everything, exactly as it does at generation time. <b>Dynamic</b> rolls a
    /// colour and so consumes RNG; <b>Static</b> carries a single fixed colour and consumes
    /// <i>none</i>; <b>Custom</b> is composited as-is and gets no colour at all. Keeping the same
    /// consumption pattern is what makes a preview of a stack reproducible against the same seed
    /// however the layer kinds are mixed.</para>
    /// </summary>
    /// <param name="ingredient">The layer to roll.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <param name="variantId">Which variant to draw, when the caller has already decided — a
    /// reference layer placed beside a stack is chosen by <see cref="StackPreview.PickVariant"/> or
    /// named outright, not rolled, so that varying the seed to explore the stack does not also keep
    /// changing the reference. Naming one <b>consumes no variant draw</b>: the choice was not the
    /// RNG's to make, so it must not cost the RNG a step and shift every colour after it.</param>
    /// <returns>The layer, its variant, and the colour spec to draw it in (null for Custom).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ingredient"/> or
    /// <paramref name="rng"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A colorized layer carries no usable colorization,
    /// which <c>Validator</c> reports as a problem rather than leaving to be discovered here.</exception>
    public static PreviewLayer ForIngredient(
        LoadedIngredient ingredient, IRng rng, string? variantId = null)
    {
        ArgumentNullException.ThrowIfNull(ingredient);
        ArgumentNullException.ThrowIfNull(rng);

        // Two statements rather than two arguments to one constructor: both consume the RNG, so
        // their ORDER is the determinism contract, and inlining them would hand that contract to
        // C#'s argument-evaluation order and to whatever order PreviewLayer happens to declare its
        // members in. Variant first, then colour — the order Generator.RollOne uses.
        string variant = variantId ?? RollVariant(ingredient, rng);
        var (spec, rolled) = ColorFor(ingredient, rng);
        return new PreviewLayer(ingredient, variant, spec, rolled);
    }

    /// <summary>One variant, by weight — the draw generation takes first for every layer.</summary>
    private static string RollVariant(LoadedIngredient ingredient, IRng rng)
    {
        // Built by hand rather than with ToDictionary: a duplicate variant id would throw a bare
        // "same key has already been added" from the framework. IngredientArchive.Read already
        // refuses one on the way in, so this only decides what an in-memory ingredient does.
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var v in ingredient.Manifest.Variants) weights.TryAdd(v.Id, v.Weight);

        return WeightedRoller.Roll(weights, rng);
    }

    /// <summary>
    /// A rolled hue and saturation written back out as the prefixed colour spec a
    /// <see cref="PreviewLayer"/> carries.
    ///
    /// <para><b>This spelling is lossy, which is exactly why nothing renders through it.</b> Every
    /// spec resolves via 8-bit RGB on its way back to (H,S), so a rolled hue that fell between two
    /// representable colours cannot be written down exactly. <see cref="PreviewLayer"/> therefore
    /// carries the unrounded <see cref="RolledColor"/> alongside this string and draws from that; the
    /// spec is what a person reads, pastes and re-runs. Rendering through the string instead put the
    /// preview about a quarter-degree of hue off the asset it claims to show — small, but this path
    /// promises what generation <i>would</i> draw, and a value generation never quantized must not be
    /// quantized on the way to being shown.</para>
    ///
    /// <para>Value/lightness is still pinned at whichever end of its axis leaves the most room for hue
    /// and saturation (<c>v=100</c> for HSV, <c>l=50</c> for HSL — <c>l=100</c> is pure white and
    /// would throw the saturation away entirely), so that the printed spec, if someone does re-run it,
    /// lands as close as the format allows.</para>
    ///
    /// <para>Static and Custom layers are exact, not approximate: a Static layer's spec is passed
    /// through verbatim from its own colorization and a Custom layer has no colour at all, so only a
    /// <i>rolled</i> colour goes through this at all.</para>
    /// </summary>
    /// <param name="color">The rolled hue (degrees) and saturation (0..1).</param>
    /// <param name="model">The layer's colour model, which decides both the prefix and the axis the
    /// third component sits on.</param>
    /// <returns>A spec such as <c>hsv:210.5,64.2,100</c>, formatted invariantly — it is printed, and
    /// re-parsed by <see cref="ColorSpec"/>, both of which a comma-decimal locale would break.</returns>
    public static string SpecFor(RolledColor color, ColorModel model)
    {
        // Rounded to three decimals, which is two orders of magnitude finer than the 8-bit RGB this
        // spec resolves back through can express (about a quarter of a degree of hue at best), so it
        // discards nothing a renderer could have used from the string anyway. What it buys is a spec
        // a person can read, paste and re-run. It is NOT what gets drawn — PreviewLayer.Rolled is,
        // and carries the unrounded value; see this method's summary.
        string h = Math.Round(color.H, 3).ToString(CultureInfo.InvariantCulture);
        string s = Math.Round(color.S * 100.0, 3).ToString(CultureInfo.InvariantCulture);
        return model == ColorModel.Hsv ? $"hsv:{h},{s},100" : $"hsl:{h},{s},50";
    }

    /// <summary>
    /// The colour one layer draws in, and the RNG it costs to decide it.
    /// </summary>
    /// <returns>The readable spec, and — for a <i>rolled</i> colour only — the unrounded value behind
    /// it. Both travel: the spec is what gets printed and pasted, the <see cref="RolledColor"/> is
    /// what gets drawn. A spec can only spell a colour 8-bit RGB can represent, so returning the
    /// string alone would round a rolled hue on its way to the renderer and put the preview a step off
    /// the asset. Static returns its author-written spec with no rolled value, because generation
    /// resolves that same string through that same parser — it is already exact.</returns>
    private static (string? Spec, RolledColor? Rolled) ColorFor(LoadedIngredient ingredient, IRng rng)
    {
        var kind = ingredient.Manifest.Kind;

        // Composited as-is, never colorized: no colour, and no draw.
        if (kind == LayerKind.Custom) return (null, null);

        var colorization = ingredient.Manifest.Colorization
            ?? throw new InvalidOperationException(
                $"Ingredient '{ingredient.Manifest.Name}' is {kind.ToString().ToLowerInvariant()} but "
                + "carries no colorization block, so there is no colour to roll.");

        if (kind == LayerKind.Static)
        {
            // Verbatim, and no RNG — the same two properties generation gives a Static layer. Passing
            // the author's own spec through rather than resolving and re-formatting it also keeps this
            // case exact, where a rolled colour cannot be.
            var entry = colorization.Entries.Count == 1 ? colorization.Entries[0] : null;
            return (entry?.Fixed
                ?? throw new InvalidOperationException(
                    $"Ingredient '{ingredient.Manifest.Name}' is static, so it must carry exactly one "
                    + "fixed colour to be drawn in; run validate on the recipe to see what is wrong."),
                null);
        }

        var color = ColorRoller.Roll(colorization, rng);
        return (SpecFor(color, colorization.Model), color);
    }
}
