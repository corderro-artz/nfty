using Nfty.App.Services;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Which Release tags the scan keeps while an update waits for the gallery.</summary>
public class ShareProtectionTests
{
    private const long Me = 7;

    private static CommunityEntry Live(string slug, long owner = Me) =>
        CommunityFixtures.Entry(slug, slug, "me", ownerId: owner) with { Release = slug + "-v1" };

    private static CommunityIndex Index(params CommunityEntry[] entries) => new(1, DateTimeOffset.UnixEpoch, entries);

    private static PendingRecord Update(string slug, int n, bool closed = false, long owner = Me) =>
        new(PendingRecord.ShareKind, 10 + n, "https://github.com/x/y/pull/" + (10 + n), slug, owner, "me", slug, 1, 8, 8, false, false, 1,
            $"{slug}-v{n - 1}", null, true, false, DateTimeOffset.UnixEpoch, closed, [], $"{slug}-v{n}");

    [Fact]
    public void An_open_update_keeps_both_the_live_version_and_the_new_one()
    {
        var tags = ShareProtection.Tags(Index(Live("cat")), [Update("cat", 2)], Me);
        Assert.Equal(["cat-v1", "cat-v2"], tags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Two_open_updates_keep_all_three_versions()
    {
        var tags = ShareProtection.Tags(Index(Live("cat")), [Update("cat", 2), Update("cat", 3)], Me);
        Assert.Equal(["cat-v1", "cat-v2", "cat-v3"], tags.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_refused_update_still_keeps_the_version_the_gallery_lists()
    {
        // The closed record stops protecting its new tag (a leftover the author may remove); the live one stays through the list.
        var tags = ShareProtection.Tags(Index(Live("cat")), [Update("cat", 2, closed: true)], Me);
        Assert.Equal(["cat-v1"], tags);
    }

    [Fact]
    public void Another_accounts_entries_and_records_protect_nothing()
    {
        var tags = ShareProtection.Tags(Index(Live("cat", owner: 99), Live("dog")), [Update("cat", 2, owner: 99)], Me);
        Assert.Equal(["dog-v1"], tags);
    }

    [Fact]
    public void No_list_and_nothing_pending_protects_nothing_and_a_record_without_a_tag_is_fine()
    {
        Assert.Empty(ShareProtection.Tags(null, [], Me));
        Assert.Empty(ShareProtection.Tags(null, [Update("cat", 2) with { Tag = null, SupersededTag = null }], Me));
    }
}
