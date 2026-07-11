using Nfty.Core.Model;

namespace Nfty.Core.Generation;

public static class RulesEngine
{
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
