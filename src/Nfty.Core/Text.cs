namespace Nfty.Core;

/// <summary>Small wording helpers, so a count is worded the same on every surface.</summary>
public static class Text
{
    /// <summary>A count with its noun: <c>1 recipe</c>, <c>2 recipes</c>.</summary>
    /// <remarks>Regular <c>s</c> plurals only; an irregular noun stays at its call site.</remarks>
    /// <param name="n">The count.</param>
    /// <param name="noun">The singular noun.</param>
    /// <returns>The phrase, invariant.</returns>
    public static string Count(int n, string noun) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{n} {noun}{(n == 1 ? "" : "s")}");
}
