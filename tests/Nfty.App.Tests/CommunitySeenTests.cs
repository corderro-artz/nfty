using Nfty.App.Services;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

public sealed class CommunitySeenTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private readonly string _root = Directory.CreateTempSubdirectory("nfty-seen-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private CommunitySeen Seen() => new(_root, () => Start);

    private static CommunityEntry E(string slug, int createdDays, int updatedDays = 0) =>
        CommunityCacheTests.Entry(slug, created: Start.AddDays(createdDays), updated: Start.AddDays(createdDays + updatedDays));

    [Fact]
    public void The_first_visit_marks_nothing()
    {
        var seen = Seen();
        Assert.Equal(SeenMark.None, seen.MarkOf(E("old", -10)));
        Assert.Equal(Start, StateJson.TryRead<System.Text.Json.Nodes.JsonObject>(File.ReadAllText(Path.Combine(_root, "seen.json")))!["firstVisit"]!.GetValue<DateTimeOffset>());
    }

    [Fact]
    public void A_share_created_after_the_first_visit_is_new()
    {
        Assert.Equal(SeenMark.New, Seen().MarkOf(E("fresh", 1)));
    }

    [Fact]
    public void A_later_updated_is_updated_not_new()
    {
        var seen = Seen();
        seen.See(E("a", 1));
        Assert.Equal(SeenMark.None, seen.MarkOf(E("a", 1)));
        Assert.Equal(SeenMark.Updated, seen.MarkOf(E("a", 1, updatedDays: 2)));
    }

    [Fact]
    public void Selecting_marks_seen_and_survives_a_restart()
    {
        var seen = Seen();
        var e = E("a", 1);
        seen.See(e);
        Assert.Equal(SeenMark.None, seen.MarkOf(e));
        seen.Flush();   // marks are written after a quiet second, or when the page closes
        Assert.Equal(SeenMark.None, Seen().MarkOf(e));
    }

    [Fact]
    public void See_all_clears_every_mark()
    {
        var seen = Seen();
        var index = new CommunityIndex(1, Start, [E("a", 1), E("b", 2)]);
        seen.SeeAll(index);
        Assert.Equal((0, 0), seen.Count(index));
    }

    [Fact]
    public void Counts_match_marks()
    {
        var seen = Seen();
        var upd = E("u", 1);
        seen.See(upd);
        var index = new CommunityIndex(1, Start, [E("n1", 1), E("n2", 3), E("old", -3), E("u", 1, updatedDays: 4)]);
        Assert.Equal((2, 1), seen.Count(index));
        Assert.Equal(2, index.Entries.Count(e => seen.MarkOf(e) == SeenMark.New));
    }

    [Fact]
    public void A_corrupt_seen_file_starts_over_without_throwing()
    {
        File.WriteAllText(Path.Combine(_root, "seen.json"), "{ not json");
        var seen = new CommunitySeen(_root, () => Start.AddDays(10));
        Assert.Equal(SeenMark.None, seen.MarkOf(E("x", 5)));
        Assert.Equal(SeenMark.New, seen.MarkOf(E("y", 11)));
    }
}
