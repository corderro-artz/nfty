namespace Nfty.Cli;

/// <summary>
/// Where a sealing passphrase comes from on a command line.
/// </summary>
/// <remarks>
/// <para><b>There is deliberately no <c>--passphrase</c> option.</b> An argument on a command line
/// is visible to every process on the machine for as long as the command runs, it lands in the
/// shell's history file, and it is captured verbatim by any CI log the command runs under. A secret
/// that has to be typed in front of all three is not a secret, and offering the flag would mean
/// most people used it — the convenient path is the one that gets taken.</para>
///
/// <para><b>A source is NAMED, never guessed</b>, which is the rule <c>ColorSpec</c> already sets
/// for every color a user types. <c>--key</c> takes <c>env:NAME</c>, <c>file:PATH</c>,
/// <c>stdin</c> or <c>prompt</c>, and an unprefixed value is an error rather than a guess — because
/// the alternatives are not interchangeable and picking one for the user would be picking their
/// threat model for them.</para>
///
/// <para><b>Why more than one source.</b> An environment variable is the usual answer and it is
/// what most tools stop at, but it is not the safest one available: on Linux
/// <c>/proc/&lt;pid&gt;/environ</c> is readable by the same user, every child process inherits it,
/// and anything that dumps the environment on a crash dumps it too. A file can be mode 600 and is
/// not inherited; stdin leaves nothing anywhere at all and is what a pipeline should use. The
/// prompt is the default because it leaves the least behind of any of them.</para>
/// </remarks>
public static class Passphrase
{
    /// <summary>The <c>env:</c> source — an environment variable's name.</summary>
    public const string EnvPrefix = "env:";

    /// <summary>The <c>file:</c> source — a path whose first line is the passphrase.</summary>
    public const string FilePrefix = "file:";

    /// <summary>The <c>stdin</c> source — one line from standard input.</summary>
    public const string StdinSource = "stdin";

    /// <summary>The <c>prompt</c> source — ask at the terminal, echoing nothing.</summary>
    public const string PromptSource = "prompt";

    /// <summary>What <c>--key</c> accepts, for a help string and for an error message.</summary>
    public static string Sources =>
        $"{EnvPrefix}NAME, {FilePrefix}PATH, {StdinSource} or {PromptSource}";

    /// <summary>
    /// Reads a passphrase from the source <paramref name="spec"/> names.
    /// </summary>
    /// <param name="spec">The <c>--key</c> value, or null to prompt.</param>
    /// <param name="prompt">What to ask, when prompting.</param>
    /// <param name="confirm">Ask twice and require a match. Applies to the PROMPT only: a
    /// passphrase read from a variable, a file or a pipe was not typed here, so asking for it twice
    /// would be asking the same source the same question. A mistyped one is a file nobody can ever
    /// open, its author included, which is why the typed path confirms at all.</param>
    /// <returns>The passphrase.</returns>
    /// <exception cref="InvalidOperationException">The source is unknown, or names nothing.</exception>
    public static string Read(string? spec, string prompt, bool confirm = false)
    {
        if (spec is null || spec == PromptSource) return ReadPrompt(prompt, confirm);

        if (spec.StartsWith(EnvPrefix, StringComparison.Ordinal))
        {
            var name = spec[EnvPrefix.Length..];
            if (name.Length == 0)
                throw new InvalidOperationException($"--key {EnvPrefix} needs a variable name.");
            return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException(
                    $"--key named the environment variable '{name}', which is unset or empty.");
        }

        if (spec.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            var path = spec[FilePrefix.Length..];
            if (path.Length == 0)
                throw new InvalidOperationException($"--key {FilePrefix} needs a path.");
            if (!File.Exists(path))
                throw new InvalidOperationException($"--key named the file '{path}', which is not there.");
            return FirstLine(File.ReadAllText(path))
                ?? throw new InvalidOperationException($"'{path}' is empty.");
        }

        if (spec == StdinSource)
        {
            return FirstLine(Console.In.ReadLine())
                ?? throw new InvalidOperationException("Nothing arrived on standard input.");
        }

        // Unknown prefix: an error, never a guess. The same rule ColorSpec follows, and for the same
        // reason - the sources are not interchangeable, so choosing one silently would be choosing
        // the user's threat model for them.
        throw new InvalidOperationException(
            $"'{spec}' does not name a passphrase source; expected {Sources}.");
    }

    /// <summary>
    /// The first line of <paramref name="text"/>, or null when there is not one.
    /// </summary>
    /// <remarks>
    /// <b>The trailing newline is dropped and nothing else is.</b> A key file written by
    /// <c>echo</c> or by any editor ends in one, and a passphrase that silently gained a <c>\n</c>
    /// would produce a file whose own author could not open it — with the two states this format
    /// cannot tell apart ("wrong passphrase, or altered file") as the only symptom. Interior and
    /// leading spaces are KEPT: a space is a legitimate character in a passphrase, and trimming it
    /// would be the same bug wearing different clothes.
    /// </remarks>
    private static string? FirstLine(string? text)
    {
        if (text is null) return null;
        var end = text.IndexOfAny(['\r', '\n']);
        var line = end < 0 ? text : text[..end];
        return line.Length == 0 ? null : line;
    }

    private static string ReadPrompt(string prompt, bool confirm)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException(
                "There is no terminal to ask for a passphrase at. Name a source with --key "
                + $"({Sources}).");

        string first = ReadMasked(prompt);
        if (first.Length == 0)
            throw new InvalidOperationException("No passphrase was entered.");
        if (!confirm) return first;

        // Sealing is one-way by design: nothing anywhere can recover the contents of a seal whose
        // passphrase was mistyped, so the confirmation is not politeness.
        if (ReadMasked("Confirm passphrase") != first)
            throw new InvalidOperationException("The two entries did not match; nothing was written.");
        return first;
    }

    /// <summary>
    /// Reads a line without echoing it.
    /// </summary>
    /// <remarks>
    /// Nothing is echoed at all — not even asterisks. A row of stars publishes the length of the
    /// passphrase to anyone looking over the shoulder this is meant to defeat, which is the one
    /// fact about it worth having.
    /// </remarks>
    private static string ReadMasked(string prompt)
    {
        Console.Write(prompt + ": ");
        var typed = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return typed.ToString();
                case ConsoleKey.Backspace:
                    if (typed.Length > 0) typed.Length--;
                    break;
                case ConsoleKey.Escape:
                    Console.WriteLine();
                    throw new OperationCanceledException("Canceled.");
                default:
                    if (!char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
                    break;
            }
        }
    }
}
