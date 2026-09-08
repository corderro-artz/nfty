using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nfty.Core.Formats;

namespace Nfty.Core.Publish;

/// <summary>
/// The cryptography behind a sealed export: authenticated encryption of a whole Set under a
/// passphrase, plus a header that says what is inside without giving any of it away.
/// </summary>
/// <remarks>
/// <para><b>What this buys, precisely.</b> Without the passphrase the payload is indistinguishable
/// from noise and cannot be altered undetectably. That part is arithmetic and it holds. Against the
/// person you gave the passphrase to it buys a refusal in the product and nothing more — nfty will
/// not write the assets back out, and this format is open source, so somebody determined
/// re-implements the reader. Every surface that offers sealing says so, because a security claim a
/// user misreads is worse than no claim at all.</para>
///
/// <para><b>The payload is framed, which opens four attacks a single-buffer AEAD does not have</b>
/// — reordering frames, truncating the payload, appending to it, and splicing a frame in from
/// another file sealed under the same passphrase. Each is closed, and it is worth being exact about
/// BY WHAT, because the obvious answer is wrong and mutation-probing the suite is what showed it:
/// <list type="bullet">
///   <item><description><b>Reorder</b> — the nonce, which is a per-file random prefix followed by
///   the frame index. Frame 1's ciphertext under frame 0's nonce fails its tag.</description></item>
///   <item><description><b>Truncate</b> — the header MAC, which covers the frame count and the
///   plaintext length, so the loop still expects every frame and runs out of bytes.</description></item>
///   <item><description><b>Append</b> — the explicit check for a trailing byte after the last
///   frame. This one has no second layer: every declared frame authenticates perfectly and the
///   extra bytes are simply never read. It is the only guard here that is load-bearing alone.</description></item>
///   <item><description><b>Splice</b> — three independent layers, any one of which suffices: the
///   salt is per file, so two seals under one passphrase do not even share a KEY; the nonce prefix
///   is per file; and the frame's associated data names the file's <c>sealId</c>. Removing all
///   three is what it takes to make a grafted frame authenticate.</description></item>
/// </list></para>
///
/// <para><b>Nonces cannot repeat.</b> Four random bytes fixed per file, then the frame index as a
/// big-endian 64-bit integer. Reusing a nonce under one key is the single failure that breaks
/// AES-GCM outright, and this construction makes it unrepresentable rather than merely unlikely.</para>
///
/// <para><b>PBKDF2 rather than Argon2</b>, at 600,000 SHA-256 iterations — the OWASP figure for
/// that pair. Argon2 is the better KDF and is not in the BCL; taking a cryptography dependency is a
/// larger decision than this feature should make on its own. The iteration count is recorded per
/// file, so raising it later costs nothing and orphans no existing seal.</para>
/// </remarks>
public static class Seal
{
    /// <summary>The plaintext header entry inside a sealed archive.</summary>
    public const string ManifestEntry = "seal.json";

    /// <summary>The encrypted payload entry inside a sealed archive.</summary>
    public const string PayloadEntry = "payload.bin";

    /// <summary>Plaintext bytes per encrypted frame.</summary>
    public const int FrameBytes = 1 << 20;

    /// <summary>PBKDF2 iterations this build writes. An older seal is read at whatever it records.</summary>
    public const int Iterations = 600_000;

    /// <summary>
    /// The shortest passphrase this will seal with.
    /// </summary>
    /// <remarks>
    /// A floor rather than advice, because the iteration count above is irrelevant against a
    /// passphrase that fits in a wordlist: sealing with "secret" produces a file that looks
    /// encrypted and is not, and nothing on screen would show the difference. Refusing is the
    /// honest response.
    /// </remarks>
    public const int MinimumPassphraseLength = 12;

    private const string KdfName = "pbkdf2-sha256";
    private const string CipherName = "aes-256-gcm";
    private const int SaltBytes = 16, SealIdBytes = 16, KeyBytes = 32, MacKeyBytes = 32;
    private const int TagBytes = 16, NonceBytes = 12, NoncePrefixBytes = 4;
    private const int MaxFrameBytes = 64 << 20;

