using System.Text;
using Nfty.Core.Formats;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class CommunityIndexTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    private static CommunityEntry Entry(long owner = 1, string login = "ann", string slug = "cats",
        string fingerprint = "fp1", string setSha = "aa", string name = "Cats",
        IReadOnlyList<string>? tags = null, DateTimeOffset? created = null, DateTimeOffset? updated = null) =>
        new(1, slug, new CommunityOwner(owner, login), "nfty-public", "cats-v1", name, "Cats", "desc",
            tags ?? new[] { "pixel", "animals" }, new CommunityCanvas(64, 64), 100, false, false, null,
            new CommunityFile("https://x/set", 10, setSha), new CommunityFile("https://x/p.png", 5, "bb"),
            fingerprint, "1.0.0", created ?? T0, updated ?? T0);

    private static Stream S(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Round_trip_is_byte_identical()
    {
        var index = new CommunityIndex(1, T0, new[] { Entry(), Entry(owner: 2, login: "bo", slug: "dogs") });
        var first = CommunityIndex.Write(index);
        var second = CommunityIndex.Write(CommunityIndex.Read(new MemoryStream(first)));
        Assert.Equal(first, second);

        var repo = new PublicRepoManifest(1, new[] { Entry() });
        var r1 = PublicRepoManifest.Write(repo);
        Assert.Equal(r1, PublicRepoManifest.Write(PublicRepoManifest.Read(new MemoryStream(r1))));
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var json = Encoding.UTF8.GetString(CommunityIndex.Write(new CommunityIndex(1, T0, new[] { Entry() })))
            .Replace("\"entries\"", "\"future\": 7, \"entries\"");
        var read = CommunityIndex.Read(S(json));
        Assert.Single(read.Entries);
    }

    [Fact]
    public void A_newer_index_is_refused_by_name()
    {
        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() =>
            CommunityIndex.Read(S("{\"schemaVersion\":2,\"generated\":\"2026-11-01T10:00:00+00:00\",\"entries\":[]}")));
        Assert.Equal(ShareRules.IndexTooNew, ex.Message);
        Assert.Throws<UnsupportedSchemaVersionException>(() =>
            PublicRepoManifest.Read(S("{\"schemaVersion\":2,\"shares\":[]}")));
    }

    [Fact]
    public void Write_sorts_ordinally_by_owner_then_slug()
    {
        var index = new CommunityIndex(1, T0, new[]
        {
            Entry(owner: 10, slug: "a"), Entry(owner: 2, slug: "b"), Entry(owner: 2, slug: "B"), Entry(owner: 2, slug: "a"),
        });
        var read = CommunityIndex.Read(new MemoryStream(CommunityIndex.Write(index)));
        Assert.Equal(new[] { "2/B", "2/a", "2/b", "10/a" }, read.Entries.Select(e => e.Id));
    }

    [Fact]
    public void SameShare_ignores_updated_and_catches_a_changed_tag()
    {
        var a = Entry();
        Assert.True(CommunityEntry.SameShare(a, Entry(updated: T0.AddDays(3))));
        Assert.False(CommunityEntry.SameShare(a, Entry(tags: new[] { "pixel", "robots" })));
        Assert.False(CommunityEntry.SameShare(a, Entry(name: "Other")));
    }

    [Fact]
    public void Same_fingerprint_by_another_owner_is_already_listed() =>
        Assert.Equal(ShareRules.AlreadyListed("ann"),
            CommunityDuplicates.Problem(Entry(owner: 2, login: "bo", setSha: "zz"), new[] { Entry() }));

    [Fact]
    public void Same_file_hash_by_another_owner_is_already_listed() =>
        Assert.Equal(ShareRules.AlreadyListed("ann"),
            CommunityDuplicates.Problem(Entry(owner: 2, login: "bo", fingerprint: "other"), new[] { Entry() }));

    [Fact]
    public void Own_set_under_another_slug_is_refused() =>
        Assert.Equal(ShareRules.AlreadySharedAs("Cats"),
            CommunityDuplicates.Problem(Entry(slug: "cats2", name: "Cats Two", setSha: "zz"), new[] { Entry() }));

    [Fact]
    public void Unchanged_update_is_refused_with_its_date() =>
        Assert.Equal(ShareRules.NothingChanged("Cats", T0.AddDays(2)),
            CommunityDuplicates.Problem(Entry(updated: T0.AddDays(5)), new[] { Entry(updated: T0.AddDays(2)) }));

    [Fact]
    public void Changed_own_set_on_its_slug_is_not_a_duplicate() =>
        Assert.Null(CommunityDuplicates.Problem(Entry(fingerprint: "new", setSha: "new"), new[] { Entry() }));

    [Fact]
    public void Dates_keep_created_and_move_updated_forward()
    {
        var prev = Entry(updated: T0);
        var pr = T0.AddDays(3);
        Assert.Null(CommunityDates.Problem(Entry(updated: T0.AddDays(1)), prev, pr));
        Assert.Null(CommunityDates.Problem(Entry(), null, T0));
        Assert.Equal(ShareRules.DatesMismatch, CommunityDates.Problem(Entry(created: T0.AddDays(1), updated: T0.AddDays(2)), prev, pr));
        Assert.Equal(ShareRules.DatesMismatch, CommunityDates.Problem(Entry(updated: T0), prev, pr));
    }

    [Fact]
    public void Updated_ahead_of_the_pr_is_refused()
    {
        var pr = T0.AddDays(1);
        Assert.Equal(ShareRules.DatesMismatch, CommunityDates.Problem(Entry(updated: pr.AddHours(1).AddMinutes(1)), null, pr));
        Assert.Null(CommunityDates.Problem(Entry(updated: pr.AddHours(1)), null, pr));
    }
}
