using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Generation;

namespace Nfty.Cli.Tests;

public class ErrorReportTests
{
    [Fact]
    public void A_domain_error_prints_its_message_without_the_type_or_a_trace()
    {
        // Engine errors already explain themselves; a stack trace is noise on top of a good
        // message, and the exception's type name means nothing to whoever ran the command.
        var ex = new RuleConflictException(new[] { "cat" },
            "No legal variant combination exists for recipe 'cat': "
            + "the incompatibility rules exclude every combination.");

        string report = ErrorReport.Format(ex, verbose: false);

        Assert.Contains("No legal variant combination exists for recipe 'cat'", report);
        Assert.DoesNotContain("RuleConflictException", report);
        Assert.DoesNotContain("   at ", report);
    }

    [Fact]
    public void A_terse_report_points_at_the_verbose_flag()
    {
        string report = ErrorReport.Format(new InvalidOperationException("boom"), verbose: false);

        Assert.Contains("--verbose", report);
    }

    [Fact]
    public void Verbose_adds_the_trace_and_keeps_the_message()
    {
        Exception caught;
        try { throw new UniqueSpaceExhaustedException(4, true, 5, 4, "allows exactly 4 unique DNA"); }
        catch (Exception ex) { caught = ex; }   // thrown, so it has a real stack trace

        string report = ErrorReport.Format(caught, verbose: true);

        Assert.Contains("allows exactly 4 unique DNA", report);
        Assert.Contains("UniqueSpaceExhaustedException", report);
        Assert.Contains("   at ", report);
        Assert.DoesNotContain("--verbose", report);   // no point suggesting what they already did
    }

    [Fact]
    public void A_missing_file_names_the_path_rather_than_leaking_dotnet_phrasing()
    {
        var ex = new FileNotFoundException("Could not find file '/tmp/nope.cbk'.", "/tmp/nope.cbk");

        string report = ErrorReport.Format(ex, verbose: false);

        Assert.Contains("/tmp/nope.cbk", report);
        Assert.Contains("No such file", report);
    }

    [Fact]
    public void An_inner_exception_is_included_since_it_usually_holds_the_real_cause()
    {
        var ex = new InvalidOperationException("could not read the cookbook",
            new UnsupportedSchemaVersionException(2, 1, "declares schemaVersion 2"));

        string report = ErrorReport.Format(ex, verbose: false);

        Assert.Contains("could not read the cookbook", report);
        Assert.Contains("declares schemaVersion 2", report);
    }

    [Fact]
    public void Verbose_is_accepted_on_a_subcommand()
    {
        var result = CommandFactory.Build().Parse("validate book.cbk --verbose");

        Assert.Empty(result.Errors);
        Assert.True(result.GetValue(CommandFactory.VerboseOption));
    }

    [Fact]
    public void Verbose_is_off_by_default()
    {
        var result = CommandFactory.Build().Parse("validate book.cbk");

        Assert.False(result.GetValue(CommandFactory.VerboseOption));
    }
}
