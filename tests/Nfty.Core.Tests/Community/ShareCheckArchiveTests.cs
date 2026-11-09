using System.IO.Compression;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class ShareCheckArchiveTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("nfty-sharecheck-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Zip(Action<ZipArchive> fill)
    {
        string path = Path.Combine(_dir.FullName, Guid.NewGuid().ToString("N") + ".set");
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            fill(zip);
        return path;
    }

    private static void Put(ZipArchive zip, string name, byte[]? bytes = null)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        if (bytes is not null) s.Write(bytes);
    }

    private static byte[] Noise(int n)
    {
        var b = new byte[n];
        new Random(7).NextBytes(b);
        return b;
    }

    private static string Unsafe(string reason) => ShareRules.UnsafeArchive(reason);

    [Fact]
    public void A_clean_set_layout_passes()
    {
        string path = Zip(z =>
        {
            Put(z, "set.json", "{}"u8.ToArray());
            Put(z, "images/0001.png", Noise(200));
            Put(z, "metadata/0001.json", "{}"u8.ToArray());
            Put(z, "nfty/0001.json", "{}"u8.ToArray());
            Put(z, "models/0001.glb", Noise(100));
            Put(z, "images/");
            Put(z, "book.cbk", Noise(100));
        });
        Assert.Empty(ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void Too_many_entries_is_refused()
    {
        string path = Zip(z => { for (int i = 0; i < 6001; i++) Put(z, $"images/{i}.png"); });
        Assert.Equal([Unsafe(ShareRules.ReasonTooManyFiles)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void A_zip_bomb_by_ratio_is_refused()
    {
        string path = Zip(z => Put(z, "images/0001.png", new byte[10 * 1024 * 1024]));
        Assert.Equal([Unsafe(ShareRules.ReasonExpandsTooFar)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void Total_expansion_over_one_gib_is_refused()
    {
        long half = ShareRules.MaxUncompressedBytes / 2 + 1;
        // Ratio 1 each, so only the total can refuse.
        Assert.Equal(Unsafe(ShareRules.ReasonExpandsTooFar),
            ShareCheck.CheckSizes([(half, half), (half, half)]));
        Assert.Null(ShareCheck.CheckSizes([(100, 100), (50, 120)]));
    }

    [Fact]
    public void The_ratio_limit_is_per_entry_and_inclusive()
    {
        Assert.Null(ShareCheck.CheckSizes([(10, 10 * ShareRules.MaxEntryRatio)]));
        Assert.Equal(Unsafe(ShareRules.ReasonExpandsTooFar),
            ShareCheck.CheckSizes([(10, 10 * ShareRules.MaxEntryRatio + 1)]));
        Assert.Equal(Unsafe(ShareRules.ReasonExpandsTooFar), ShareCheck.CheckSizes([(0, 101)]));
    }

    [Fact]
    public void Dot_dot_path_is_refused()
    {
        string path = Zip(z => Put(z, "images/../evil.png", Noise(50)));
        Assert.Equal([Unsafe(ShareRules.ReasonNameLeaves)], ShareCheck.CheckArchive(path));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:/win.png")]
    [InlineData("c:evil.png")]
    public void Absolute_and_drive_paths_are_refused(string name)
    {
        string path = Zip(z => Put(z, name, Noise(50)));
        Assert.Equal([Unsafe(ShareRules.ReasonNameLeaves)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void Backslash_path_is_refused()
    {
        string path = Zip(z => Put(z, "images\\0001.png", Noise(50)));
        Assert.Equal([Unsafe(ShareRules.ReasonNameLeaves)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void A_symlink_entry_is_refused()
    {
        string path = Zip(z =>
        {
            var e = z.CreateEntry("images/0001.png");
            using (var s = e.Open()) s.Write("target"u8);
            e.ExternalAttributes = 0xA1FF << 16;
        });
        Assert.Equal([Unsafe(ShareRules.ReasonHoldsLink)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void Unexpected_root_file_is_refused()
    {
        string path = Zip(z => { Put(z, "set.json", Noise(20)); Put(z, "notes.txt", Noise(20)); });
        Assert.Equal([ShareRules.UnexpectedFile("notes.txt")], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void A_file_in_an_unlisted_folder_is_refused()
    {
        string path = Zip(z => { Put(z, "set.json", "{}"u8.ToArray()); Put(z, "extras/a.png", Noise(20)); });
        Assert.Equal([ShareRules.UnexpectedFile("extras/a.png")], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void One_root_cbk_is_allowed_two_are_not()
    {
        string one = Zip(z => { Put(z, "set.json", "{}"u8.ToArray()); Put(z, "a.cbk", Noise(20)); });
        Assert.Empty(ShareCheck.CheckArchive(one));
        string two = Zip(z => { Put(z, "set.json", "{}"u8.ToArray()); Put(z, "a.cbk", Noise(20)); Put(z, "b.cbk", Noise(20)); });
        Assert.Equal([ShareRules.UnexpectedFile("b.cbk")], ShareCheck.CheckArchive(two));
    }

    [Fact]
    public void Stray_files_are_listed_but_capped_at_ten()
    {
        string path = Zip(z => { Put(z, "set.json", "{}"u8.ToArray()); for (int i = 0; i < 15; i++) Put(z, $"junk{i}.txt", Noise(10)); });
        var problems = ShareCheck.CheckArchive(path);
        Assert.Equal(10, problems.Count);
        Assert.Equal(ShareRules.UnexpectedFile("junk0.txt"), problems[0]);
    }

    [Fact]
    public void Over_200_mb_is_refused_before_opening()
    {
        string path = Path.Combine(_dir.FullName, "big.set");
        using (var fs = File.Create(path)) fs.SetLength(ShareRules.MaxSetBytes + 1);
        Assert.Equal([ShareRules.SetTooLarge(ShareRules.MaxSetBytes + 1, 0)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_not_a_set()
    {
        string path = Path.Combine(_dir.FullName, "x.set");
        File.WriteAllText(path, "hello, not a zip");
        Assert.Equal([ShareRules.NotASet], ShareCheck.CheckArchive(path));
    }

    // ---- hardening carried from review

    [Fact]
    public void A_zip_with_no_root_set_json_is_not_a_set()
    {
        string path = Zip(z => Put(z, "images/0001.png", Noise(50)));
        Assert.Equal([ShareRules.NotASet], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void An_empty_archive_is_not_a_set()
    {
        string path = Zip(_ => { });
        Assert.Equal([ShareRules.NotASet], ShareCheck.CheckArchive(path));
    }

    [Theory]
    [InlineData("images/a:b.png")]
    [InlineData("images/./x.png")]
    [InlineData("images//x.png")]
    [InlineData("./set2.json")]
    [InlineData("images//")]
    public void Odd_names_are_refused(string name)
    {
        string path = Zip(z => { Put(z, "set.json", "{}"u8.ToArray()); Put(z, name, Noise(20)); });
        Assert.Equal([Unsafe(ShareRules.ReasonNameLeaves)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void Duplicate_entry_names_are_refused()
    {
        string path = Zip(z =>
        {
            Put(z, "set.json", "{}"u8.ToArray());
            Put(z, "images/0001.png", Noise(20));
            Put(z, "images/0001.png", Noise(20));
        });
        Assert.Equal([Unsafe(ShareRules.ReasonDuplicateName)], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void Exactly_6000_entries_pass()
    {
        string path = Zip(z =>
        {
            Put(z, "set.json", "{}"u8.ToArray());
            for (int i = 1; i < 6000; i++) Put(z, $"images/{i}.png");
        });
        Assert.Empty(ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void A_wrong_case_directory_is_a_stray_file()
    {
        string path = Zip(z => { Put(z, "set.json", "{}"u8.ToArray()); Put(z, "Images/x", Noise(20)); });
        Assert.Equal([ShareRules.UnexpectedFile("Images/x")], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void Real_bytes_past_the_total_are_refused()
    {
        string path = Zip(z => { Put(z, "set.json", Noise(600)); Put(z, "images/1.png", Noise(600)); });
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(Unsafe(ShareRules.ReasonExpandsTooFar), ShareCheck.CheckActualBytes(zip, 1000));
        Assert.Null(ShareCheck.CheckActualBytes(zip, 1200));
    }

    [Fact]
    public void A_lying_central_directory_cannot_make_an_entry_inflate_past_its_claim()
    {
        // 1 MiB of zeros deflates to ~1 KB; the central directory is then rewritten to claim 1000 bytes.
        string path = Zip(z => { Put(z, "set.json", "{}"u8.ToArray()); Put(z, "images/1.png", new byte[1 << 20]); });
        var bytes = File.ReadAllBytes(path);
        int patched = 0;
        for (int i = 0; i + 30 < bytes.Length; i++)
        {
            bool central = bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 1 && bytes[i + 3] == 2;
            bool local = bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 3 && bytes[i + 3] == 4;
            int at = central ? i + 24 : local ? i + 22 : -1;
            if (at < 0 || BitConverter.ToInt32(bytes, at) != 1 << 20) continue;
            BitConverter.GetBytes(1000).CopyTo(bytes, at);
            patched++;
        }
        Assert.True(patched >= 1);
        File.WriteAllBytes(path, bytes);

        // The runtime's reader stops at the declared length, so this passes and no byte past the
        // claim is ever read. Pinned as the premise the per-entry guard rests on: if a runtime ever
        // inflates past the claim, CheckActualBytes refuses it and this test stays green, but the
        // read below would show more than 1000 bytes.
        using var zip = ZipFile.OpenRead(path);
        Assert.Null(ShareCheck.CheckActualBytes(zip));
        using var s = zip.GetEntry("images/1.png")!.Open();
        using var sink = new MemoryStream();
        s.CopyTo(sink);
        Assert.True(sink.Length <= 1000);
    }
}
