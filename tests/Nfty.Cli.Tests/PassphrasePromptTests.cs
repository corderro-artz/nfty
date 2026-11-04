namespace Nfty.Cli.Tests;

/// <summary>
/// The interactive passphrase prompt, driven key by key through its key-source seam: nothing is
/// echoed but the prompt, Backspace edits, Escape cancels, and a sealing confirmation must match.
/// </summary>
public class PassphrasePromptTests
{
    private static Func<ConsoleKeyInfo> Keys(params ConsoleKeyInfo[] keys)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        return () => queue.Dequeue();
    }

    private static ConsoleKeyInfo Ch(char c) => new(c, ConsoleKey.A, false, false, false);
    private static ConsoleKeyInfo K(ConsoleKey key) => new('\0', key, false, false, false);
    private static ConsoleKeyInfo[] Typed(string text) => text.Select(Ch).ToArray();

    [Fact]
    public void Typed_keys_are_read_and_nothing_but_the_prompt_is_echoed()
    {
        using var echo = new StringWriter();
        string got = Passphrase.ReadMasked("Passphrase", Keys([.. Typed("hunter 2"), K(ConsoleKey.Enter)]), echo);

        Assert.Equal("hunter 2", got);
        Assert.Equal("Passphrase: " + Environment.NewLine, echo.ToString());
    }

    [Fact]
    public void Backspace_removes_the_last_character_and_is_harmless_on_an_empty_line()
    {
        using var echo = new StringWriter();
        string got = Passphrase.ReadMasked("P", Keys(
            K(ConsoleKey.Backspace), Ch('a'), Ch('b'), K(ConsoleKey.Backspace), Ch('c'), K(ConsoleKey.Enter)), echo);

        Assert.Equal("ac", got);
    }

    [Fact]
    public void Control_characters_are_not_typed()
    {
        using var echo = new StringWriter();
        string got = Passphrase.ReadMasked("P", Keys(
            Ch('a'), new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), Ch('b'), K(ConsoleKey.Enter)), echo);

        Assert.Equal("ab", got);
    }

    [Fact]
    public void Escape_cancels()
    {
        using var echo = new StringWriter();
        Assert.Throws<OperationCanceledException>(
            () => Passphrase.ReadMasked("P", Keys(Ch('a'), K(ConsoleKey.Escape)), echo));
    }

    [Fact]
    public void An_empty_entry_is_refused()
    {
        using var echo = new StringWriter();
        var ex = Assert.Throws<InvalidOperationException>(
            () => Passphrase.ReadPrompt("P", confirm: false, Keys(K(ConsoleKey.Enter)), echo));
        Assert.Equal("No passphrase was entered.", ex.Message);
    }

    [Fact]
    public void A_matching_confirmation_returns_the_passphrase()
    {
        using var echo = new StringWriter();
        string got = Passphrase.ReadPrompt("P", confirm: true,
            Keys([.. Typed("pw"), K(ConsoleKey.Enter), .. Typed("pw"), K(ConsoleKey.Enter)]), echo);

        Assert.Equal("pw", got);
        Assert.Contains("Confirm passphrase: ", echo.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mismatched_confirmation_writes_nothing()
    {
        using var echo = new StringWriter();
        var ex = Assert.Throws<InvalidOperationException>(() => Passphrase.ReadPrompt("P", confirm: true,
            Keys([.. Typed("pw"), K(ConsoleKey.Enter), .. Typed("pX"), K(ConsoleKey.Enter)]), echo));
        Assert.Contains("did not match", ex.Message, StringComparison.Ordinal);
    }
}
