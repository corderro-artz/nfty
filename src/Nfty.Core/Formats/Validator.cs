using System.Globalization;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

public static class Validator
{
    public static IReadOnlyList<string> Validate(LoadedCookBook cb)
    {
        var problems = new List<string>();
        CheckCookBook(problems, cb);
        foreach (var r in cb.Recipes)
            CheckRecipe(problems, r, cb.Manifest.Canvas);
        return problems;
    }

    /// <summary>
    /// Validates a standalone ingredient — every check that does NOT need a canvas. The canvas is a
    /// property of the CookBook, so "does this variant match the canvas?" cannot be answered here;
    /// <see cref="Validate(LoadedCookBook)"/> remains the authoritative whole-book gate. Used by the
    /// CLI's <c>new ingredient</c> / <c>add variant</c>, which build an .igt before a canvas exists.
    /// </summary>
    public static IReadOnlyList<string> ValidateIngredient(LoadedIngredient ing)
    {
        var problems = new List<string>();
        string where = $"Ingredient '{ing.Manifest.Id}'";
        CheckIngredient(problems, where, ing);
        CheckUniformSize(problems, where, ing.VariantImages.Values);
        return problems;
    }

    /// <summary>
    /// Validates a standalone recipe — every canvas-independent check, plus that all variant images
    /// across all its ingredients share one size, so they could match some future canvas. Used by
    /// the CLI's <c>new recipe</c> / <c>add ingredient</c>.
    /// </summary>
    public static IReadOnlyList<string> ValidateRecipe(LoadedRecipe r)
    {
        var problems = new List<string>();
        CheckRecipeStructure(problems, r);
        foreach (var ing in r.Ingredients)
            CheckIngredient(problems, $"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}'", ing);
        CheckUniformSize(problems, $"Recipe '{r.Manifest.Id}'",
            r.Ingredients.SelectMany(i => i.VariantImages.Values));
        return problems;
    }

    private static void CheckCookBook(List<string> problems, LoadedCookBook cb)
    {
        // Id uniqueness comes first: every ToDictionary below would throw on a duplicate, and
        // a validator that crashes cannot report anything.
        foreach (var dup in Duplicates(cb.Recipes.Select(r => r.Manifest.Id)))
            problems.Add($"CookBook has duplicate recipe id '{dup}'.");

        if (cb.Manifest.RecipeWeights.Values.Sum() <= 0)
            problems.Add("CookBook has zero total recipe weight.");

        // Target supply is optional (absent on every schemaVersion-1 archive), but a book that
        // states one must state a real one. Zero or negative is not "unset" - it is a number the
        // author typed that cannot be minted, and it would otherwise sit in the UI looking deliberate.
        // Whether the target is ACHIEVABLE is deliberately not checked here: that needs
        // UniqueSpace.Count, which enumerates, and Validator must stay cheap enough to run on open.
        if (cb.Manifest.TargetSupply is { } target && target < 1)
            problems.Add($"CookBook target supply must be at least 1 (found {target}).");

        // Canvas is the single source of truth for every image size. A non-positive dimension
        // is an impossible collection — ImageSharp cannot allocate a zero- or negative-sized
        // image — and left unchecked it surfaces only as a confusing per-variant size mismatch
        // (or an allocation throw deep in Compositor). The GUI feeds this straight from user
        // input, so reject it here where the message can name the real problem.
        if (cb.Manifest.Canvas.Width <= 0 || cb.Manifest.Canvas.Height <= 0)
            problems.Add($"CookBook canvas is {cb.Manifest.Canvas.Width}x{cb.Manifest.Canvas.Height}; "
                + "both canvas dimensions must be positive.");

        // A weight of zero shelves a recipe without deleting it, which is legitimate. A NEGATIVE
        // weight is not: the roller accumulates weights in order and returns the first entry whose
        // running total passes the sample, so a negative one silently steals the share of the
        // entries after it and makes the counted space unreachable.
        foreach (var (id, weight) in cb.Manifest.RecipeWeights)
            if (!double.IsFinite(weight))
                problems.Add($"Recipe '{id}' has a non-finite recipe weight ({Num(weight)}); "
                    + "weights must be a finite number, zero or greater.");
            else if (weight < 0)
                problems.Add($"Recipe '{id}' has a negative recipe weight ({Num(weight)}); "
                    + "weights must be zero or greater, and zero means never rolled.");

        var recipeIds = cb.Recipes.Select(r => r.Manifest.Id).ToHashSet();
        foreach (var id in cb.Manifest.RecipeWeights.Keys)
            if (!recipeIds.Contains(id))
                problems.Add($"recipeWeights references unknown recipe '{id}'.");

        foreach (var r in cb.Recipes)
            if (!cb.Manifest.RecipeWeights.ContainsKey(r.Manifest.Id))
                problems.Add($"Recipe '{r.Manifest.Id}' has no recipe weight.");
    }

