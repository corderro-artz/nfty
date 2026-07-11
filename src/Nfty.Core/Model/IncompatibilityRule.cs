namespace Nfty.Core.Model;

public enum RuleType { Exclude, Require }

public record RuleTarget(string IngredientId, string VariantId);

public record IncompatibilityRule(RuleType Type, RuleTarget When, IReadOnlyList<RuleTarget> Targets);
