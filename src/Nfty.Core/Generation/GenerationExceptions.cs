namespace Nfty.Core.Generation;

/// <summary>
/// A recipe's incompatibility rules exclude every combination of its variants, so no asset
/// of that type can ever be rolled. Distinct from <see cref="UniqueSpaceExhaustedException"/>,
/// which means the space is real but too small — this means the space is empty.
/// </summary>
public class RuleConflictException : InvalidOperationException
{
    /// <summary>The recipes whose rules exclude every combination.</summary>
    public IReadOnlyList<string> RecipeIds { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="recipeIds">The recipes with an empty space.</param>
    /// <param name="message">The message shown to the user verbatim.</param>
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
    /// <summary>How many unique DNA the book actually admits.</summary>
    public long Available { get; }
    /// <summary>Whether <see cref="Available"/> is the real figure or a floor.</summary>
    public bool IsExact { get; }
    /// <summary>How many assets were asked for.</summary>
    public int Requested { get; }
    /// <summary>How many were produced before the space ran out.</summary>
    public int Produced { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="available">The space the book admits.</param>
    /// <param name="isExact">Whether that figure is exact.</param>
    /// <param name="requested">How many were asked for.</param>
    /// <param name="produced">How many were produced.</param>
    /// <param name="message">The message shown to the user verbatim.</param>
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