    /// <summary>
    /// Reads a sealed archive's header. No passphrase, because a recipient who has not found the
    /// passphrase yet still needs to be told what they are holding.
    /// </summary>
    /// <param name="path">The sealed archive.</param>
    /// <returns>What the file says about itself in the clear.</returns>
    /// <exception cref="SealedSetException">It is not a sealed archive, or its header is unreadable.</exception>
    public static SealManifest Peek(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(ManifestEntry)
                ?? throw new SealedSetException(
                    $"'{path}' has no {ManifestEntry}, so it is not a sealed export.");
            using var s = entry.Open();
            var manifest = JsonSerializer.Deserialize<SealManifest>(s, Json.Options)
                ?? throw new SealedSetException($"'{path}' has an empty {ManifestEntry}.");
            UnsupportedSchemaVersionException.Require(manifest);
            return manifest;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            throw new SealedSetException($"'{path}' is not a readable sealed export: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Seals <paramref name="plaintext"/> into a new archive at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">Where to write it. An existing file there is replaced.</param>
    /// <param name="plaintext">The bytes to encrypt — a packed Set. Must be seekable: the frame
    /// count and the plaintext length are authenticated by the FIRST frame, so both have to be
    /// known before a byte is written.</param>
    /// <param name="collection">The collection's name, shown to a recipient with no key.</param>
    /// <param name="count">How many assets are inside, shown to a recipient with no key.</param>
    /// <param name="note">What the author wants read first, or null.</param>
    /// <param name="policy">What nfty will let the recipient do.</param>
    /// <param name="passphrase">The passphrase. Never stored, never written, never logged.</param>
    /// <exception cref="ArgumentException">The passphrase is too short, or the stream cannot seek.</exception>
    /// <exception cref="SealedSetException">This platform provides no AES-GCM.</exception>
    public static void Write(string path, Stream plaintext, string collection, int count,
        string? note, SealPolicy policy, string passphrase) =>
        Write(path, plaintext, collection, count, note, policy, passphrase, Iterations);

    /// <summary>
    /// <see cref="Write(string, Stream, string, int, string?, SealPolicy, string)"/>, at a chosen
    /// iteration count.
    /// </summary>
    /// <remarks>
    /// Internal and for tests only. The shipped count is deliberately expensive — about a second per
    /// derivation — and the tamper suite runs two dozen cases that care about the AEAD rather than
    /// about the KDF. The count is recorded in the header and honoured on read, so a seal written
    /// here is a real seal in every respect except how long it takes to open.
    /// </remarks>
    internal static void Write(string path, Stream plaintext, string collection, int count,
        string? note, SealPolicy policy, string passphrase, int iterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(policy);
        RequireUsablePassphrase(passphrase);
        RequireAesGcm();
        if (!plaintext.CanSeek)
            throw new ArgumentException(
                "Sealing needs a seekable stream: the payload's length and frame count are "
                + "authenticated by the first frame, so both must be known before it is written.",
                nameof(plaintext));

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] sealId = RandomNumberGenerator.GetBytes(SealIdBytes);
        byte[] noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixBytes);
        var (encKey, macKey) = DeriveKeys(passphrase, salt, iterations);

        long plainBytes = plaintext.Length - plaintext.Position;
        int frames = (int)((plainBytes + FrameBytes - 1) / FrameBytes);

        var header = new SealManifest(
            SealId: Convert.ToHexStringLower(sealId),
            Collection: collection,
            Count: count,
            Note: string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Policy: policy,
            PolicyMac: string.Empty,
            Kdf: new SealKdf(KdfName, iterations, Convert.ToHexStringLower(salt)),
            Cipher: new SealCipher(CipherName, FrameBytes, frames, plainBytes,
                Convert.ToHexStringLower(noncePrefix)));
        header = header with { PolicyMac = Convert.ToHexStringLower(Mac(macKey, header)) };

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        // NoCompression: ciphertext does not compress, so deflating it spends CPU to make the file
        // marginally larger.
        var payload = zip.CreateEntry(PayloadEntry, CompressionLevel.NoCompression);
        using (var outS = payload.Open())
            EncryptFrames(plaintext, outS, encKey, sealId, noncePrefix, frames, plainBytes);

        using var headerStream = zip.CreateEntry(ManifestEntry).Open();
        JsonSerializer.Serialize(headerStream, header, Json.Options);
    }

