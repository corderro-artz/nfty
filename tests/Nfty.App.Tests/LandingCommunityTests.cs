using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Landing's Browse the community gallery button and its New/Updated chip.</summary>
public class LandingCommunityTests
{
    private static LandingViewModel Landing(FakeNav nav, FakeCommunityCache? cache, ICommunitySeen? seen, bool withPage = true) =>
        new(nav, new FakeDialogs(), new FilePickerService(), new RecentsService(Directory.CreateTempSubdirectory().FullName),
            new CookBookSession(), _ => null!, _ => null!, (_, _, _) => null!,
            community: withPage ? () => CommunityFixtures.Page(cache ?? new FakeCommunityCache()) : null,
            communityCache: cache, communitySeen: seen);

    /// <summary>A cache holding twelve entries, for frames that need the chip inked.</summary>
    internal static FakeCommunityCache InkedCache() => new()
    {
        Cached = CommunityFixtures.Listing(Enumerable.Range(1, 12).Select(i => CommunityFixtures.Entry("e" + i))),
    };

    /// <summary>A reader whose first visit was before every fixture entry, so all of them are new.</summary>
    internal static CommunitySeen InkedSeen() => new(Directory.CreateTempSubdirectory("nfty-seen-").FullName,
        () => CommunityFixtures.T0.AddDays(-1));

    [Fact]
    public void Landing_chip_uses_the_cached_index_only()
    {
        var cache = new FakeCommunityCache
        {
            Cached = CommunityFixtures.Listing([CommunityFixtures.Entry("a"), CommunityFixtures.Entry("b")]),
            // What a network refresh would have produced: must never be read.
            Next = CommunityFixtures.Listing([CommunityFixtures.Entry("a")]),
        };
        // The reader first visited before the entries were shared, so both are new.
        var seen = new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName,
            () => CommunityFixtures.T0.AddDays(-1));
        var vm = Landing(new FakeNav(), cache, seen);

        Assert.Equal("2 new · 0 updated", vm.CommunityChip);
        Assert.True(vm.HasCommunityChip);
        Assert.Equal(1.0, vm.CommunityChipOpacity);
        Assert.Equal(2, cache.Cached.Index!.Entries.Count);   // no refresh replaced the cached list
    }

    [Fact]
    public void Landing_chip_has_no_ink_with_nothing_cached_or_nothing_new()
    {
        var seen = new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName);
        var none = Landing(new FakeNav(), new FakeCommunityCache(), seen);
        Assert.Equal("", none.CommunityChip);
        Assert.Equal(0.0, none.CommunityChipOpacity);

        var cache = new FakeCommunityCache { Cached = CommunityFixtures.Listing([CommunityFixtures.Entry("a")]) };
        seen.SeeAll(cache.Cached.Index!);
        var allSeen = Landing(new FakeNav(), cache, seen);
        Assert.Equal("", allSeen.CommunityChip);
        Assert.False(allSeen.HasCommunityChip);
    }

    [Fact]
    public void Landing_browse_the_community_gallery_opens_the_community_page()
    {
        var nav = new FakeNav();
        var vm = Landing(nav, null, null);
        Assert.True(vm.BrowseCommunityCommand.CanExecute(null));
        vm.BrowseCommunityCommand.Execute(null);
        Assert.IsType<CommunityViewModel>(nav.Current);
        Assert.False(Landing(new FakeNav(), null, null, withPage: false).BrowseCommunityCommand.CanExecute(null));
    }
}
