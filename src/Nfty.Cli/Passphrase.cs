namespace Nfty.Cli;

/// <summary>
/// Reads a sealing passphrase or Pinata credential from a named command-line source.
/// </summary>
/// <remarks>
/// <para><b>There is deliberately no <c>--passphrase</c> option.</b> An argument on a command line
/// may be visible to other processes with sufficient permissions for as long as the command runs,
/// it can land in the shell's history file, and CI command logs can capture it verbatim. A secret
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
/// <c>/proc/&lt;pid&gt;/environ</c> can be readable by the same user, child processes normally inherit it,
/// and a crash report can capture environment values. A file can have restricted permissions;
/// stdin can feed a pipeline without putting the secret in arguments or environment variables.
/// The input producer and its logs still need protection. The prompt is the default because it
/// avoids requiring a separately stored source.</para>
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
    /// Reads a nonempty secret from the source <paramref name="spec"/> names.
    /// </summary>
    /// <remarks>Environment values are read whole. Files and stdin supply their first line,
    /// without its CR/LF ending; file encoding is detected from its BOM when present. Leading,
    /// interior and trailing spaces are preserved. Empty values or first lines are refused.</remarks>
    /// <param name="spec">The named source option's value, or null to prompt.</param>
    /// <param name="prompt">What to ask, when prompting.</param>
    /// <param name="confirm">Ask twice and require a match. Applies to the PROMPT only: a
    /// passphrase read from a variable, a file or a pipe was not typed here, so asking for it twice
    /// would be asking the same source the same question. A mistyped one is a file nobody can ever
    /// open, its author included, which is why the typed path confirms at all.</param>
    /// <param name="option">The option the source was named by, for the error messages: <c>--key</c>
    /// for a passphrase, <c>--pinata-key</c> for a pinning key. The same sources, the same rules.</param>
    /// <returns>The nonempty secret.</returns>
    /// <exception cref="InvalidOperationException">The source is unknown, empty or missing; prompting
    /// has no terminal; or confirmation does not match.</exception>
    /// <exception cref="IOException">Reading the source file or console fails.</exception>
    /// <exception cref="UnauthorizedAccessException">The source file cannot be accessed.</exception>
    /// <exception cref="OperationCanceledException">Escape cancels the prompt.</exception>
    public static string Read(string? spec, string prompt, bool confirm = false, string option = "--key")
    {
        if (spec is null || spec == PromptSource) return ReadPrompt(prompt, confirm, option);

        if (spec.StartsWith(EnvPrefix, StringComparison.Ordinal))
        {
            var name = spec[EnvPrefix.Length..];
            if (name.Length == 0)
                throw new InvalidOperationException($"{option} {EnvPrefix} needs a variable name.");
            return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException(
                    $"{option} named the environment variable '{name}', which is unset or empty.");
        }

        if (spec.StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            var path = spec[FilePrefix.Length..];
            if (path.Length == 0)
                throw new InvalidOperationException($"{option} {FilePrefix} needs a path.");
            if (!File.Exists(path))
                throw new InvalidOperationException($"{option} named the file '{path}', which is not there.");
            using var reader = new StreamReader(path);
            return reader.ReadLine() is { Length: > 0 } line
                ? line
                : throw new InvalidOperationException($"'{path}' is empty.");
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
            $"{option} does not name a {(option == "--key" ? "passphrase" : option)} source; expected {Sources}.");
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
    /// <param name="text">The text, or null for no input.</param>
    /// <returns>The first line when nonempty, or null for a missing or empty first line.</returns>
    private static string? FirstLine(string? text)
    {
        if (text is null) return null;
        var end = text.IndexOfAny(['\r', '\n']);
        var line = end < 0 ? text : text[..end];
        return line.Length == 0 ? null : line;
    }

    /// <summary>Prompts at an interactive terminal, refusing redirected input.</summary>
    /// <param name="prompt">The prompt label.</param>
    /// <param name="confirm">Whether to require the same entry twice.</param>
    /// <param name="option">The source option to name when no terminal exists.</param>
    /// <returns>The nonempty secret.</returns>
    private static string ReadPrompt(string prompt, bool confirm, string option)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException(
                $"There is no terminal to ask at. Name a source with {option} "
                + $"({Sources}).");

        return ReadPrompt(prompt, confirm, () => Console.ReadKey(intercept: true), Console.Out);
    }

    /// <summary>
    /// The interactive prompt over an injected key source and echo writer — the seam a test drives,
    /// since a test runner's stdin is always redirected. The console path passes
    /// <c>Console.ReadKey(intercept: true)</c> and <c>Console.Out</c>; nothing else differs.
    /// </summary>
    /// <param name="prompt">The prompt label.</param>
    /// <param name="confirm">Whether to require the same entry twice.</param>
    /// <param name="readKey">Borrowed key source; called synchronously.</param>
    /// <param name="echo">Borrowed writer for prompts and newlines; never disposed here.</param>
    /// <returns>The nonempty secret.</returns>
    /// <exception cref="InvalidOperationException">The entry is empty or confirmation differs.</exception>
    /// <exception cref="OperationCanceledException">Escape cancels either entry.</exception>
    internal static string ReadPrompt(string prompt, bool confirm, Func<ConsoleKeyInfo> readKey, TextWriter echo)
    {
        string first = ReadMasked(prompt, readKey, echo);
        if (first.Length == 0)
            throw new InvalidOperationException("No passphrase was entered.");
        if (!confirm) return first;

        // Sealing is one-way by design: nothing anywhere can recover the contents of a seal whose
        // passphrase was mistyped, so the confirmation is not politeness.
        if (ReadMasked("Confirm passphrase", readKey, echo) != first)
            throw new InvalidOperationException("The two entries did not match; nothing was written.");
        return first;
    }

    /// <summary>
    /// Reads a line without echoing it.
    /// </summary>
    /// <remarks>
    /// Secret characters are never echoed, including as asterisks; prompts and newlines are written.
    /// Enter finishes, Backspace removes the last UTF-16 code unit, Escape cancels, and other control
    /// characters are ignored. A row of stars publishes the length of the
    /// passphrase to anyone looking over the shoulder this is meant to defeat, which is the one
    /// fact about it worth having.
    /// </remarks>
    /// <param name="prompt">The label written before reading.</param>
    /// <param name="readKey">Borrowed synchronous key source.</param>
    /// <param name="echo">Borrowed writer for prompts and newlines; never disposed here.</param>
    /// <returns>The entered text, which may be empty.</returns>
    /// <exception cref="OperationCanceledException">Escape cancels the entry.</exception>
    internal static string ReadMasked(string prompt, Func<ConsoleKeyInfo> readKey, TextWriter echo)
    {
        echo.Write(prompt + ": ");
        var typed = new System.Text.StringBuilder();
        while (true)
        {
            var key = readKey();
            switch (key.Key)
            {
                case ConsoleKey.Enter:
                    echo.WriteLine();
                    return typed.ToString();
                case ConsoleKey.Backspace:
                    if (typed.Length > 0) typed.Length--;
                    break;
                case ConsoleKey.Escape:
                    echo.WriteLine();
                    throw new OperationCanceledException("Canceled.");
                default:
                    if (!char.IsControl(key.KeyChar)) typed.Append(key.KeyChar);
                    break;
            }
        }
    }
}
