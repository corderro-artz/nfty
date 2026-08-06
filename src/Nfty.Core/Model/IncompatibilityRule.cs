namespace Nfty.Core.Model;

/// <summary>What a rule does to a rolled selection.</summary>
public enum RuleType
{
    /// <summary>The targets may NOT appear alongside the trigger.</summary>
    Exclude,

    /// <summary>One of the targets MUST appear alongside the trigger.</summary>
    Require,
}

/// <summary>A specific variant of a specific layer.</summary>
/// <param name="IngredientId">The layer.</param>
/// <param name="VariantId">The variant of it.</param>
public record RuleTarget(string IngredientId, string VariantId);

/// <summary>
/// A constraint on which variants may be rolled together. Legality is a function of the variant
/// selection alone, so it is decided before any pixel is touched — a rejected roll costs only the
/// rolls themselves.
/// </summary>
/// <param name="Type">Whether the targets are forbidden or required.</param>
/// <param name="When">The trigger: the rule applies only when this variant was rolled.</param>
/// <param name="Targets">What is then forbidden or required.</param>
public record IncompatibilityRule(RuleType Type, RuleTarget When, IReadOnlyList<RuleTarget> Targets);
