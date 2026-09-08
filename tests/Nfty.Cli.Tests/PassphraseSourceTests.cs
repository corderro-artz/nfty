using Nfty.Cli;

namespace Nfty.Cli.Tests;

/// <summary>
/// Where <c>--key</c> reads a passphrase from.
/// </summary>
/// <remarks>
/// <para><b>This file exists because the passphrase path used to be one environment variable and an
/// interactive prompt, and the prompt cannot be tested at all.</b> Naming the source explicitly
/// turned three of the four into ordinary functions with inputs and outputs — so the untestable
/// surface is now only the genuinely interactive one, rather than half the feature.</para>
///
/// <para>These are CLI behavior rather than engine behavior, which is why they live here beside
/// <c>ErrorReport</c>'s tests rather than in <c>Nfty.Core.Tests</c>.</para>
/// </remarks>
public class PassphraseSourceTests
{
    private const string Pass = "correct-horse-battery-staple";

    private static string TempFile(string contents)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-key-").FullName, "key.txt");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void An_environment_variable_is_read_by_name()
    {
        Environment.SetEnvironmentVariable("NFTY_TEST_KEY", Pass);
        try
        {
            Assert.Equal(Pass, Passphrase.Read("env:NFTY_TEST_KEY", "p"));
        }
        finally { Environment.SetEnvironmentVariable("NFTY_TEST_KEY", null); }
    }

    [Fact]
    public void An_unset_variable_says_which_one_rather_than_returning_nothing()
    {
        // Returning empty would reach Seal as a too-short passphrase and be reported as a passphrase
        // problem, which is the wrong sentence for a typo in a variable name.
        var ex = Assert.Throws<InvalidOperationException>(
            () => Passphrase.Read("env:NFTY_DEFINITELY_UNSET", "p"));

        Assert.Contains("NFTY_DEFINITELY_UNSET", ex.Message);
        Assert.Contains("unset or empty", ex.Message);
    }

    [Fact]
    public void A_file_gives_up_its_first_line()
    {
        Assert.Equal(Pass, Passphrase.Read("file:" + TempFile(Pass), "p"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void A_trailing_newline_is_not_part_of_the_passphrase(string ending)
    {
        // THE defect this helper exists to prevent. Every editor and every `echo` writes one, and a
        // passphrase that silently gained a newline seals a file its own author cannot open - with
        // "either the passphrase is wrong, or the file was changed" as the only symptom, which is
        // the one message that cannot tell you which.
        Assert.Equal(Pass, Passphrase.Read("file:" + TempFile(Pass + ending), "p"));
    }

    [Fact]
    public void Spaces_inside_and_in_front_are_kept()
    {
        // The mirror of the rule above: a space is a legitimate passphrase character, so trimming
        // it would be the same bug in different clothes. Only the line ENDING goes.
        const string spaced = "  four words with spaces ";
        Assert.Equal(spaced, Passphrase.Read("file:" + TempFile(spaced + "\n"), "p"));
    }

    [Fact]
    public void A_missing_or_empty_file_says_so()
    {
        var missing = Path.Combine(Path.GetTempPath(), "nfty-not-here-" + Guid.NewGuid());
        Assert.Contains("not there",
            Assert.Throws<InvalidOperationException>(() => Passphrase.Read("file:" + missing, "p")).Message);

        Assert.Contains("empty",
            Assert.Throws<InvalidOperationException>(
                () => Passphrase.Read("file:" + TempFile(""), "p")).Message);
    }

    [Fact]
    public void Standard_input_gives_up_one_line()
    {
        var previous = Console.In;
        try
        {
            Console.SetIn(new StringReader(Pass + "\nand a second line nobody asked for\n"));
            Assert.Equal(Pass, Passphrase.Read("stdin", "p"));
        }
        finally { Console.SetIn(previous); }
    }

    [Theory]
    [InlineData("NFTY_KEY")]          // the shape of the option this replaced
    [InlineData("hunter2hunter2")]    // the passphrase itself, typed straight in
    [InlineData("environment:X")]
    [InlineData("")]
    public void An_unprefixed_or_unknown_source_is_refused_rather_than_guessed(string spec)
    {
        // The rule ColorSpec sets for every color a user types, applied to the one input where
        // guessing would be worst. `--key hunter2` looks like it works; treating it as the
        // passphrase would put the secret in argv - the exact thing there is no --passphrase for.
        var ex = Assert.Throws<InvalidOperationException>(() => Passphrase.Read(spec, "p"));

        Assert.Contains("does not name a passphrase source", ex.Message);
        Assert.Contains("env:NAME", ex.Message);
        Assert.Contains("file:PATH", ex.Message);
        Assert.Contains("stdin", ex.Message);
    }

    [Theory]
    [InlineData("env:")]
    [InlineData("file:")]
    public void A_source_with_nothing_after_the_prefix_says_what_is_missing(string spec)
    {
        Assert.Throws<InvalidOperationException>(() => Passphrase.Read(spec, "p"));
    }

    [Fact]
    public void With_no_terminal_the_prompt_says_to_name_a_source_instead()
    {
        // Under a test runner, and under CI, stdin is redirected - so this is the branch a scripted
        // caller actually meets when they forget --key. It has to name the alternatives rather than
        // say "no terminal" and stop.
        var ex = Assert.Throws<InvalidOperationException>(() => Passphrase.Read(null, "p"));

        Assert.Contains("--key", ex.Message);
        Assert.Contains("env:NAME", ex.Message);
    }

    [Fact]
    public void The_prompt_is_nameable_explicitly_so_inspect_can_tell_it_apart_from_no_key_at_all()
    {
        // `inspect x.tin` with no --key prints the header and stops; `--key prompt` is how you say
        // "ask me". Without a name for it there would be no way to distinguish the two, which is
        // what the old boolean --key was for.
        Assert.Throws<InvalidOperationException>(() => Passphrase.Read("prompt", "p"));
    }
}
