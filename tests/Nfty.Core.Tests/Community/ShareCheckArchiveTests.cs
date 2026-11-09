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
        string path = Zip(z => Put(z, "extras/a.png", Noise(20)));
        Assert.Equal([ShareRules.UnexpectedFile("extras/a.png")], ShareCheck.CheckArchive(path));
    }

    [Fact]
    public void One_root_cbk_is_allowed_two_are_not()
    {
        string one = Zip(z => Put(z, "a.cbk", Noise(20)));
        Assert.Empty(ShareCheck.CheckArchive(one));
        string two = Zip(z => { Put(z, "a.cbk", Noise(20)); Put(z, "b.cbk", Noise(20)); });
        Assert.Equal([ShareRules.UnexpectedFile("b.cbk")], ShareCheck.CheckArchive(two));
    }

    [Fact]
    public void Stray_files_are_listed_but_capped_at_ten()
    {
        string path = Zip(z => { for (int i = 0; i < 15; i++) Put(z, $"junk{i}.txt", Noise(10)); });
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
}