    private static void CheckRecipe(List<string> problems, LoadedRecipe r, Dimensions canvas)
    {
        CheckRecipeStructure(problems, r);
        foreach (var ing in r.Ingredients)
        {
            string where = $"Ingredient '{ing.Manifest.Id}' in '{r.Manifest.Id}'";
            CheckIngredient(problems, where, ing);
            CheckVariantImagesCanvas(problems, where, ing, canvas);
        }
    }

    /// <summary>Recipe checks that need no canvas: id uniqueness, layerOrder, and rule references.</summary>
    private static void CheckRecipeStructure(List<string> problems, LoadedRecipe r)
    {
        foreach (var dup in Duplicates(r.Ingredients.Select(i => i.Manifest.Id)))
            problems.Add($"Recipe '{r.Manifest.Id}' has duplicate ingredient id '{dup}'.");

        // Built duplicate-tolerantly (last wins) so a duplicate is reported above rather than
        // thrown here; the rest of the checks still run and report what they find.
        var ingById = new Dictionary<string, LoadedIngredient>();
        foreach (var i in r.Ingredients) ingById[i.Manifest.Id] = i;

        CheckLayerOrder(problems, r, ingById);

        foreach (var rule in r.Manifest.Rules)
            foreach (var t in rule.Targets.Append(rule.When))
                if (!ingById.ContainsKey(t.IngredientId))
                    problems.Add($"Recipe '{r.Manifest.Id}' rule references unknown ingredient '{t.IngredientId}'.");
    }

    /// <summary>
    /// layerOrder and the ingredient list must name exactly the same set, each id once:
    /// generation only ever rolls layerOrder, so an ingredient missing from it is never
    /// composited, a layerOrder entry with no ingredient has nothing to roll, and a repeated
    /// entry rolls the same layer twice.
    /// </summary>
    private static void CheckLayerOrder(
        List<string> problems, LoadedRecipe r, Dictionary<string, LoadedIngredient> ingById)
    {
        // No layers means nothing to composite: every asset would be fully transparent.
        if (r.Manifest.LayerOrder.Count == 0)
            problems.Add($"Recipe '{r.Manifest.Id}' has an empty layerOrder, so it would "
                + "generate a fully-transparent asset.");

        foreach (var layerId in r.Manifest.LayerOrder)
            if (!ingById.ContainsKey(layerId))
                problems.Add($"Recipe '{r.Manifest.Id}' layerOrder references unknown ingredient '{layerId}'.");

        // A repeated entry rolls that layer once per appearance: the asset gets two selections
        // for one trait category, its rarity is counted twice, and only the last roll is what
        // the incompatibility rules ever see. There is no reading of a repeat that makes sense,
        // so it is rejected rather than given semantics.
        foreach (var dup in Duplicates(r.Manifest.LayerOrder))
            problems.Add($"Recipe '{r.Manifest.Id}' lists ingredient '{dup}' more than once in its "
                + "layerOrder, so that layer would be rolled more than once per asset.");

        var ordered = r.Manifest.LayerOrder.ToHashSet(StringComparer.Ordinal);
        foreach (var ing in r.Ingredients)
            if (!ordered.Contains(ing.Manifest.Id))
                problems.Add($"Recipe '{r.Manifest.Id}' has ingredient '{ing.Manifest.Id}' "
                    + "that is missing from its layerOrder, so it is never rolled.");
    }

    /// <summary>The canvas-independent per-ingredient checks. <paramref name="where"/> carries the
    /// caller's context ("Ingredient 'x'" standalone, or "…in 'r'" inside a recipe).</summary>
    private static void CheckIngredient(List<string> problems, string where, LoadedIngredient ing)
    {
        if (ing.Manifest.Variants.Count == 0)
            problems.Add($"{where} has no variants.");
        if (ing.Manifest.Variants.Sum(v => v.Weight) <= 0)
            problems.Add($"{where} has zero total variant weight.");
        foreach (var dup in Duplicates(ing.Manifest.Variants.Select(v => v.Id)))
            problems.Add($"{where} has duplicate variant id '{dup}'.");

        // Zero is a legitimate way to shelve a variant; negative is not — see CheckCookBook.
        foreach (var v in ing.Manifest.Variants)
            if (!double.IsFinite(v.Weight))
                problems.Add($"{where} has variant '{v.Id}' with a non-finite weight ({Num(v.Weight)}); "
                    + "weights must be a finite number, zero or greater.");
            else if (v.Weight < 0)
                problems.Add($"{where} has variant '{v.Id}' with a negative weight ({Num(v.Weight)}); "
                    + "weights must be zero or greater, and zero means never rolled.");

        CheckKind(problems, where, ing.Manifest.Kind, ing.Manifest.Colorization);
        CheckColorEntries(problems, where, ing.Manifest.Colorization);
        CheckVariantImagesPresentAndGray(problems, where, ing);
    }

