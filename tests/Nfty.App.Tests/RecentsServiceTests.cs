using Nfty.App.Models;
using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>RecentsService persists to a JSON file under an injectable storage directory so tests
/// never touch the developer's real %APPDATA%. Every test below gets its own temp dir.</summary>
public class RecentsServiceTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void Add_dedupes_by_path_and_moves_to_the_front()
    {
        var dir = TempDir();
        try
        {
            var svc = new RecentsService(dir);
            var a = new RecentItem("A", "meta a", Path.Combine(dir, "a.cbk"), false);
            var b = new RecentItem("B", "meta b", Path.Combine(dir, "b.cbk"), false);
            svc.Add(a);
            svc.Add(b);
            svc.Add(a);   // re-add A → moves back to front, still one entry

            Assert.Equal(2, svc.Items.Count);
            Assert.Equal(Path.GetFullPath(a.Path), svc.Items[0].Path);
            Assert.Equal(Path.GetFullPath(b.Path), svc.Items[1].Path);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Add_caps_the_list_at_ten()
    {
        var dir = TempDir();
        try
        {
            var svc = new RecentsService(dir);
            for (int i = 0; i < 11; i++)
                svc.Add(new RecentItem($"Item{i}", "meta", Path.Combine(dir, $"item{i}.cbk"), false));

            Assert.Equal(10, svc.Items.Count);
            // the oldest (item0) is gone; the newest (item10) is at the front
            Assert.DoesNotContain(svc.Items, i => i.Path == Path.GetFullPath(Path.Combine(dir, "item0.cbk")));
            Assert.Equal(Path.GetFullPath(Path.Combine(dir, "item10.cbk")), svc.Items[0].Path);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Items_round_trip_through_the_file()
    {
        var dir = TempDir();
        try
        {
            var first = new RecentsService(dir);
            first.Add(new RecentItem("A", "meta a", Path.Combine(dir, "a.cbk"), false));
            first.Add(new RecentItem("B", "loose ingredient", Path.Combine(dir, "b.igt"), true));

            var second = new RecentsService(dir);
            Assert.Equal(first.Items.Select(i => i.Path), second.Items.Select(i => i.Path));
            Assert.Equal(first.Items.Select(i => i.Name), second.Items.Select(i => i.Name));
            Assert.Equal(first.Items.Select(i => i.Loose), second.Items.Select(i => i.Loose));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Remove_deletes_by_path()
    {
        var dir = TempDir();
        try
        {
            var svc = new RecentsService(dir);
            var a = new RecentItem("A", "meta a", Path.Combine(dir, "a.cbk"), false);
            var b = new RecentItem("B", "meta b", Path.Combine(dir, "b.cbk"), false);
            svc.Add(a);
            svc.Add(b);

            svc.Remove(svc.Items.Single(i => i.Name == "A").Path);

            Assert.Single(svc.Items);
            Assert.Equal("B", svc.Items[0].Name);

            var fresh = new RecentsService(dir);
            Assert.Single(fresh.Items);
            Assert.Equal("B", fresh.Items[0].Name);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_corrupt_store_loads_as_empty()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "recents.json"), "{ not json");
            var svc = new RecentsService(dir);
            Assert.Empty(svc.Items);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_first_run_is_empty()
    {
        var dir = TempDir();
        try
        {
            var svc = new RecentsService(dir);
            Assert.Empty(svc.Items);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
