using System.IO.Compression;
using Nfty.Core.Output;

namespace Nfty.Core.Publish;

/// <summary>
/// Opens a sealed export for looking at.
/// </summary>
/// <remarks>
/// <para><b>There is deliberately no counterpart that writes one back out.</b> A sealed export is
/// the answer to "let them read it, not take it", and an <c>unseal</c> next to it would make the
/// whole thing theater — the author who sealed it still has the Set it was sealed from, so nothing
/// legitimate needs the operation. <see cref="SetExporter"/> refuses a <c>.tin</c> as a source for
/// the same reason, and that refusal lives in Core so a front-end cannot be the only thing enforcing
/// it.</para>
///
/// <para><b>What this does not claim.</b> Everything here runs on the recipient's machine with the
/// recipient's passphrase, so the refusal above is nfty declining, not mathematics forbidding.
/// Someone holding the passphrase who wants the PNGs will get them. The encryption is real against
/// anyone WITHOUT the passphrase; the view-only mark is a lock on a door, and the door is in a wall
/// the key-holder is already standing inside.</para>
/// </remarks>
public static class SealedSetReader
{
    /// <summary>
    /// Decrypts a sealed export and reads the Set inside it.
    /// </summary>
    /// <param name="path">The <c>.tin</c>.</param>
    /// <param name="passphrase">The passphrase it was sealed with.</param>
    /// <returns>The Set, plus the seal's header. The caller owns it and must dispose it — doing so
    /// deletes the decrypted copy from the temporary directory it was unpacked into.</returns>
    /// <exception cref="SealedSetException">The passphrase is wrong, the file was altered, or what
    /// came out is not a Set.</exception>
    public static SealedSet Open(string path, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        var temp = Directory.CreateTempSubdirectory("nfty-tin-").FullName;
        string inner = Path.Combine(temp, "payload.set");
        try
        {
            SealManifest header;
            using (var plaintext = File.Create(inner))
                header = Seal.ExtractTo(path, passphrase, plaintext);

            ZipFile.ExtractToDirectory(inner, temp);

            // Deleted the moment it is unpacked. Leaving it would put a complete, unencrypted,
            // ready-to-copy .set in the temp directory for as long as the Set stayed open - which is
            // the export the seal exists to withhold, sitting on disk under a predictable name.
            File.Delete(inner);

            if (!SetReader.IsSetFolder(temp))
                throw new SealedSetException(
                    $"'{path}' opened, but what is inside is not a cooked Set.");

            var loaded = SetReader.Read(temp);
            return new SealedSet
            {
                Header = header,
                Manifest = loaded.Manifest,
                Items = loaded.Items,
                SourceDirectory = temp,
                TempDir = temp,
            };
        }
        catch
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
            throw;
        }
    }
}

/// <summary>A Set that came out of a seal, carrying the seal's own header alongside it.</summary>
/// <remarks>
/// A <see cref="LoadedSet"/> with the header attached rather than a separate return value, because
/// every screen that shows a sealed Set has to show what it is allowed to do with it — and a policy
/// the caller has to remember to carry alongside the data is a policy that will eventually be
/// dropped on the way to the one place that checks it.
/// </remarks>
public sealed class SealedSet : LoadedSet
{
    /// <summary>What the seal says: who it was for, and what nfty will let them do.</summary>
    public required SealManifest Header { get; init; }
}