    /// <summary>Each layer kind admits exactly one shape of colorization — see spec 4.1.</summary>
    private static void CheckKind(List<string> problems, string where, LayerKind kind, Colorization? col)
    {
        switch (kind)
        {
            case LayerKind.Custom:
                // Composited as-is; must NEVER carry a colorization.
                if (col is not null)
                    problems.Add($"{where} is custom but has a colorization; custom layers must have none.");
                break;

            case LayerKind.Static:
                // Colorized with exactly one fixed color, deterministically.
                if (col is null)
                    problems.Add($"{where} is static but has no colorization; static requires exactly one fixed color.");
                else if (col.Entries.Count != 1)
                    problems.Add($"{where} is static but has {col.Entries.Count} colorization entries; static requires exactly one fixed color.");
                else if (col.Entries[0].Fixed is null || col.Entries[0].Range is not null)
                    problems.Add($"{where} is static but its colorization entry is not a single fixed color (no ranges allowed).");
                break;

            case LayerKind.Dynamic:
                // Value-map rolled from one or more weighted entries.
                if (col is null)
                {
                    problems.Add($"{where} is dynamic but has no colorization.");
                    break;
                }

                // Spec 4.1: dynamic needs >=1 entries. Spec 5.1: non-zero total weight.
                // Without both, the roll has nothing to pick from.
                if (col.Entries.Count == 0)
                    problems.Add($"{where} is dynamic but has no colorization entries; dynamic requires at least one.");
                else if (col.Entries.Sum(e => e.Weight) <= 0)
                    problems.Add($"{where} is dynamic but its colorization entries have zero total weight.");

                foreach (var entry in col.Entries)
                    if ((entry.Fixed is null) == (entry.Range is null))
                        problems.Add($"{where} has a colorization entry that must have exactly one of fixed or range.");
                break;
        }
    }

    /// <summary>Checks every entry's payload, whichever kind declared it.</summary>
    private static void CheckColorEntries(List<string> problems, string where, Colorization? col)
    {
        if (col is null) return;

        // Dna.cs and UniqueSpace.cs both floor a non-positive quantize up to 1 via Math.Max(1, q)
        // so the count-versus-generate promise stays intact — but that coercion is silent, and a
        // quantize of 0 is a plausible hand-edit or a GUI spinner bottoming out. Left unreported,
        // the author asked for one bucket width and generation silently used another. Report it
        // here instead, since Validator is the one place that decides what a legal book is.
        if (col.HueQuantize <= 0)
            problems.Add($"{where} has a hueQuantize of {col.HueQuantize}; it must be a positive "
                + "integer (the bucket width in hue degrees). A value of zero or less is silently "
                + "treated as 1 during generation, producing a far finer colour space than the "
                + "author most likely intended.");
        if (col.SatQuantize <= 0)
            problems.Add($"{where} has a satQuantize of {col.SatQuantize}; it must be a positive "
                + "integer (the bucket width in saturation percent). A value of zero or less is "
                + "silently treated as 1 during generation, producing a far finer colour space "
                + "than the author most likely intended.");

        foreach (var entry in col.Entries)
        {
            // Zero is a legitimate way to shelve a colour entry; negative is not — see CheckCookBook.
            if (!double.IsFinite(entry.Weight))
                problems.Add($"{where} has a colorization entry with a non-finite weight "
                    + $"({Num(entry.Weight)}); weights must be a finite number, zero or greater.");
            else if (entry.Weight < 0)
                problems.Add($"{where} has a colorization entry with a negative weight "
                    + $"({Num(entry.Weight)}); weights must be zero or greater, and zero means never rolled.");

            // Ranges are sampled ascending and never wrap, so an inverted or out-of-axis range
            // is author error — the roller would silently sample nonsense.
            if (entry.Range is { } range)
                CheckRange(problems, where, range);

            // Spec 9: a colour spec must carry an explicit prefix and is never guessed. Parsing
            // here is what makes `validate` catch it instead of Generate throwing FormatException later.
            if (entry.Fixed is { } spec)
            {
                try { ColorSpec.Parse(spec); }
                catch (FormatException ex)
                {
                    problems.Add($"{where} has an invalid fixed color '{spec}': {ex.Message}");
                }
            }
        }
    }

