using Nfty.Core.Model;

namespace Nfty.Core.Generation;

/// <summary>Decides whether a rolled selection satisfies a recipe's incompatibility rules.</summary>
public static class RulesEngine
{
    /// <summary>Checks a selection against the rules.</summary>
    /// <param name="selection">Variant id per ingredient id.</param>
    /// <param name="rules">The recipe's rules.</param>
    /// <returns>True when no rule is violated.</returns>
    public static bool IsLegal(
        IReadOnlyDictionary<string, string> selection,
        IReadOnlyList<IncompatibilityRule> rules)
    {
        foreach (var rule in rules)
        {
            bool whenMatches = selection.TryGetValue(rule.When.IngredientId, out var chosen)
                               && chosen == rule.When.VariantId;
            if (!whenMatches) continue;

            foreach (var target in rule.Targets)
            {
                bool present = selection.TryGetValue(target.IngredientId, out var got)
                               && got == target.VariantId;
                if (rule.Type == RuleType.Exclude && present) return false;
                if (rule.Type == RuleType.Require && !present) return false;
            }
        }
        return true;
    }
}
