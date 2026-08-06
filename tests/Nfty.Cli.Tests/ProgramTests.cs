using System.Diagnostics;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>Program.cs</c> had zero coverage while owning everything CLAUDE.md calls load-bearing about
/// error surfacing: <c>EnableDefaultExceptionHandler = false</c>, the <c>--verbose</c> trace toggle
/// and the process exit code. <see cref="ErrorReportTests"/> sat at 100% testing the formatter in
/// isolation, which is precisely the false confidence — flipping the handler flag left all 42 tests
/// green while turning "error: No such file: x" into "Unhandled exception:" plus a raw stack trace.
///
/// <para>These run the built executable rather than calling into the parser, because the wiring IS
/// the thing under test. Nothing else in the suite starts the process.</para>
/// </summary>
public class ProgramTests
{
    /// <summary>Locates the CLI built alongside this test assembly. Both projects target the same
    /// framework and configuration, so it sits at a fixed offset from here.</summary>
    private static string Exe()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        // .../tests/Nfty.Cli.Tests/bin/<config>/<tfm>/  ->  src/Nfty.Cli/bin/<config>/<tfm>/
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        string tfm = here.Name, config = here.Parent!.Name;
        string exe = Path.Combine(dir!.FullName, "src", "Nfty.Cli", "bin", config, tfm,
            OperatingSystem.IsWindows() ? "Nfty.Cli.exe" : "Nfty.Cli");
        Assert.True(File.Exists(exe), $"CLI not built at {exe}");
        return exe;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(Exe())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return (p.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// The seam itself. System.CommandLine's own handler catches first and prints "Unhandled
    /// exception:" with a trace, so ErrorReport never runs unless it is switched off — and the
    /// difference is invisible to every test that goes through the parser directly.
    /// </summary>
    [Fact]
    public void An_engine_error_is_reported_by_nfty_not_by_the_framework()
    {
        var (code, _, err) = Run("validate", "definitely-not-here.cbk");

        Assert.Equal(1, code);
        Assert.Contains("error: No such file", err);
        Assert.DoesNotContain("Unhandled exception", err);
        Assert.DoesNotContain("at Nfty.", err);            // no stack trace without --verbose
        Assert.Contains("--verbose", err);                  // ...but it says how to get one
    }

    /// <summary>--verbose is declared recursive, so it has to work AFTER the subcommand as well as
    /// before it — which is where a user actually types it.</summary>
    [Fact]
    public void Verbose_adds_the_stack_trace_and_works_after_the_subcommand()
    {
        var (code, _, err) = Run("validate", "definitely-not-here.cbk", "--verbose");

        Assert.Equal(1, code);
        Assert.Contains("error: No such file", err);
        Assert.Contains("at Nfty.", err);
    }

    [Fact]
    public void A_successful_command_exits_zero_and_writes_nothing_to_stderr()
    {
        var (code, _, err) = Run("--help");

        Assert.Equal(0, code);
        Assert.Equal("", err.Trim());
    }

    /// <summary>A CLI that returns 0 on failure breaks every script that uses it, so the codes are
    /// pinned rather than assumed.</summary>
    [Theory]
    [InlineData(1, new[] { "flibbertigibbet" })]                 // unknown command
    [InlineData(1, new string[0])]                               // no command at all
    [InlineData(1, new[] { "validate" })]                        // required argument missing
    [InlineData(0, new[] { "--version" })]
    public void Exit_codes_are_what_a_script_expects(int expected, string[] args)
    {
        var (code, _, _) = Run(args);

        Assert.Equal(expected, code);
    }

    /// <summary>A mistyped command must echo what was typed. "Required command was not provided"
    /// alone leaves the user guessing which word was wrong.</summary>
    [Fact]
    public void An_unknown_command_names_the_token_that_was_not_understood()
    {
        var (_, _, err) = Run("flibbertigibbet");

        Assert.Contains("flibbertigibbet", err);
    }
}
