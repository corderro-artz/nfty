using System.Text.Json;
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

    // ---- the move off %APPDATA% -------------------------------------------------------------------

    /// <summary>Writes a pre-.nfty recents file at <paramref name="path"/>. Never the real
    /// %APPDATA% one — that is why the legacy path is a parameter rather than a default.</summary>
    private static void WriteLegacy(string path, params string[] names)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var items = names.Select(n => new RecentItem(n, "meta", Path.Combine(Path.GetTempPath(), n + ".cbk"), false));
        File.WriteAllText(path, JsonSerializer.Serialize(items,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public void The_pre_store_list_is_migrated_so_nobody_loses_their_landing_screen()
    {
        var oldDir = TempDir();
        var newDir = TempDir();
        try
        {
            var legacy = Path.Combine(oldDir, "recents.json");
            WriteLegacy(legacy, "A", "B", "C");

            var svc = new RecentsService(StateStore.At(newDir), legacy);

            Assert.Equal(new[] { "A", "B", "C" }, svc.Items.Select(i => i.Name));
            // Written through to the new home, so the migration is not re-done next launch.
            Assert.True(File.Exists(Path.Combine(newDir, RecentsService.FileName)));
            Assert.Equal(new[] { "A", "B", "C" },
                new RecentsService(StateStore.At(newDir)).Items.Select(i => i.Name));
        }
        finally { Delete(oldDir, newDir); }
    }

    [Fact]
    public void The_old_file_is_left_where_it_is()
    {
        var oldDir = TempDir();
        var newDir = TempDir();
        try
        {
            var legacy = Path.Combine(oldDir, "recents.json");
            WriteLegacy(legacy, "A");
            var before = File.ReadAllText(legacy);

            _ = new RecentsService(StateStore.At(newDir), legacy);

            // Never deleted: someone who goes back to an older build still has their list.
            Assert.True(File.Exists(legacy));
            Assert.Equal(before, File.ReadAllText(legacy));
        }
        finally { Delete(oldDir, newDir); }
    }

    [Fact]
    public void Migration_happens_once_and_does_not_resurrect_a_list_the_user_cleared()
    {
        var oldDir = TempDir();
        var newDir = TempDir();
        try
        {
            var legacy = Path.Combine(oldDir, "recents.json");
            WriteLegacy(legacy, "A", "B");

            var first = new RecentsService(StateStore.At(newDir), legacy);
            foreach (var item in first.Items.ToList()) first.Remove(item.Path);
            Assert.Empty(first.Items);

            // The store now holds an EMPTY list — which is not the same as holding no file. Keying
            // migration off "no entries" instead of "no file" would bring A and B back every launch.
            var second = new RecentsService(StateStore.At(newDir), legacy);

            Assert.Empty(second.Items);
        }
        finally { Delete(oldDir, newDir); }
    }

    [Fact]
    public void An_existing_list_in_the_store_is_never_overwritten_by_the_old_one()
    {
        var oldDir = TempDir();
        var newDir = TempDir();
        try
        {
            var legacy = Path.Combine(oldDir, "recents.json");
            WriteLegacy(legacy, "OLD");
            var svc = new RecentsService(StateStore.At(newDir));
            svc.Add(new RecentItem("NEW", "meta", Path.Combine(newDir, "new.cbk"), false));

            var reopened = new RecentsService(StateStore.At(newDir), legacy);

            Assert.Equal(new[] { "NEW" }, reopened.Items.Select(i => i.Name));
        }
        finally { Delete(oldDir, newDir); }
    }

    [Fact]
    public void A_missing_or_corrupt_old_file_migrates_nothing_and_throws_nothing()
    {
        var oldDir = TempDir();
        var newDir = TempDir();
        try
        {
            var absent = Path.Combine(oldDir, "gone.json");
            Assert.Empty(new RecentsService(StateStore.At(newDir), absent).Items);

            var corrupt = Path.Combine(oldDir, "recents.json");
            File.WriteAllText(corrupt, "{ not json");
            Assert.Empty(new RecentsService(StateStore.At(newDir), corrupt).Items);
        }
        finally { Delete(oldDir, newDir); }
    }

    [Fact]
    public void Nothing_is_persisted_when_there_is_nowhere_to_write_and_nothing_throws()
    {
        var dir = TempDir();
        try
        {
            // Pinned at a path that is a FILE, so every write fails.
            var blocked = Path.Combine(dir, "not-a-folder");
            File.WriteAllText(blocked, "x");
            var svc = new RecentsService(StateStore.At(blocked));

            svc.Add(new RecentItem("A", "meta", Path.Combine(dir, "a.cbk"), false));   // must not throw

            Assert.Single(svc.Items);   // the session still has it
            Assert.Empty(new RecentsService(StateStore.At(blocked)).Items);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_legacy_path_is_named_only_where_it_is_wanted()
    {
        // A property rather than a constructor default, so a test can never reach the developer's
        // own list by omission. Nothing but the composition root passes it.
        Assert.EndsWith(Path.Combine("nfty", "recents.json"), RecentsService.LegacyFile, StringComparison.Ordinal);
        Assert.Equal("recents.json", RecentsService.FileName);
    }

    private static void Delete(params string[] dirs)
    {
        foreach (var d in dirs)
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
    }
}