    /// <summary>
    /// Decrypts a sealed archive's payload into <paramref name="plaintext"/>.
    /// </summary>
    /// <param name="path">The sealed archive.</param>
    /// <param name="passphrase">The passphrase it was sealed with.</param>
    /// <param name="plaintext">Where the decrypted bytes go.</param>
    /// <returns>The header, verified against the passphrase.</returns>
    /// <exception cref="SealedSetException">The passphrase is wrong, or the file was altered — two
    /// states nothing here can tell apart, reported as one.</exception>
    public static SealManifest ExtractTo(string path, string passphrase, Stream plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        RequireAesGcm();

        var header = Peek(path);
        if (header.Kdf.Algorithm != KdfName || header.Cipher.Algorithm != CipherName)
            throw new SealedSetException(
                $"'{path}' was sealed with {header.Kdf.Algorithm} / {header.Cipher.Algorithm}, "
                + "which this build of nfty cannot read.");
        if (header.Kdf.Iterations is < 1 or > 20_000_000)
            throw new SealedSetException($"'{path}' declares a key derivation that cannot be run.");

        var (encKey, macKey) = DeriveKeys(passphrase, Hex(header.Kdf.Salt, "salt", path),
            header.Kdf.Iterations);

        // Checked BEFORE a byte of payload is touched. A wrong passphrase derives a wrong macKey, so
        // this doubles as the fast answer to "is this the right passphrase" - one HMAC, rather than
        // decrypting a whole collection to find out.
        if (!CryptographicOperations.FixedTimeEquals(
                Mac(macKey, header), Hex(header.PolicyMac, "policyMac", path)))
            throw SealedSetException.CannotOpen(path);

        using var zip = ZipFile.OpenRead(path);
        var payload = zip.GetEntry(PayloadEntry)
            ?? throw new SealedSetException($"'{path}' has no {PayloadEntry}, so it holds nothing.");
        using var inS = payload.Open();
        DecryptFrames(inS, plaintext, encKey, Hex(header.SealId, "sealId", path),
            Hex(header.Cipher.NoncePrefix, "noncePrefix", path), header, path);
        return header;
    }

    /// <summary>Whether a passphrase is long enough to be worth deriving a key from.</summary>
    /// <param name="passphrase">The candidate.</param>
    /// <returns>True when it meets <see cref="MinimumPassphraseLength"/>.</returns>
    public static bool IsUsablePassphrase(string? passphrase) =>
        passphrase is not null && passphrase.Length >= MinimumPassphraseLength;

    private static void RequireUsablePassphrase(string passphrase)
    {
        if (IsUsablePassphrase(passphrase)) return;
        throw new ArgumentException(
            $"A sealing passphrase must be at least {MinimumPassphraseLength} characters. Below "
            + "that the encryption is decoration: the file looks sealed and is not, and nothing on "
            + "screen would show the difference.", nameof(passphrase));
    }

    /// <summary>
    /// Whether this platform can seal at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Public so a front-end can ASK before it offers the choice.</b> AES-GCM is a platform
    /// capability rather than a language feature — .NET exposes it only where the underlying crypto
    /// library provides it — so on a host without it, sealing is not a thing that fails, it is a
    /// thing that is not there. A checkbox that accepts a passphrase, a confirmation and a note and
    /// then throws is a worse answer than one that is switched off with a reason next to it.</para>
    ///
    /// <para>Every entry point here still checks it independently: a front-end reading this is a
    /// courtesy, not the enforcement.</para>
    /// </remarks>
    public static bool IsSupported => AesGcm.IsSupported;

    /// <summary>Why sealing is unavailable, for a surface that wants to say so.</summary>
    public const string UnsupportedMessage =
        "This platform provides no AES-GCM, so nfty cannot seal or open a sealed export here.";

    private static void RequireAesGcm()
    {
        if (!IsSupported) throw new SealedSetException(UnsupportedMessage);
    }

    /// <summary>
    /// A hex field out of the header, as bytes. Every one of these is attacker-controlled text in a
    /// file that may not be a seal at all, so a malformed one is a domain failure with a message —
    /// not the raw <c>FormatException</c> <c>Convert.FromHexString</c> would otherwise throw from
    /// the middle of a decrypt.
    /// </summary>
    private static byte[] Hex(string value, string field, string path)
    {
        try { return Convert.FromHexString(value); }
        catch (FormatException ex)
        {
            throw new SealedSetException(
                $"'{path}' has a malformed '{field}' in its {ManifestEntry}.", ex);
        }
    }

