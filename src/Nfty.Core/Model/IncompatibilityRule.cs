namespace Nfty.Core.Model;

/// <summary>What a rule does to a rolled selection.</summary>
public enum RuleType
{
    /// <summary>NONE of the targets may appear alongside the trigger. Any one of them present is a
    /// violation.</summary>
    Exclude,

    /// <summary>ALL of the targets must appear alongside the trigger. Any one of them missing is a
    /// violation — this is a conjunction, not a choice.</summary>
    /// <remarks>This said "one of the targets" until 2026-09-05, which is not what
    /// <c>RulesEngine.IsLegal</c> has ever done: it loops the targets and rejects on the first one
    /// absent. <c>Multi_target_and_multi_rule_selections_evaluated_correctly</c> has pinned the
    /// conjunction the whole time, so the CODE was right and this sentence was wrong — a
    /// documentation bug that shipped in the XML docs and would have taught an author the opposite
    /// of what their rule does. Nothing about generation changed when it was corrected.</remarks>
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
