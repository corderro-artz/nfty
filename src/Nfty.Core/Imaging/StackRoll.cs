using System.Globalization;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Ticket;

namespace Nfty.Core.Imaging;

/// <summary>
/// Rolls a whole Recipe into the <see cref="PreviewLayer"/> stack <see cref="StackPreview.Render"/>
/// draws — one deterministic roll from a seed, in generation's own draw order.
///
/// <para><b>The recipe's stack is decided by <see cref="Ticketer.TryRoll"/>, not by a walk of its
/// own.</b> The draw order <i>is</i> the determinism contract — which layers are left out, which
/// variant each present one gets, which color a Dynamic one rolls — and this used to be a second copy
/// of it. The copy never learned about optional layers, so on any book using them it previewed stacks
/// the seed would never cook. <see cref="ForRecipe"/> now projects the recipe into a one-recipe
/// <see cref="MenuBook"/> and rolls it exactly as <c>Generator</c> does, so the two cannot disagree.
/// <see cref="ForIngredient"/> still rolls a single loose layer itself: it has no recipe, so no
/// absent chance and no rules, and it is not the roll of an asset.</para>
///
/// <para><b>What it does not reproduce.</b> The roll includes the recipe draw every asset starts with,
/// taken against a book that holds only this recipe — so for a CookBook whose ONLY recipe this is,
/// previewing it at seed <c>s</c> is the first asset of generating that book at seed <c>s</c>. In a
/// book of several recipes that draw would have picked among them, so the preview is then what this
/// recipe's stack looks like when rolled — the same layers, in the same order, with colors drawn from
/// the same distribution — and not any particular asset.</para>
/// </summary>
public static class StackRoll
{
    /// <summary>
    /// The RNG a seed string drives — <see cref="SeedHash.ToUlong"/> into
    /// <see cref="SplitMix64Rng"/>, exactly as <c>Generator</c> seeds a run. Exposed so a caller
    /// rolling a recipe and then a loose extra layer draws both from one continuing stream instead of
    /// re-seeding (which would hand the two layers correlated colors), and so no front-end has to
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
    /// Rolls a recipe's stack, bottom-to-top, honoring its absent chances and its incompatibility
    /// rules — through <see cref="Ticketer.TryRoll"/>, the product's one roll of an asset.
    /// </summary>
    /// <param name="recipe">The recipe whose stack to roll.</param>
    /// <param name="rng">The run's RNG, from <see cref="RngFor"/>. Consumed exactly as generation
    /// consumes it: per attempt, one recipe draw, then per layer its absent-or-variant draw and, for a
    /// present Dynamic layer, its color draws.</param>
    /// <param name="maxAttempts">How many whole-stack rolls to try before giving up on the rules.
    /// Defaults to the same budget generation uses, so a recipe that previews is a recipe that
    /// cooks.</param>
    /// <returns>One layer per PRESENT layer, in paint order — ready for
    /// <see cref="StackPreview.Render"/>. A layer the roll left out has no entry, exactly as it has no
    /// pixels in a cooked asset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="recipe"/> or <paramref name="rng"/>
    /// is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAttempts"/> is not
    /// positive.</exception>
    /// <exception cref="KeyNotFoundException">The layer order names an ingredient the recipe does not
    /// carry — a malformed recipe, which <c>Validator.ValidateRecipe</c> reports properly.</exception>
    /// <exception cref="InvalidOperationException">The recipe's rules rejected every attempt; or a
    /// stacked colorized layer carries no usable colorization (checked for every stacked layer before
    /// anything is rolled, including one the roll would have left out).</exception>
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

        // Every malformation this method has always named is named BEFORE the roll, in the words it
        // always used: Ticketer trusts a Validator-clean book and would fail on these with a bare
        // KeyNotFoundException or NullReferenceException instead.
        foreach (var layerId in recipe.Manifest.LayerOrder)
        {
            if (!byId.TryGetValue(layerId, out var ingredient))
                throw new KeyNotFoundException(
                    $"Recipe '{recipe.Manifest.Id}' stacks an ingredient '{layerId}' that it does "
                    + "not carry, so there is nothing to roll for that layer.");
            if (ingredient.Manifest.Kind != LayerKind.Custom) SpecOrColorization(ingredient);
        }

