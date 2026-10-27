using System.CommandLine;
using Nfty.Core;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>--threads</c> and <c>NFTY_THREADS</c>: how wide the CLI's heavy work runs. The count never
/// reaches the output, so the end-to-end test compares two whole Sets byte for byte.
/// </summary>
public class ThreadsOptionTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static (int code, string outText, string errText) Capture(params string[] args)
    {
        var prevOut = Console.Out;
        var prevErr = Console.Error;
        using var outW = new StringWriter();
        using var errW = new StringWriter();
        Console.SetOut(outW);
        Console.SetError(errW);
        int code;
        try { code = Run(args); }
        finally { Console.SetOut(prevOut); Console.SetError(prevErr); }
        return (code, outW.ToString(), errW.ToString());
    }

    /// <summary>Pins the logical processor count and the environment variable for one test.</summary>
    private static T With<T>(int logical, string? env, Func<T> body)
    {
        var prevEnv = Environment.GetEnvironmentVariable(Parallelism.EnvironmentVariable);
        var prevProc = Parallelism.ProcessorOverride;
        Parallelism.ProcessorOverride = () => logical;
        Environment.SetEnvironmentVariable(Parallelism.EnvironmentVariable, env);
        try { return body(); }
        finally
        {
            Parallelism.ProcessorOverride = prevProc;
            Environment.SetEnvironmentVariable(Parallelism.EnvironmentVariable, prevEnv);
        }
    }

    private static Parallelism Resolve(string line) =>
        CommandFactory.ResolveThreads(CommandFactory.Build().Parse(line));

    [Fact]
    public void Threads_defaults_to_two_thirds_of_the_machine()
    {
        var p = With(9, null, () => Resolve("inspect a.cbk"));
        Assert.Equal(6, p.Requested);
    }

    [Fact]
    public void The_help_names_the_default_and_the_machine()
    {
        CommandFactory.Build();
        var text = CommandFactory.ThreadsOption.Description!;
        Assert.Contains($"default: {Parallelism.DefaultDegree} of {Parallelism.LogicalProcessors} logical processors", text);
    }

    [Fact]
    public void Threads_max_means_every_logical_processor()
    {
        Assert.Equal(Parallelism.All, With(4, null, () => Resolve("inspect a.cbk --threads max")));
        Assert.Equal(Parallelism.All, With(4, null, () => Resolve("inspect a.cbk --threads 0")));
    }

    [Fact]
    public void Threads_above_the_logical_count_are_clamped_and_named()
    {
        var prevErr = Console.Error;
        using var err = new StringWriter();
        Console.SetError(err);
        int effective;
        try { effective = With(4, null, () => Resolve("inspect a.cbk --threads 64").Effective); }
        finally { Console.SetError(prevErr); }

        Assert.Equal(4, effective);
        Assert.Equal($"--threads 64 exceeds 4 logical processors; using 4.{Environment.NewLine}", err.ToString());
    }

    [Fact]
    public void A_count_within_the_machine_says_nothing()
    {
        var prevErr = Console.Error;
        using var err = new StringWriter();
        Console.SetError(err);
        try { With(4, null, () => Resolve("inspect a.cbk --threads 3")); }
        finally { Console.SetError(prevErr); }
        Assert.Equal("", err.ToString());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("two")]
    [InlineData("1.5")]
    public void A_negative_thread_count_is_a_parse_error(string value)
    {
        var result = CommandFactory.Build().Parse(new[] { "inspect", "a.cbk", "--threads", value });
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void The_option_works_on_every_command_that_does_heavy_work()
    {
        foreach (var line in new[]
        {
            "generate b.cbk --out o --count 1 --threads 2", "extend b.cbk o --to 5 --threads 2",
            "menu b.cbk --out g.mnu --threads 2", "export s.set --out o --threads 2",
            "inspect b.cbk --voxel --threads 2",
        })
            Assert.Empty(CommandFactory.Build().Parse(line).Errors);
    }

    [Fact]
    public void NFTY_THREADS_is_read_only_when_the_option_is_absent()
    {
        Assert.Equal(2, With(8, "2", () => Resolve("inspect a.cbk")).Requested);
        Assert.Equal(3, With(8, "2", () => Resolve("inspect a.cbk --threads 3")).Requested);
        Assert.Equal(Parallelism.All, With(8, "max", () => Resolve("inspect a.cbk")));
    }

    [Theory]
    [InlineData("many")]
    [InlineData("-2")]
    public void An_invalid_NFTY_THREADS_is_an_error_naming_the_variable(string env)
    {
        var ex = Assert.Throws<ArgumentException>(() => With(8, env, () => Resolve("inspect a.cbk")));
        Assert.Contains("NFTY_THREADS", ex.Message);
        Assert.Contains(env, ex.Message);
    }

    [Fact]
    public void An_invalid_NFTY_THREADS_does_not_fail_when_the_option_is_given()
    {
        Assert.Equal(2, With(8, "many", () => Resolve("inspect a.cbk --threads 2")).Requested);
    }

    [Fact]
    public void Generate_at_one_and_max_threads_writes_identical_sets()
    {
        var root = Directory.CreateTempSubdirectory("nfty-threads-").FullName;
        try
        {
            Assert.Equal(0, Run("demo", root));
            var book = Path.Combine(root, "ChestDemo.cbk");
            var one = Path.Combine(root, "one");
            var max = Path.Combine(root, "max");
            var (c1, _, e1) = Capture("generate", book, "--out", one, "--count", "6", "--seed", "t8", "--threads", "1");
            var (c2, _, e2) = Capture("generate", book, "--out", max, "--count", "6", "--seed", "t8", "--threads", "max");
            Assert.True(c1 == 0, e1);
            Assert.True(c2 == 0, e2);

            var a = Directory.GetFiles(one, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(one, f)).Order(StringComparer.Ordinal).ToList();
            var b = Directory.GetFiles(max, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(max, f)).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(a, b);
            Assert.NotEmpty(a);
            foreach (var rel in a)
                Assert.True(File.ReadAllBytes(Path.Combine(one, rel)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(max, rel))), rel);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
