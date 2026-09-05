using Nfty.Core.Model;

namespace Nfty.Core.Editing;

/// <summary>
/// Authoring for a recipe's incompatibility rules: add, replace, remove. Pure — every method
/// returns a NEW <see cref="RecipeManifest"/> and mutates nothing, the same shape
/// <see cref="LayerDepth"/> and <see cref="CookBookEdits"/> use, so a caller can hold the previous
/// manifest for undo without defensive copying.
///
/// <para>Until this existed a rule could not be created or edited anywhere in the product — not in
/// the GUI, not in the CLI. Every creation path wrote an empty rule list, and the only way a recipe
/// got a rule was hand-editing the manifest JSON inside the archive.</para>
///
/// <para><b>A rule is addressed by INDEX</b>, not by an id, because the manifest has no rule id and
/// adding a required one would be a real schema migration for a field that buys nothing at
/// generation time. The index is the position in <see cref="RecipeManifest.Rules"/>, which is
/// stable for as long as the caller holds one manifest — which is exactly how long an edit
/// lasts.</para>
/// </summary>
public static class RuleEdits
{
    /// <summary>Appends a rule.</summary>
    /// <param name="recipe">The recipe to add to.</param>
    /// <param name="rule">The rule. Rejected if degenerate (see <see cref="Validate"/>) or if the
    /// recipe already carries the same one.</param>
    /// <returns>A new manifest with the rule appended.</returns>
    /// <exception cref="ArgumentException">The rule is degenerate or a duplicate.</exception>
    public static RecipeManifest Add(RecipeManifest recipe, IncompatibilityRule rule)
    {
        Validate(rule);
        if (recipe.Rules.Any(r => AreSame(r, rule)))
            throw new ArgumentException(
                $"Recipe '{recipe.Id}' already carries this rule. A second copy would constrain "
                + "nothing further.");

        return recipe with { Rules = recipe.Rules.Append(rule).ToList() };
    }

    /// <summary>Replaces the rule at <paramref name="index"/>.</summary>
    /// <param name="recipe">The recipe to edit.</param>
    /// <param name="index">Zero-based position in <see cref="RecipeManifest.Rules"/>.</param>
    /// <param name="rule">The replacement.</param>
    /// <returns>A new manifest with that one rule swapped, every other rule and its position
    /// untouched.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No rule at that index.</exception>
    /// <exception cref="ArgumentException">The rule is degenerate, or duplicates a DIFFERENT rule
    /// in the same recipe — replacing a rule with itself is allowed, since that is what an edit
    /// that changes nothing looks like.</exception>
    public static RecipeManifest ReplaceAt(RecipeManifest recipe, int index, IncompatibilityRule rule)
    {
        RequireIndex(recipe, index);
        Validate(rule);
        for (int i = 0; i < recipe.Rules.Count; i++)
            if (i != index && AreSame(recipe.Rules[i], rule))
                throw new ArgumentException(
                    $"Recipe '{recipe.Id}' already carries this rule at position {i + 1}.");

        var rules = recipe.Rules.ToList();
        rules[index] = rule;
        return recipe with { Rules = rules };
    }

    /// <summary>Removes the rule at <paramref name="index"/>.</summary>
    /// <param name="recipe">The recipe to edit.</param>
    /// <param name="index">Zero-based position in <see cref="RecipeManifest.Rules"/>.</param>
    /// <returns>A new manifest without it. Every later rule shifts down one, which is why a caller
    /// holding indexes across a removal must re-read them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">No rule at that index.</exception>
    public static RecipeManifest RemoveAt(RecipeManifest recipe, int index)
    {
        RequireIndex(recipe, index);
        var rules = recipe.Rules.ToList();
        rules.RemoveAt(index);
        return recipe with { Rules = rules };
    }

    /// <summary>
    /// Whether two rules constrain the same thing. Targets compare as a SET: a rule's targets are
    /// evaluated as a conjunction, so listing them in a different order is the same rule written
    /// twice, and duplicate detection that missed that would be no detection at all.
    /// </summary>
    /// <param name="a">One rule.</param>
    /// <param name="b">The other.</param>
    /// <returns>True when they are the same constraint.</returns>
    public static bool AreSame(IncompatibilityRule a, IncompatibilityRule b) =>
        a.Type == b.Type
        && a.When == b.When
        && a.Targets.Count == b.Targets.Count
        && a.Targets.All(t => b.Targets.Contains(t));

    /// <summary>
    /// Rejects a rule that cannot mean anything. This is the authoring half of a pair:
    /// <c>Validator</c> reports the same two shapes on a book that already has them, since before
    /// this class existed a rule could only be written by hand and a hand-written one can be
    /// anything. Refusing to WRITE one costs nothing; refusing to COOK one is justified because
    /// each silently changes the size of the DNA space.
    /// </summary>
    /// <param name="rule">The rule to check.</param>
    /// <exception cref="ArgumentException">The rule is degenerate.</exception>
    public static void Validate(IncompatibilityRule rule)
    {
        // A rule with nothing on the other side never fires: RulesEngine loops the targets, and an
        // empty loop rejects nothing.
        if (rule.Targets.Count == 0)
            throw new ArgumentException(
                "A rule needs at least one target — with none, there is nothing for the trigger to "
                + "forbid or require and the rule can never fire.");

        // Every layer rolls exactly ONE variant, so a rule pointing a layer at itself is always
        // degenerate — and degenerate in two opposite directions, which is what makes it worth
        // refusing rather than warning about. Exclude(bg:day, bg:day) bans bg:day from the whole
        // collection; Exclude(bg:day, bg:night) can never fire at all. Neither is what anyone means.
        foreach (var t in rule.Targets)
            if (string.Equals(t.IngredientId, rule.When.IngredientId, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"A rule cannot constrain layer '{t.IngredientId}' against itself: one layer "
                    + "rolls exactly one variant, so the rule would either ban that variant "
                    + "outright or never fire at all.");

        // The same target twice is a conjunction with itself. Harmless to evaluate, meaningless to
        // store, and it makes two rules that ARE the same compare as different.
        var seen = new HashSet<RuleTarget>();
        foreach (var t in rule.Targets)
            if (!seen.Add(t))
                throw new ArgumentException(
                    $"Target '{t.IngredientId}:{t.VariantId}' is listed twice in one rule.");
    }

    private static void RequireIndex(RecipeManifest recipe, int index)
    {
        if (index < 0 || index >= recipe.Rules.Count)
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Recipe '{recipe.Id}' has {recipe.Rules.Count} rule(s); there is none at "
                + $"position {index + 1}.");
    }
}
