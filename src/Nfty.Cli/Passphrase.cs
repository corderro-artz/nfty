namespace Nfty.Cli;

/// <summary>
/// Where a sealing passphrase comes from on a command line.
/// </summary>
/// <remarks>
/// <para><b>There is deliberately no <c>--passphrase</c> option.</b> An argument on a command line
/// is visible to every process on the machine for as long as the command runs, it lands in the
/// shell's history file, and it is captured verbatim by any CI log the command runs under. A
/// secret that has to be typed in front of all three is not a secret, and offering the flag would
/// mean most people used it — the convenient path is the one that gets taken.</para>
///
/// <para>So: an environment variable named by <c>--key-env</c>, or a prompt. The prompt is the
/// default because it is the one that leaves nothing behind at all.</para>
/// </remarks>
public static class Passphrase
{
    /// <summary>
    /// Reads a passphrase.
    /// </summary>
    /// <param name="envName">The environment variable named by <c>--key-env</c>, or null to prompt.</param>
    /// <param name="prompt">What to ask, when prompting.</param>
    /// <param name="confirm">Ask twice and require a match. True when sealing — a passphrase
    /// mistyped once is a file nobody can ever open, including its author.</param>
    /// <returns>The passphrase.</returns>
    /// <exception cref="InvalidOperationException">The variable is unset or empty, the two entries
    /// did not match, or there is no terminal to prompt at.</exception>
    public static string Read(string? envName, string prompt, bool confirm = false)
    {
        if (envName is not null)
        {
            return Environment.GetEnvironmentVariable(envName) is { Length: > 0 } value
                ? value
                : throw new InvalidOperationException(
                    $"--key-env named '{envName}', but that environment variable is unset or empty.");
        }

        if (Console.IsInputRedirected)
            throw new InvalidOperationException(
                "There is no terminal to ask for a passphrase at. Put it in an environment variable "
                + "and name that variable with --key-env.");

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