        var menu = Ticketer.Prepare(OneRecipeMenu(recipe, byId));

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            // The same gate generation applies, in the same place: legality is a function of the
            // variant selection alone, and an illegal roll is discarded and re-rolled. A preview that
            // skipped this would happily show a combination the collection can never contain — the
            // same class of lie as scaling a layer to fit.
            var ticket = Ticketer.TryRoll(menu, rng);
            if (ticket is null) continue;

            return ticket.Layers.Select(t => ToPreview(byId[t.IngredientId], t)).ToList();
        }

        throw new InvalidOperationException(
            $"Could not roll a legal stack for recipe '{recipe.Manifest.Id}' after {maxAttempts} "
            + "attempts: its incompatibility rules rejected every roll. Loosen the rules, or run "
            + "validate to see what the recipe allows.");
    }

    /// <summary>
    /// The recipe as the only recipe of a <see cref="MenuBook"/> — what <see cref="Ticketer"/> rolls.
    /// </summary>
    /// <remarks>
    /// Built here rather than through <c>MenuProjection</c>, which takes a whole book and throws on a
    /// duplicate id; this keeps the tolerance a preview has always had (a duplicate ingredient id
    /// resolves last-wins, above, and a duplicate variant id first-wins, as <c>RollVariant</c>
    /// does). For a well-formed recipe the layers are exactly the ones <c>MenuProjection</c> makes.
    /// The canvas is never read by the roll, so a zero one stands in for the book's.
    /// </remarks>
    private static MenuBook OneRecipeMenu(LoadedRecipe recipe, IReadOnlyDictionary<string, LoadedIngredient> byId)
    {
        var layers = recipe.Manifest.LayerOrder
            .Distinct(StringComparer.Ordinal)
            .Select(id => byId[id].Manifest)
            .Select(m => new MenuLayer(m.Id, m.Name, m.Kind, m.Colorization,
                m.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal).ToList()))
            .ToList();

        var menuRecipe = new MenuRecipe(recipe.Manifest.Id, recipe.Manifest.Name, 1,
            recipe.Manifest.LayerOrder, layers, recipe.Manifest.Rules,
            recipe.Manifest.AbsentPercent ?? new Dictionary<string, double>(StringComparer.Ordinal));

        return new MenuBook(MenuSchema.Current, null, new Dimensions(0, 0), string.Empty, string.Empty,
            [menuRecipe], new MenuArt(MenuArtMode.Loose, 0, 0, []));
    }

    /// <summary>
    /// One rolled layer as a <see cref="PreviewLayer"/>. The kind comes from the INGREDIENT, never the
    /// ticket — the rule <c>Plating.Plate</c> keeps. A Dynamic layer carries the ticket's unrounded
    /// (H,S) as <see cref="PreviewLayer.Rolled"/> beside its readable spec; a Static one carries its
    /// author-written spec verbatim; a Custom one carries neither.
    /// </summary>
    private static PreviewLayer ToPreview(LoadedIngredient ingredient, TicketLayer rolled)
    {
        switch (ingredient.Manifest.Kind)
        {
            case LayerKind.Custom:
                return new PreviewLayer(ingredient, rolled.VariantId, null, null);
            case LayerKind.Static:
                return new PreviewLayer(ingredient, rolled.VariantId, SpecOrColorization(ingredient).Spec, null);
            default:
                var color = new RolledColor(rolled.Hue!.Value, rolled.Sat!.Value);
                return new PreviewLayer(ingredient, rolled.VariantId,
                    SpecFor(color, SpecOrColorization(ingredient).Colorization.Model), color);
        }
    }

    /// <summary>
    /// Rolls one layer: its variant by weight, then its color by kind.
    ///
    /// <para>Kind decides everything, exactly as it does at generation time. <b>Dynamic</b> rolls a
    /// color and so consumes RNG; <b>Static</b> carries a single fixed color and consumes
    /// <i>none</i>; <b>Custom</b> is composited as-is and gets no color at all. Keeping the same
    /// consumption pattern is what makes a preview of a stack reproducible against the same seed
    /// however the layer kinds are mixed.</para>
    /// </summary>
    /// <param name="ingredient">The layer to roll.</param>
    /// <param name="rng">The run's RNG.</param>
    /// <param name="variantId">Which variant to draw, when the caller has already decided — a
    /// reference layer placed beside a stack is chosen by <see cref="StackPreview.PickVariant"/> or
    /// named outright, not rolled, so that varying the seed to explore the stack does not also keep
    /// changing the reference. Naming one <b>consumes no variant draw</b>: the choice was not the
    /// RNG's to make, so it must not cost the RNG a step and shift every color after it.</param>
    /// <returns>The layer, its variant, and the color spec to draw it in (null for Custom).</returns>
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
        // members in. Variant first, then color — the order Ticketer.TryRoll uses for a present
        // layer. There is no absent draw here: a loose ingredient belongs to no recipe, so it has no
        // absent chance to roll.
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
    /// A rolled hue and saturation written back out as the prefixed color spec a
    /// <see cref="PreviewLayer"/> carries.
    ///
    /// <para><b>This spelling is lossy, which is exactly why nothing renders through it.</b> Every
    /// spec resolves via 8-bit RGB on its way back to (H,S), so a rolled hue that fell between two
    /// representable colors cannot be written down exactly. <see cref="PreviewLayer"/> therefore
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
    /// through verbatim from its own colorization and a Custom layer has no color at all, so only a
    /// <i>rolled</i> color goes through this at all.</para>
    /// </summary>
    /// <param name="color">The rolled hue (degrees) and saturation (0..1).</param>
    /// <param name="model">The layer's color model, which decides both the prefix and the axis the
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
    /// The color one layer draws in, and the RNG it costs to decide it.
    /// </summary>
    /// <returns>The readable spec, and — for a <i>rolled</i> color only — the unrounded value behind
    /// it. Both travel: the spec is what gets printed and pasted, the <see cref="RolledColor"/> is
    /// what gets drawn. A spec can only spell a color 8-bit RGB can represent, so returning the
    /// string alone would round a rolled hue on its way to the renderer and put the preview a step off
    /// the asset. Static returns its author-written spec with no rolled value, because generation
    /// resolves that same string through that same parser — it is already exact.</returns>
    private static (string? Spec, RolledColor? Rolled) ColorFor(LoadedIngredient ingredient, IRng rng)
    {
        // Composited as-is, never colorized: no color, and no draw.
        if (ingredient.Manifest.Kind == LayerKind.Custom) return (null, null);

        var (spec, colorization) = SpecOrColorization(ingredient);

        // Verbatim, and no RNG — the same two properties generation gives a Static layer. Passing the
        // author's own spec through rather than resolving and re-formatting it also keeps this case
        // exact, where a rolled color cannot be.
        if (spec is not null) return (spec, null);

        var color = ColorRoller.Roll(colorization, rng);
        return (SpecFor(color, colorization.Model), color);
    }

    /// <summary>
    /// A colorized layer's colorization, and for a Static layer its one fixed spec — or the
    /// InvalidOperationException that names the layer when it has neither. Shared by the loose roll
    /// and the recipe roll, which checks every stacked layer with it before rolling.
    /// </summary>
    private static (string? Spec, Colorization Colorization) SpecOrColorization(LoadedIngredient ingredient)
    {
        var kind = ingredient.Manifest.Kind;
        var colorization = ingredient.Manifest.Colorization
            ?? throw new InvalidOperationException(
                $"Ingredient '{ingredient.Manifest.Name}' is {kind.ToString().ToLowerInvariant()} but "
                + "carries no colorization block, so there is no color to roll.");

        if (kind != LayerKind.Static) return (null, colorization);

        var entry = colorization.Entries.Count == 1 ? colorization.Entries[0] : null;
        return (entry?.Fixed
            ?? throw new InvalidOperationException(
                $"Ingredient '{ingredient.Manifest.Name}' is static, so it must carry exactly one "
                + "fixed color to be drawn in; run validate on the recipe to see what is wrong."),
            colorization);
    }
}
