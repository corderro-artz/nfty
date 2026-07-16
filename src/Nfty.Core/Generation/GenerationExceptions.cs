namespace Nfty.Core.Generation;

/// <summary>
/// A recipe's incompatibility rules exclude every combination of its variants, so no asset
/// of that type can ever be rolled. Distinct from <see cref="UniqueSpaceExhaustedException"/>,
/// which means the space is real but too small — this means the space is empty.
/// </summary>
public class RuleConflictException : InvalidOperationException
{
    public IReadOnlyList<string> RecipeIds { get; }

    public RuleConflictException(IReadOnlyList<string> recipeIds, string message)
        : base(message) => RecipeIds = recipeIds;
}

/// <summary>
/// More unique assets were requested than the cookbook can produce. <see cref="Available"/> is
/// the true maximum when <see cref="IsExact"/>; otherwise the space was too large to count and
/// the real figure is greater than <see cref="Available"/> — meaning the reroll budget, not the
/// space, is what ran out.
/// </summary>
public class UniqueSpaceExhaustedException : InvalidOperationException
{
    public long Available { get; }
    public bool IsExact { get; }
    public int Requested { get; }
    public int Produced { get; }

    public UniqueSpaceExhaustedException(
        long available, bool isExact, int requested, int produced, string message)
        : base(message)
    {
        Available = available;
        IsExact = isExact;
        Requested = requested;
        Produced = produced;
    }
}
