using Nfty.Core.Model;

namespace Nfty.Core.Publish;

/// <summary>
/// What a sealed export permits its recipient to do. Written into the seal header in the clear, and
/// bound to the passphrase by <see cref="SealManifest.PolicyMac"/>.
/// </summary>
/// <param name="AllowExport">Whether nfty will write the assets back out of the seal. False is the
/// point of the feature.</param>
/// <param name="AllowEdit">Whether nfty will let the contents be modified in place.</param>
public record SealPolicy(bool AllowExport, bool AllowEdit)
{
    /// <summary>The policy a "share this for critique" seal carries: look, do not take.</summary>
    public static SealPolicy ViewOnly { get; } = new(AllowExport: false, AllowEdit: false);
}

/// <summary>How the passphrase was stretched into a key.</summary>
/// <param name="Algorithm">The KDF name, for a reader that must refuse one it does not implement.</param>
/// <param name="Iterations">The PBKDF2 iteration count this file was written with. Recorded rather
/// than assumed, so raising the default later does not make every existing seal unreadable.</param>
/// <param name="Salt">The per-file salt, hex. Public by design: a salt defeats precomputation, it
/// is not a secret.</param>
public record SealKdf(string Algorithm, int Iterations, string Salt);

/// <summary>How the payload was encrypted.</summary>
/// <param name="Algorithm">The AEAD name.</param>
/// <param name="FrameBytes">Plaintext bytes per frame. The payload is encrypted in independently
/// authenticated frames so a 4 GB collection never has to be held in memory to be read or written.</param>
/// <param name="Frames">How many frames the payload holds.</param>
/// <param name="PlainBytes">The plaintext length, which is what gives the final (short) frame its
/// size. Both are in the AAD of every frame, so a truncated payload fails to authenticate rather
/// than decrypting to a shorter collection.</param>
/// <param name="NoncePrefix">The four random bytes every frame nonce begins with, hex. The
/// remaining eight are the frame index, so no nonce can repeat under one key.</param>
public record SealCipher(string Algorithm, int FrameBytes, int Frames, long PlainBytes, string NoncePrefix);

/// <summary>
/// The <c>seal.json</c> at the top of a sealed export — <b>plaintext, deliberately</b>.
/// </summary>
/// <remarks>
/// <para><b>Why any of it is readable without the key.</b> A recipient holding a file they cannot
/// open needs to be told what it is, or the only thing the format communicates is "something went
/// wrong". <c>inspect</c> prints this header, so they learn the collection's name, its size, and
/// the note the author wrote them, and then know which passphrase to go and find.</para>
///
/// <para><b>That is a deliberate metadata leak and it is the whole cost of the choice.</b> Anyone
/// who obtains the file learns that the collection exists, what it is called and how large it is.
/// The art, the traits, the rarity and the DNA are all inside the encryption; the label on the tin
/// is not.</para>
///
/// <para><b><see cref="PolicyMac"/> is what stops the label being rewritten.</b> It is an HMAC over
/// <see cref="Canonical"/> keyed by a second key derived from the same passphrase, so someone who
/// intercepts the file cannot edit <c>allowExport</c> to true, or restate the note, and pass it on.
/// Someone who HAS the passphrase can recompute it — that is not a gap in the implementation, it is
/// what it means to hold the key, and it is why the seal is described everywhere in this product as
/// a lock on a door rather than a wall.</para>
/// </remarks>
/// <param name="SealId">A random 128-bit id, hex. It is in the AAD of every frame, so frames cannot
/// be spliced between two files sealed with the same passphrase.</param>
/// <param name="Collection">The collection's name, for a recipient with no key.</param>
/// <param name="Count">How many assets are inside.</param>
/// <param name="Note">What the author wants the recipient to read first. Null when none was given.</param>
/// <param name="Policy">What nfty will let the recipient do.</param>
/// <param name="PolicyMac">Hex HMAC-SHA256 over <see cref="Canonical"/>.</param>
/// <param name="Kdf">How the key was derived.</param>
/// <param name="Cipher">How the payload was encrypted.</param>
/// <param name="SchemaVersion">The schema this header was written against.</param>
public record SealManifest(
    string SealId,
    string Collection,
    int Count,
    string? Note,
    SealPolicy Policy,
    string PolicyMac,
    SealKdf Kdf,
    SealCipher Cipher,
    int SchemaVersion = Schema.Current) : ISchemaVersioned
{
    /// <summary>
    /// The exact bytes the MAC covers, as a string.
    /// </summary>
    /// <returns>One line naming every header field that carries meaning, in a fixed order.</returns>
    /// <remarks>
    /// <b>Spelled out by hand rather than by re-serializing this record</b>, because the MAC has to
    /// mean the same thing in every future build. <c>System.Text.Json</c> emits a record's members
    /// in declaration order, so MACing its own JSON would make reordering two properties — a
    /// refactor with no observable effect anywhere else — silently invalidate every seal ever
    /// written. This method is the format; the record is just how it is carried.
    /// </remarks>
    public string Canonical() => string.Join('|',
        "nfty-seal/1",
        SealId,
        Collection,
        Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Note ?? string.Empty,
        Policy.AllowExport ? "export=yes" : "export=no",
        Policy.AllowEdit ? "edit=yes" : "edit=no",
        Kdf.Algorithm,
        Kdf.Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Kdf.Salt,
        Cipher.Algorithm,
        Cipher.FrameBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Cipher.Frames.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Cipher.PlainBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Cipher.NoncePrefix);
}
