using System.CommandLine;
using Nfty.Cli;

// System.CommandLine's own handler catches first and prints "Unhandled exception:" with a raw
// trace, so ErrorReport never sees the exception unless this is off.
var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
ParseResult? parse = null;

try
{
    // Keep parsing inside the same error boundary as invocation. A failed parse may not produce
    // a result; only a completed parse can reliably tell whether --verbose was an option or a value.
    parse = CommandFactory.Build().Parse(args);
    return parse.Invoke(invocation);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ErrorReport.Format(ex, parse?.GetValue(CommandFactory.VerboseOption) == true));
    return 1;
}
