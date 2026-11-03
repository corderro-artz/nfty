using System.CommandLine;
using Nfty.Cli;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>ipfs status</c> and <c>export --upload</c>: they parse, the Pinata key is only ever NAMED,
/// and every option that would do nothing is rejected rather than ignored. Nothing here reaches a
/// network: every case fails before a request is made.
/// </summary>
public class IpfsCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    [Fact]
    public void Status_parses_with_and_without_a_provider()
    {
        Assert.Empty(Parse("ipfs status").Errors);
        Assert.Empty(Parse("ipfs status --api http://127.0.0.1:5002").Errors);
        Assert.Empty(Parse("ipfs status --pinata --pinata-key env:PINATA_JWT").Errors);
    }

    [Theory]
    [InlineData("ipfs status --pinata --api http://127.0.0.1:5001", "Choose one")]
    [InlineData("ipfs status --pinata-key env:X", "needs --pinata")]
    [InlineData("ipfs status --api 127.0.0.1:5001", "http address")]
    public void Status_rejects_what_would_do_nothing(string line, string expected)
    {
        var errors = Parse(line).Errors;
        Assert.Contains(errors, e => e.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void A_pinata_key_typed_on_the_command_line_is_refused_not_used()
    {
        var parse = Parse("ipfs status --pinata --pinata-key hunter2");
        Assert.Empty(parse.Errors);
        var ex = Assert.Throws<InvalidOperationException>(() => parse.Invoke(NonThrowing));
        Assert.Contains("does not name a --pinata-key source", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pinata-key hunter2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unset_key_variable_is_named()
    {
        var parse = Parse("ipfs status --pinata --pinata-key env:NFTY_TEST_NO_SUCH_VAR_9f2");
        var ex = Assert.Throws<InvalidOperationException>(() => parse.Invoke(NonThrowing));
        Assert.Contains("--pinata-key named the environment variable 'NFTY_TEST_NO_SUCH_VAR_9f2'", ex.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--api http://127.0.0.1:5001", "needs --upload")]
    [InlineData("--pinata", "needs --upload")]
    [InlineData("--upload --seal", "--seal cannot go with it")]
    [InlineData("--upload --art-only", "--art-only cannot go with it")]
    [InlineData("--upload --image-base-uri ipfs://x/", "--image-base-uri cannot go with it")]
    public void Export_upload_options_are_rejected_where_they_mean_nothing(string extra, string expected)
    {
        var errors = Parse("export set --out out " + extra).Errors;
        Assert.Contains(errors, e => e.Message.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Export_upload_parses()
    {
        Assert.Empty(Parse("export set --out out --preset marketplace --upload").Errors);
        Assert.Empty(Parse("export set --out out --upload --pinata --pinata-key file:jwt.txt").Errors);
    }
}
