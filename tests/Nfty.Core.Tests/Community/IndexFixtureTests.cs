using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.Core.Tests.Community;

/// <summary>
/// The gallery repo's <c>build_index.py</c> must write the same bytes <see cref="CommunityIndex.Write"/> does. This test pins
/// the C# side to a committed file the Python test compares against, so neither side can drift alone.
/// </summary>
public class IndexFixtureTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("nfty.sln not found above the test binary.");
    }

    /// <summary>The three entries the Python fixture is built from: an out-of-order list, a non-ASCII name, an offset date, no book hash.</summary>
    public static CommunityIndex Fixture()
    {
        var t = new DateTimeOffset(2026, 11, 9, 14, 2, 0, TimeSpan.Zero);
        CommunityEntry E(long id, string login, string slug, string name, string description, string? book,
            DateTimeOffset created, DateTimeOffset updated, params string[] tags) =>
            new(1, slug, new CommunityOwner(id, login), $"{login}/nfty-public", $"{slug}-v1", name, name, description, tags,
                new CommunityCanvas(32, 32), 600, book is not null, book is not null, book,
                new CommunityFile($"https://github.com/{login}/nfty-public/releases/download/{slug}-v1/{slug}.set", 4194304, new string('a', 64)),
                new CommunityFile($"https://github.com/{login}/nfty-public/releases/download/{slug}-v1/preview.png", 81920, new string('b', 64)),
                new string('c', 64), "2.5.0", created, updated);
        return new CommunityIndex(1, new DateTimeOffset(2026, 11, 9, 14, 10, 0, TimeSpan.Zero), new[]
        {
            E(1234567, "someone", "vapor-chests", "Vapor Chests", "Chests & strongboxes, 29 drawings <3 + more.", new string('d', 64), t, t, "props", "pixel-art"),
            E(42, "ann", "cafe-cats", "Café Cats", "Naïve \"cats\" \\ it's ☃ \U0001F408 `x` \u007F \u0001.", null, t.AddDays(-3), t.AddHours(5).ToOffset(TimeSpan.FromHours(2)).AddTicks(1230000), "animals"),
            E(1234567, "someone", "bones", "Bones", "", null, t, t),
        });
    }

    [Fact]
    public void Index_fixture_for_python_is_current()
    {
        string path = Path.Combine(RepoRoot(), "tools", "tests", "gallery_fixtures", "index.expected.json");
        byte[] actual = CommunityIndex.Write(Fixture());
        if (Environment.GetEnvironmentVariable("NFTY_WRITE_FIXTURE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, actual);
        }
        Assert.True(File.Exists(path), $"{path} is missing; run this test once with NFTY_WRITE_FIXTURE=1 to write it.");
        Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(actual),
            $"{path} is out of date with CommunityIndex.Write; run this test with NFTY_WRITE_FIXTURE=1 and commit it (the Python index test reads it).");
    }
}
