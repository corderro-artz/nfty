namespace Nfty.Core.Publish;

/// <summary>
/// A sealed export could not be opened. One type for every reason, because the reasons are not
/// distinguishable from outside the seal and pretending otherwise would be a lie about what the
/// cryptography proved.
/// </summary>
/// <remarks>
/// <b>A wrong passphrase and a tampered file fail identically, and must.</b> The MAC and the frame
/// tags are computed from a key derived from the passphrase, so a wrong passphrase produces a wrong
/// key and every check fails — exactly as an edited file does under the right key. There is no
/// computation that separates the two, and a message claiming to have separated them would be
/// inventing a fact. The text says both.
/// </remarks>
public sealed class SealedSetException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What to show the user, verbatim — <c>ErrorReport</c> prints it.</param>
    public SealedSetException(string message) : base(message) { }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What to show the user, verbatim.</param>
    /// <param name="inner">The underlying failure.</param>
    public SealedSetException(string message, Exception inner) : base(message, inner) { }

    /// <summary>The failure every authentication check reports.</summary>
    /// <param name="path">The file that would not open.</param>
    /// <returns>The exception to throw.</returns>
    public static SealedSetException CannotOpen(string path) => new(
        $"'{path}' would not open. Either the passphrase is wrong, or the file was changed after "
        + "it was sealed - a sealed export cannot tell those apart, and does not guess.");
}
