namespace Nfty.Core.Publish;

/// <summary>
/// A Menu atlas would be larger than one image can be (a pixel ceiling on a side, or in area), so the
/// write is refused rather than built.
/// </summary>
/// <remarks>
/// <para>The refusal carries its two halves separately. <see cref="Problem"/> is what is wrong and is
/// true on any front-end; <see cref="Hint"/> is the way out spelled for the command line. The CLI
/// prints <see cref="Exception.Message"/>, which joins them, so its text is what it always was. A
/// screen with its own Atlas/Loose tray shows <see cref="Problem"/> and says "Choose Loose" in its own
/// words, instead of leaking a flag that does not exist there.</para>
///
/// <para>It derives from <see cref="InvalidOperationException"/> because that is what the refusal threw
/// before it had a type, so any caller that caught the base keeps working.</para>
/// </remarks>
public sealed class AtlasTooLargeException : InvalidOperationException
{
    /// <summary>The way out as the command line spells it.</summary>
    public const string CliHint = "write with --art loose instead.";

    /// <summary>What is wrong: the atlas size and the limit it is past. No instruction in it.</summary>
    public string Problem { get; }

    /// <summary>What to do about it, as the command line says it.</summary>
    public string Hint { get; }

    /// <summary>Creates the refusal.</summary>
    /// <param name="problem">What is wrong, without an instruction.</param>
    public AtlasTooLargeException(string problem) : base($"{problem} — {CliHint}")
    {
        Problem = problem;
        Hint = CliHint;
    }
}
