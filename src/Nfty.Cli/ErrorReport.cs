using System.Text;

namespace Nfty.Cli;

/// <summary>
/// Turns an exception into something worth reading. The engine's own errors already explain
/// themselves — a stack trace on top of a good message just buries it — so the trace is kept
/// behind <c>--verbose</c> for when the message alone isn't enough.
/// </summary>
public static class ErrorReport
{
    public static string Format(Exception ex, bool verbose)
    {
        var report = new StringBuilder("error: ").Append(Describe(ex));

        // The outermost message is often just context ("could not read the cookbook"); the
        // cause underneath is usually the part that says what to fix.
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            report.AppendLine().Append("  caused by: ").Append(Describe(inner));

        if (verbose)
            report.AppendLine().AppendLine().Append(ex.ToString());
        else
            report.AppendLine().Append("Run with --verbose for the full stack trace.");

        return report.ToString();
    }

    /// <summary>
    /// One exception's message, in nfty's own terms where the framework's phrasing would leak
    /// implementation detail at someone who just mistyped a path.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        FileNotFoundException { FileName: not null } e => $"No such file: {e.FileName}",
        _ => ex.Message,
    };
}
