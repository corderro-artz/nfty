using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Publish;

namespace Nfty.Core.Tests;

/// <summary>
/// The sealed-export format, tested the way a format that claims to resist tampering has to be:
/// by tampering with it.
/// </summary>
/// <remarks>
/// Every seal here is written through the internal iteration-count overload at
/// <see cref="Iters"/> rather than the shipped 600,000. The KDF cost is about a second per
/// derivation by design, and these cases are about the AEAD and the framing, not about PBKDF2 —
/// paying it two dozen times would put a minute on the suite to re-prove one constant.
/// <see cref="The_shipped_iteration_count_is_what_gets_written"/> covers the constant itself.
/// </remarks>
public class SealTests
{
    private const int Iters = 1000;
    private const string Pass = "correct-horse-battery-staple";

    private static string Dir() => Directory.CreateTempSubdirectory("nfty-sealtest-").FullName;

    /// <summary>Deterministic bytes, so a decrypted payload can be compared exactly.</summary>
    private static byte[] Payload(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)((i * 31 + 7) & 0xFF);
        return bytes;
    }

    private static string Sealed(byte[] payload, string passphrase = Pass, string? note = null,
        string? path = null, int iterations = Iters)
    {
        path ??= Path.Combine(Dir(), "x" + Archives.SealedExtension);
        using var plain = new MemoryStream(payload, writable: false);
        Seal.Write(path, plain, "Chest Demo", 500, note, SealPolicy.ViewOnly, passphrase, iterations);
        return path;
    }

    private static byte[] Opened(string path, string passphrase = Pass)
    {
        using var outS = new MemoryStream();
        Seal.ExtractTo(path, passphrase, outS);
        return outS.ToArray();
    }

    // ------------------------------------------------------------------ it works at all

    [Theory]
    [InlineData(1)]
    [InlineData(Seal.FrameBytes - 1)]
    [InlineData(Seal.FrameBytes)]                  // exactly one frame, no remainder
    [InlineData(Seal.FrameBytes + 1)]              // two frames, the second one byte long
    [InlineData(Seal.FrameBytes * 2 + 12345)]      // three frames, a real remainder
    public void What_goes_in_comes_back_out(int length)
    {
        // The frame boundaries are where a chunked AEAD goes wrong, so the sizes are chosen to sit
        // exactly on and either side of one rather than to be "big" and "small".
        var payload = Payload(length);

        Assert.Equal(payload, Opened(Sealed(payload)));
    }

    [Fact]
    public void An_empty_payload_round_trips_rather_than_being_a_special_case()
    {
        Assert.Empty(Opened(Sealed(Array.Empty<byte>())));
    }

    [Fact]
    public void The_header_says_what_is_inside_without_a_passphrase()
    {
        // The point of leaving the header in the clear: a recipient holding a file they cannot open
        // is told what it is, so they know which passphrase to go and find. It is also the whole
        // cost of the choice - the name and the size ARE disclosed to anyone who gets the file.
        var header = Seal.Peek(Sealed(Payload(50), note: "Draft for review"));

        Assert.Equal("Chest Demo", header.Collection);
        Assert.Equal(500, header.Count);
        Assert.Equal("Draft for review", header.Note);
        Assert.False(header.Policy.AllowExport);
        Assert.False(header.Policy.AllowEdit);
    }

    [Fact]
    public void The_payload_is_not_sitting_in_the_file_in_the_clear()
    {
        // The assertion a reader of this format actually wants: that "encrypted" is not decoration.
        // A recognisable run of the plaintext is searched for across the WHOLE file, header included,
        // rather than only inside the payload entry.
        var marker = Encoding.UTF8.GetBytes("THE-SECRET-COLLECTION-MARKER");
        var payload = new byte[4096];
        marker.CopyTo(payload, 1000);

        byte[] file = File.ReadAllBytes(Sealed(payload));

        Assert.DoesNotContain(marker, Windows(file, marker.Length));
    }

    private static IEnumerable<byte[]> Windows(byte[] file, int width)
    {
        for (int i = 0; i + width <= file.Length; i++) yield return file[i..(i + width)];
    }

    // ------------------------------------------------------------------ the key

    [Fact]
    public void A_wrong_passphrase_does_not_open_it()
    {
        var path = Sealed(Payload(4096));

        var ex = Assert.Throws<SealedSetException>(() => Opened(path, "wrong-passphrase-entirely"));
        // Both possibilities are stated, because nothing here can distinguish them: a wrong
        // passphrase derives a wrong MAC key, which is exactly what an edited file looks like.
        Assert.Contains("passphrase is wrong", ex.Message);
        Assert.Contains("changed after", ex.Message);
    }

    [Fact]
    public void A_passphrase_one_character_off_does_not_open_it_either()
    {
        // Guarding against a comparison that got truncated or prefix-matched somewhere.
        var path = Sealed(Payload(64));

        Assert.Throws<SealedSetException>(() => Opened(path, Pass + "x"));
        Assert.Throws<SealedSetException>(() => Opened(path, Pass[..^1]));
    }

    [Fact]
    public void A_passphrase_too_short_to_be_worth_deriving_from_is_refused_up_front()
    {
        // Not advice: 600,000 iterations is irrelevant against a passphrase in a wordlist, and the
        // resulting file would look exactly as sealed as a real one.
        using var plain = new MemoryStream(Payload(16));
        var ex = Assert.Throws<ArgumentException>(() =>
            Seal.Write(Path.Combine(Dir(), "x.tin"), plain, "C", 1, null, SealPolicy.ViewOnly,
                "short", Iters));

        Assert.Contains(Seal.MinimumPassphraseLength.ToString(), ex.Message);
        Assert.False(Seal.IsUsablePassphrase(new string('a', Seal.MinimumPassphraseLength - 1)));
        Assert.True(Seal.IsUsablePassphrase(new string('a', Seal.MinimumPassphraseLength)));
    }

    [Fact]
    public void The_shipped_iteration_count_is_what_gets_written()
    {
        // The one thing the low-iteration tests above cannot cover. Slow on purpose - this is the
        // only place in the suite that pays the real KDF.
        var path = Sealed(Payload(32), iterations: Seal.Iterations);

        Assert.Equal(Seal.Iterations, Seal.Peek(path).Kdf.Iterations);
        Assert.Equal(600_000, Seal.Iterations);
        Assert.Equal(Payload(32), Opened(path));
    }

    [Fact]
    public void Two_seals_of_the_same_bytes_under_the_same_passphrase_are_different_files()
    {
        var payload = Payload(2048);
        var a = File.ReadAllBytes(Sealed(payload));
        var b = File.ReadAllBytes(Sealed(payload));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Every_per_file_random_really_is_per_file()
    {
        // The three values that keep two seals under ONE passphrase from having anything in common.
        // Asserted individually rather than via "the files differ", because that weaker check passes
        // while any ONE of them is still random - and the graft defence is these three stacked, so a
        // test that cannot see two of them go missing is not testing the stack.
        //
        // The salt is the deepest of them: it feeds the KDF, so two seals do not share a key at all,
        // and a fixed salt would silently collapse the other two guards into the only ones left.
        var x = Seal.Peek(Sealed(Payload(64), path: Path.Combine(Dir(), "a.tin")));
        var y = Seal.Peek(Sealed(Payload(64), path: Path.Combine(Dir(), "b.tin")));

        Assert.NotEqual(x.Kdf.Salt, y.Kdf.Salt);
        Assert.NotEqual(x.Cipher.NoncePrefix, y.Cipher.NoncePrefix);
        Assert.NotEqual(x.SealId, y.SealId);
    }

    // ------------------------------------------------------------------ tampering

    /// <summary>Rewrites one entry of a sealed archive in place.</summary>
    private static void Replace(string path, string entryName, byte[] content)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.GetEntry(entryName)!.Delete();
        using var s = zip.CreateEntry(entryName, CompressionLevel.NoCompression).Open();
        s.Write(content);
    }

    private static byte[] Entry(string path, string entryName)
    {
        using var zip = ZipFile.OpenRead(path);
        using var s = zip.GetEntry(entryName)!.Open();
        using var buf = new MemoryStream();
        s.CopyTo(buf);
        return buf.ToArray();
    }

    [Fact]
    public void Editing_the_policy_to_allow_export_makes_the_file_refuse_to_open()
    {
        // THE claim the seal makes beyond encryption: someone who intercepts the file cannot strip
        // the view-only mark and pass it on as an ordinary Set. Note what this does NOT claim -
        // someone holding the passphrase can recompute the MAC, which is what it means to hold a key.
        var path = Sealed(Payload(512));
        var header = Seal.Peek(path);
        Assert.False(header.Policy.AllowExport);

        var forged = header with { Policy = new SealPolicy(AllowExport: true, AllowEdit: true) };
        Replace(path, Seal.ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(forged, Json.Options));

        Assert.Throws<SealedSetException>(() => Opened(path));
    }

    [Theory]
    [InlineData("note")]
    [InlineData("collection")]
    [InlineData("count")]
    [InlineData("sealId")]
    [InlineData("frames")]
    [InlineData("plainBytes")]
    [InlineData("iterations")]
    public void Editing_any_load_bearing_header_field_makes_the_file_refuse_to_open(string field)
    {
        // The MAC covers the whole canonical header, not only the policy. A recipient reads the
        // collection name, the count and the note off a file they cannot otherwise inspect, so those
        // three are exactly as load-bearing as the policy is - a re-labelled seal is a lie the
        // format would otherwise carry for free.
        var path = Sealed(Payload(512), note: "original note");
        var h = Seal.Peek(path);
        var forged = field switch
        {
            "note" => h with { Note = "you may redistribute this" },
            "collection" => h with { Collection = "Something Else" },
            "count" => h with { Count = 1 },
            "sealId" => h with { SealId = new string('a', 32) },
            "frames" => h with { Cipher = h.Cipher with { Frames = h.Cipher.Frames + 1 } },
            "plainBytes" => h with { Cipher = h.Cipher with { PlainBytes = 8 } },
            "iterations" => h with { Kdf = h.Kdf with { Iterations = Iters + 1 } },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        Replace(path, Seal.ManifestEntry, JsonSerializer.SerializeToUtf8Bytes(forged, Json.Options));

        Assert.Throws<SealedSetException>(() => Opened(path));
    }

    [Theory]
    [InlineData(0)]        // first byte of the first frame's ciphertext
    [InlineData(600)]      // the middle of it
    [InlineData(-20)]      // inside the trailing tag
    [InlineData(-1)]       // the very last byte
    public void Flipping_one_bit_of_the_payload_makes_the_file_refuse_to_open(int offset)
    {
        var path = Sealed(Payload(1024));
        var payload = Entry(path, Seal.PayloadEntry);
        int i = offset >= 0 ? offset : payload.Length + offset;
        payload[i] ^= 0x01;
        Replace(path, Seal.PayloadEntry, payload);

        Assert.Throws<SealedSetException>(() => Opened(path));
    }

    [Fact]
    public void Truncating_the_payload_makes_the_file_refuse_to_open()
    {
        // Framing's first hazard: a reader that just decrypts frames until the bytes run out
        // accepts a truncated collection as a shorter one. Caught by the HEADER MAC, which covers
        // the frame count and the plaintext length - so the loop still expects every frame and runs
        // out of bytes part way through one. (Probed: the frame associated data is a second layer
        // here, not the mechanism.)
        var path = Sealed(Payload(Seal.FrameBytes + 5000));
        var payload = Entry(path, Seal.PayloadEntry);
        Replace(path, Seal.PayloadEntry, payload[..(payload.Length - 4000)]);

        Assert.Throws<SealedSetException>(() => Opened(path));
    }

    [Fact]
    public void Appending_to_the_payload_makes_the_file_refuse_to_open()
    {
        // The mirror hazard, and the one a frame loop cannot see on its own: every declared frame
        // authenticates perfectly and there are simply bytes left over. Caught by reading one more
        // byte after the loop, which is the only thing standing between "authenticated" and
        // "authenticated, and then some".
        var path = Sealed(Payload(2048));
        var payload = Entry(path, Seal.PayloadEntry);
        Replace(path, Seal.PayloadEntry, [.. payload, .. RandomNumberGenerator.GetBytes(64)]);

        Assert.Throws<SealedSetException>(() => Opened(path));
    }

    [Fact]
    public void Swapping_two_frames_makes_the_file_refuse_to_open()
    {
        // Framing's third hazard: each frame is a valid AEAD message on its own, so a reordered
        // payload would decrypt cleanly into a scrambled collection. Caught by the NONCE, whose
        // low eight bytes are the frame index - frame 1's ciphertext under frame 0's nonce fails
        // its tag before the associated data is even consulted.
        int frame = Seal.FrameBytes + 16;                 // ciphertext + tag
        var path = Sealed(Payload(Seal.FrameBytes * 3));
        var payload = Entry(path, Seal.PayloadEntry);
        var swapped = (byte[])payload.Clone();
        Array.Copy(payload, 0, swapped, frame, frame);
        Array.Copy(payload, frame, swapped, 0, frame);
        Replace(path, Seal.PayloadEntry, swapped);

        Assert.Throws<SealedSetException>(() => Opened(path));
    }

    [Fact]
    public void A_frame_from_another_seal_under_the_same_passphrase_does_not_graft_on()
    {
        // Framing's fourth hazard, and the subtlest: a frame lifted from one file authenticating
        // under another. THREE independent things stop it, and probing says each alone is enough -
        // the salt is per file, so two seals under one passphrase do not share a key at all; the
        // nonce prefix is per file; and the sealId is in every frame's associated data. This test
        // passes until all three are removed, which is worth knowing rather than assuming: the
        // obvious reading, that the sealId is what does the work, is wrong.
        var a = Sealed(Payload(Seal.FrameBytes * 2), path: Path.Combine(Dir(), "a.tin"));
        var b = Sealed(Payload(Seal.FrameBytes * 2), path: Path.Combine(Dir(), "b.tin"));

        var pa = Entry(a, Seal.PayloadEntry);
        var pb = Entry(b, Seal.PayloadEntry);
        int frame = Seal.FrameBytes + 16;
        Array.Copy(pb, 0, pa, 0, frame);                  // graft b's first frame into a
        Replace(a, Seal.PayloadEntry, pa);

        Assert.Throws<SealedSetException>(() => Opened(a));
    }

    // ------------------------------------------------------------------ malformed input

    [Fact]
    public void A_file_that_is_not_a_seal_is_reported_as_one_rather_than_crashing()
    {
        var dir = Dir();
        var notZip = Path.Combine(dir, "notes.tin");
        File.WriteAllText(notZip, "this is not a zip at all");
        Assert.Throws<SealedSetException>(() => Seal.Peek(notZip));

        var zipWithoutHeader = Path.Combine(dir, "empty.tin");
        using (var zip = ZipFile.Open(zipWithoutHeader, ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        var ex = Assert.Throws<SealedSetException>(() => Seal.Peek(zipWithoutHeader));
        Assert.Contains(Seal.ManifestEntry, ex.Message);
    }

    [Fact]
    public void A_header_whose_hex_fields_are_not_hex_is_reported_rather_than_throwing_a_format_error()
    {
        // These are attacker-controlled strings in a file that may not be a seal at all.
        // Convert.FromHexString throws a bare FormatException, and ErrorReport would print
        // "The input is not a valid hex string..." to somebody trying to open a colleague's file.
        var path = Sealed(Payload(64));
        var h = Seal.Peek(path);
        Replace(path, Seal.ManifestEntry,
            JsonSerializer.SerializeToUtf8Bytes(h with { Kdf = h.Kdf with { Salt = "zzzz" } },
                Json.Options));

        var ex = Assert.Throws<SealedSetException>(() => Opened(path));
        Assert.Contains("salt", ex.Message);
    }

    [Fact]
    public void An_absurd_frame_size_is_refused_rather_than_allocated()
    {
        // The header MAC has already passed here, so these numbers came from whoever held the
        // passphrase - but they size an allocation and drive a loop, and a header claiming a 2 GB
        // frame should be a refusal rather than an OutOfMemoryException.
        var path = Sealed(Payload(64));
        var h = Seal.Peek(path);

        // Re-MAC'd, so the shape check is what rejects it rather than the MAC.
        var forged = h with { Cipher = h.Cipher with { FrameBytes = int.MaxValue } };
        Replace(path, Seal.ManifestEntry, Resealed(forged));

        var ex = Assert.Throws<SealedSetException>(() => Opened(path));
        Assert.Contains("payload shape", ex.Message);
    }

    /// <summary>Serializes a header with a MAC that is valid for it — a forgery by someone who has
    /// the passphrase, which is the only way past the MAC and so the only way to reach the checks
    /// that sit behind it.</summary>
    private static byte[] Resealed(SealManifest header)
    {
        byte[] master = Rfc2898DeriveBytes.Pbkdf2(Pass, Convert.FromHexString(header.Kdf.Salt),
            header.Kdf.Iterations, HashAlgorithmName.SHA256, 64);
        byte[] mac = HMACSHA256.HashData(master[32..],
            Encoding.UTF8.GetBytes((header with { PolicyMac = string.Empty }).Canonical()));
        return JsonSerializer.SerializeToUtf8Bytes(
            header with { PolicyMac = Convert.ToHexStringLower(mac) }, Json.Options);
    }

    [Fact]
    public void A_seal_from_a_future_schema_is_refused_rather_than_misread()
    {
        // The same gate every other manifest goes through. A future field could change what the
        // header MEANS, and a reader that ignores it opens the file under rules it cannot see.
        var path = Sealed(Payload(64));
        var h = Seal.Peek(path);
        Replace(path, Seal.ManifestEntry,
            JsonSerializer.SerializeToUtf8Bytes(h with { SchemaVersion = 99 }, Json.Options));

        Assert.Throws<UnsupportedSchemaVersionException>(() => Seal.Peek(path));
    }
}
