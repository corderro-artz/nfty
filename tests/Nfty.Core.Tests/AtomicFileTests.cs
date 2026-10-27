using Nfty.Core.Formats;

namespace Nfty.Core.Tests;

public class AtomicFileTests
{
    private static string Dir() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Replace_writes_through_a_unique_temp_and_moves_it_into_place()
    {
        var dir = Dir();
        var path = Path.Combine(dir, "a.txt");
        File.WriteAllText(path, "old");
        string? seen = null;

        AtomicFile.Replace(path, tmp =>
        {
            seen = tmp;
            Assert.False(File.Exists(tmp));
            Assert.Equal(dir, Path.GetDirectoryName(tmp));
            Assert.NotEqual(path + ".tmp", tmp);
            File.WriteAllText(tmp, "new");
        });

        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(File.Exists(seen));
        Assert.Single(Directory.GetFiles(dir));
        Assert.NotEqual(AtomicFile.TempBeside(path), AtomicFile.TempBeside(path));
    }

    [Fact]
    public void A_throwing_writer_leaves_the_original_untouched_and_no_temp_behind()
    {
        var dir = Dir();
        var path = Path.Combine(dir, "a.txt");
        File.WriteAllText(path, "old");

        var ex = Assert.Throws<InvalidOperationException>(() => AtomicFile.Replace(path, tmp =>
        {
            File.WriteAllText(tmp, "half");
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal("boom", ex.Message);
        Assert.Equal("old", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir));
    }

    [Fact]
    public void A_stale_fixed_tmp_beside_the_file_does_not_block_the_replace()
    {
        var dir = Dir();
        var path = Path.Combine(dir, "a.cbk");
        File.WriteAllText(path + ".tmp", "stale");

        AtomicFile.Replace(path, tmp =>
        {
            // CreateNew, like ZipArchiveMode.Create: a colliding temp name would throw here.
            using var fs = new FileStream(tmp, FileMode.CreateNew);
            fs.WriteByte(7);
        });

        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(path));
        Assert.Equal("stale", File.ReadAllText(path + ".tmp"));
    }

    [Fact]
    public async Task ReplaceAsync_matches_Replace()
    {
        var dir = Dir();
        var path = Path.Combine(dir, "a.txt");
        File.WriteAllText(path, "old");

        await AtomicFile.ReplaceAsync(path, async tmp => await File.WriteAllTextAsync(tmp, "new"));
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir));

        await Assert.ThrowsAsync<InvalidOperationException>(() => AtomicFile.ReplaceAsync(path, async tmp =>
        {
            await File.WriteAllTextAsync(tmp, "half");
            throw new InvalidOperationException("boom");
        }));
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir));
    }
}
