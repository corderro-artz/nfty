using System.CommandLine;
using Nfty.Cli;

// Parse first, so --verbose is known even when the command itself throws.
var parse = CommandFactory.Build().Parse(args);

// System.CommandLine's own handler catches first and prints "Unhandled exception:" with a raw
// trace, so ErrorReport never sees the exception unless this is off.
var invocation = new InvocationConfiguration { EnableDefaultExceptionHandler = false };

try
{
    return parse.Invoke(invocation);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ErrorReport.Format(ex, parse.GetValue(CommandFactory.VerboseOption)));
    return 1;
}