    /// <summary>Presence of every variant's image and grayscale for dynamic/static — no canvas needed.</summary>
    private static void CheckVariantImagesPresentAndGray(List<string> problems, string where, LoadedIngredient ing)
    {
        foreach (var v in ing.Manifest.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal))
        {
            if (!ing.VariantImages.TryGetValue(v.Id, out var img))
            {
                problems.Add($"{where} variant '{v.Id}' has no image.");
                continue;
            }
            if (ing.Manifest.Kind != LayerKind.Custom && !IsGrayscale(img))
                problems.Add($"{where} variant '{v.Id}' is not grayscale; "
                    + "dynamic/static value-maps must have R=G=B.");
        }
    }

    /// <summary>The canvas-dependent half: every present variant image must match the CookBook canvas.</summary>
    private static void CheckVariantImagesCanvas(
        List<string> problems, string where, LoadedIngredient ing, Dimensions canvas)
    {
        foreach (var v in ing.Manifest.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal))
        {
            if (!ing.VariantImages.TryGetValue(v.Id, out var img)) continue; // presence reported elsewhere
            if (img.Width != canvas.Width || img.Height != canvas.Height)
                problems.Add($"{where} variant '{v.Id}' has dimensions {img.Width}x{img.Height}, "
                    + $"expected canvas {canvas.Width}x{canvas.Height}.");
        }
    }

    /// <summary>
    /// Every image in the set must share one size. Without a canvas (ingredient/recipe authoring),
    /// this is the closest reachable check: images that already differ from each other can never all
    /// match a single future canvas.
    /// </summary>
    private static void CheckUniformSize(List<string> problems, string where, IEnumerable<Image<Rgba32>> images)
    {
        var sizes = images.Select(i => (i.Width, i.Height)).Distinct().ToList();
        if (sizes.Count > 1)
        {
            string list = string.Join(", ", sizes
                .OrderBy(s => s.Width).ThenBy(s => s.Height)
                .Select(s => $"{s.Width}x{s.Height}"));
            problems.Add($"{where} has variant images of differing sizes ({list}); every image must "
                + "share one size so they can all match the CookBook canvas.");
        }
    }

    /// <summary>Every pixel must have R==G==B: dynamic/static layers are grayscale value-maps
    /// colorized at generation time, so a coloured pixel here would be silently misinterpreted.</summary>
    private static bool IsGrayscale(Image<Rgba32> img)
    {
        bool gray = true;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && gray; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    if (p.R != p.G || p.G != p.B) { gray = false; break; }
                }
            }
        });
        return gray;
    }

    /// <summary>Each id that appears more than once, in first-seen order.</summary>
    /// <summary>
    /// A number as it should appear inside a problem string. Invariant, for the same reason the
    /// reports in <c>Stats/</c> are: a problem gets pasted into an issue or diffed against another
    /// machine's run, and under a comma-decimal locale the default formatting renders <c>-1.5</c>
    /// as <c>−1,5</c> — with U+2212, not an ASCII hyphen — which is neither comparable nor
    /// greppable.
    /// </summary>
    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<string> Duplicates(IEnumerable<string> ids) =>
        ids.GroupBy(id => id, StringComparer.Ordinal)
           .Where(g => g.Count() > 1)
           .Select(g => g.Key);

    /// <summary>
    /// A colorization range must run ascending and stay on its axis. Hue is 0..360 degrees and
    /// saturation 0..100 percent, both inclusive; wrap-around is not a feature (the roller
    /// samples <c>Min + r*(Max-Min)</c>, so an inverted range walks backwards off the range).
    /// </summary>
    private static void CheckRange(List<string> problems, string where, ColorRange range)
    {
        if (range.HueMin > range.HueMax)
            problems.Add($"{where} has a colorization range whose hueMin ({range.HueMin}) is "
                + $"greater than its hueMax ({range.HueMax}); ranges do not wrap around.");
        if (range.SatMin > range.SatMax)
            problems.Add($"{where} has a colorization range whose satMin ({range.SatMin}) is "
                + $"greater than its satMax ({range.SatMax}); ranges do not wrap around.");

        if (range.HueMin < 0 || range.HueMin > 360 || range.HueMax < 0 || range.HueMax > 360)
            problems.Add($"{where} has a colorization hue range {range.HueMin}..{range.HueMax} "
                + "outside the hue axis 0..360.");
        if (range.SatMin < 0 || range.SatMin > 100 || range.SatMax < 0 || range.SatMax > 100)
            problems.Add($"{where} has a colorization sat range {range.SatMin}..{range.SatMax} "
                + "outside the saturation axis 0..100.");
    }
}