    /// <summary>
    /// Stretches the passphrase into two independent keys: one to encrypt with, one to authenticate
    /// the header with. Separated because a key should do one job — reusing the encryption key for
    /// the header MAC mixes two constructions over one secret for no benefit.
    /// </summary>
    private static (byte[] Enc, byte[] Mac) DeriveKeys(string passphrase, byte[] salt, int iterations)
    {
        byte[] master = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, iterations,
            HashAlgorithmName.SHA256, KeyBytes + MacKeyBytes);
        return (master[..KeyBytes], master[KeyBytes..]);
    }

    private static byte[] Mac(byte[] macKey, SealManifest header) =>
        HMACSHA256.HashData(macKey, Encoding.UTF8.GetBytes(header.Canonical()));

    /// <summary>
    /// What every frame authenticates alongside its ciphertext: the file it belongs to, the shape of
    /// the payload, and the frame's own position.
    /// </summary>
    /// <remarks>
    /// <b>Belt, not braces — and knowing which is which matters.</b> Probing the suite shows that
    /// removing this changes no test: the per-file salt and nonce prefix already stop a cross-file
    /// graft, the nonce index already stops a reorder, and the header MAC already stops a
    /// truncation. It stays for two reasons. It is the layer that still holds if a nonce prefix
    /// ever repeats — the class remark spells out the three-deep stack it belongs to — and it makes
    /// each frame's authentication independent of the header having been read correctly, so a later
    /// change to how the header is handled cannot quietly unbind the payload. It is also part of the
    /// format the moment it ships: adding or removing it afterwards breaks every existing seal, so
    /// the choice is made once, here.
    /// </remarks>
    private static byte[] Aad(byte[] sealId, int frames, long plainBytes, int index) =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"nfty-seal/1|{Convert.ToHexStringLower(sealId)}|{frames}|{plainBytes}|{index}"));

    private static void EncryptFrames(Stream plaintext, Stream output, byte[] encKey,
        byte[] sealId, byte[] noncePrefix, int frames, long plainBytes)
    {
        byte[] plain = new byte[FrameBytes];
        byte[] cipher = new byte[FrameBytes];
        byte[] tag = new byte[TagBytes];
        byte[] nonce = new byte[NonceBytes];
        noncePrefix.CopyTo(nonce, 0);

        using var gcm = new AesGcm(encKey, TagBytes);
        for (int i = 0; i < frames; i++)
        {
            int n = (int)Math.Min(FrameBytes, plainBytes - (long)i * FrameBytes);
            plaintext.ReadExactly(plain, 0, n);
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(NoncePrefixBytes), (ulong)i);
            gcm.Encrypt(nonce, plain.AsSpan(0, n), cipher.AsSpan(0, n), tag,
                Aad(sealId, frames, plainBytes, i));
            output.Write(cipher, 0, n);
            output.Write(tag, 0, TagBytes);
        }
    }

    private static void DecryptFrames(Stream input, Stream output, byte[] encKey,
        byte[] sealId, byte[] noncePrefix, SealManifest header, string path)
    {
        int frames = header.Cipher.Frames;
        long plainBytes = header.Cipher.PlainBytes;
        int frameBytes = header.Cipher.FrameBytes;

        // The header MAC has already passed, so these numbers came from whoever held the passphrase.
        // Checked anyway, because they size an allocation and drive a loop: a header claiming a
        // 2 GB frame would be an out-of-memory crash rather than a refusal.
        if (frames < 0 || plainBytes < 0 || frameBytes is <= 0 or > MaxFrameBytes
            || (long)frames * frameBytes < plainBytes)
            throw new SealedSetException($"'{path}' declares a payload shape that cannot be read.");

        byte[] cipher = new byte[frameBytes];
        byte[] plain = new byte[frameBytes];
        byte[] tag = new byte[TagBytes];
        byte[] nonce = new byte[NonceBytes];
        noncePrefix.CopyTo(nonce, 0);

        using var gcm = new AesGcm(encKey, TagBytes);
        try
        {
            for (int i = 0; i < frames; i++)
            {
                int n = (int)Math.Min(frameBytes, plainBytes - (long)i * frameBytes);
                input.ReadExactly(cipher, 0, n);
                input.ReadExactly(tag, 0, TagBytes);
                BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(NoncePrefixBytes), (ulong)i);
                gcm.Decrypt(nonce, cipher.AsSpan(0, n), tag, plain.AsSpan(0, n),
                    Aad(sealId, frames, plainBytes, i));
                output.Write(plain, 0, n);
            }

            // Every frame authenticated and bytes still to come. Not reachable by editing seal.json,
            // since the header MAC covers the frame count - it means the payload entry itself was
            // appended to, and a reader that simply stopped counting at `frames` would not notice.
            if (input.ReadByte() != -1) throw SealedSetException.CannotOpen(path);
        }
        catch (Exception ex) when (ex is CryptographicException or EndOfStreamException)
        {
            throw new SealedSetException(SealedSetException.CannotOpen(path).Message, ex);
        }
    }
}
