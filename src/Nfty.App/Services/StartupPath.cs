namespace Nfty.App.Services;

/// <summary>
/// What the process was asked to open at launch: <c>nfty book.cbk</c>, or a file dropped on the executable.
/// </summary>
public static class StartupPath
{
    /// <summary>
    /// The first argument that names something to open: the first one that is not an option.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The path as given (not required to exist: a missing one gets the ordinary error card
    /// when it is opened, which says so), or null when there is none.</returns>
    public static string? FromArguments(IEnumerable<string>? args)
    {
        if (args is null) return null;
        foreach (var a in args)
        {
            if (string.IsNullOrWhiteSpace(a) || a.StartsWith('-')) continue;
            return a;
        }
        return null;
    }

    /// <summary>
    /// Whether the process was asked to open its path in the viewer ALONE: <c>nfty --viewer x.png</c>,
    /// which is the command the Windows "open with the nfty viewer" registration runs.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>True when <c>--viewer</c> is among them.</returns>
    public static bool WantsViewer(IEnumerable<string>? args) =>
        args?.Any(a => string.Equals(a, FileAssociationRegistrar.ViewerSwitch, StringComparison.OrdinalIgnoreCase)) == true;
}
