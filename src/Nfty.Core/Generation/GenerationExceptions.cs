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
/// the true maximum only when <see cref="Certainty"/> says so; otherwise it is a bound, and
/// <see cref="Certainty"/> is what says in which direction.
/// </summary>
/// <remarks>
/// This carried a <c>bool isExact</c>, and read every non-exact outcome as "the real figure is
/// GREATER — so the reroll budget, not the space, is what ran out". One of those outcomes is the
/// opposite: a recipe with rules and more combinations than the enumeration budget reports an upper
/// bound, and telling a user their book allows "more than" a number it may not reach is worse than
/// saying nothing.
/// </remarks>
public class UniqueSpaceExhaustedException : InvalidOperationException
{
    /// <summary>How many unique DNA the book actually admits.</summary>
    public long Available { get; }
    /// <summary>What <see cref="Available"/> is: the figure, a floor, a ceiling, or nothing.</summary>
    public SpaceCertainty Certainty { get; }

    /// <summary>Whether <see cref="Available"/> is the real figure rather than a bound.</summary>
    public bool IsExact => Certainty == SpaceCertainty.Exact;
    /// <summary>How many assets were asked for.</summary>
    public int Requested { get; }
    /// <summary>How many were produced before the space ran out.</summary>
    public int Produced { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="available">The space the book admits.</param>
    /// <param name="certainty">What that figure is.</param>
    /// <param name="requested">How many were asked for.</param>
    /// <param name="produced">How many were produced.</param>
    /// <param name="message">The message shown to the user verbatim.</param>
    public UniqueSpaceExhaustedException(
        long available, SpaceCertainty certainty, int requested, int produced, string message)
        : base(message)
    {
        Available = available;
        Certainty = certainty;
        Requested = requested;
        Produced = produced;
    }
}
