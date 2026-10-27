namespace Nfty.App.Services;

/// <summary>How the app words a list of validation problems for a person: one per line.</summary>
public static class Problems
{
    /// <summary>Joins problems one per line, the way every card and tooltip that lists them does.</summary>
    /// <param name="problems">The Validator's sentences, verbatim.</param>
    /// <returns>The text.</returns>
    public static string Join(IEnumerable<string> problems) => string.Join("\n", problems);
}
