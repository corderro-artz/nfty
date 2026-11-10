using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>A cache that answers from memory: a scripted listing, and one preview sheet for every entry.</summary>
internal sealed class FakeCommunityCache : ICommunityCache
{
    public CommunityListing? Next { get; set; }
    public CommunityListing? Cached { get; set; }
    public string? PreviewPath { get; set; }
    public HashSet<string> MissingPreviews { get; } = [];
    public string SetsDir { get; } = Path.Combine(Path.GetTempPath(), "nfty-fake-community-sets");

    public Task<CommunityListing> RefreshAsync(CancellationToken ct)
    {
        var listing = Next ?? new CommunityListing(null, DateTimeOffset.UtcNow, ShareRules.Unreachable);
        Cached = listing;
        return Task.FromResult(listing);
    }

    public Task<string?> PreviewAsync(CommunityFile file, CancellationToken ct) =>
        Task.FromResult(MissingPreviews.Contains(file.Url) ? null : PreviewPath);
}

/// <summary>An account that only knows its login; its client throws, as a revoked token's would.</summary>
internal sealed class FakeGitHubAccount : IGitHubAccount
{
    public string? Login { get; set; }
    public bool IsConnected => Login is not null;
    public string? UnavailableReason => null;
    public Task<DeviceCode> BeginAsync(CancellationToken cancellationToken) => throw new InvalidOperationException();
    public Task ConnectAsync(DeviceCode code, CancellationToken cancellationToken) => throw new InvalidOperationException();
    public void Disconnect() { }
    public GitHubShareClient? Client() => throw new InvalidOperationException("a revoked token");
    public event EventHandler? Changed { add { } remove { } }
}

/// <summary>The Community page's fixtures: entries, sheets and a ready view model.</summary>
internal static class CommunityFixtures
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    public static CommunityEntry Entry(string slug, string name = "Sample", string login = "alice", long ownerId = 7,
        int assets = 100, string[]? tags = null, bool models = false, bool book = false, string collection = "Coll",
        DateTimeOffset? created = null, DateTimeOffset? updated = null, string description = "A collection.")
    {
        var c = created ?? T0;
        return new CommunityEntry(1, slug, new CommunityOwner(ownerId, login), "nfty-public", "v1", name, collection,
            description, tags ?? [], new CommunityCanvas(16, 16), assets, models, book, null,
            new CommunityFile("https://example.test/" + slug + ".set", 4_194_304, new string('a', 64)),
            new CommunityFile("https://example.test/" + slug + ".png", 3, new string('b', 64)),
            "fp-" + slug, "1.0", c, updated ?? c);
    }

    public static CommunityListing Listing(IEnumerable<CommunityEntry> entries, string? notice = null) =>
        new(new CommunityIndex(1, T0, entries.ToList()), new DateTimeOffset(2026, 11, 9, 14, 2, 0, TimeSpan.Zero), notice);

    /// <summary>A 2 x 2 sheet of 16 px cells carrying the grid chunk, so the rail has cells to step through.</summary>
    public static string Sheet(string dir)
    {
        var path = Path.Combine(dir, "preview.png");
        using (var img = new Image<Rgba32>(32, 32))
        {
            Rgba32[] colors = [new(200, 40, 40, 255), new(40, 160, 70, 255), new(50, 90, 210, 255), new(230, 190, 40, 255)];
            for (int i = 0; i < 4; i++)
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        img[(i % 2) * 16 + x, (i / 2) * 16 + y] = (x + y) % 5 == 0 ? new Rgba32(255, 255, 255, 255) : colors[i];
            img.SaveAsPng(path);
        }
        Nfty.Core.Publish.SheetGrid.WriteInto(path, new Nfty.Core.Publish.SheetGrid(2, 2, 16, 16, 4));
        return path;
    }

    public static CommunityViewModel Page(FakeCommunityCache cache, ICommunitySeen? seen = null, IGitHubAccount? account = null,
        IUrlOpener? urls = null, IStatusService? status = null, int pageSize = 8)
    {
        var vm = new CommunityViewModel(cache, seen ?? new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName),
            account ?? new FakeGitHubAccount(), new FakeNav(), new FakeDialogs(), status ?? new StatusService(),
            _ => Task.CompletedTask, urls);
        vm.PageSize = pageSize;
        return vm;
    }

    public static async Task<CommunityViewModel> Opened(CommunityListing listing, ICommunitySeen? seen = null,
        IGitHubAccount? account = null, IUrlOpener? urls = null, int pageSize = 8, IStatusService? status = null)
    {
        var cache = new FakeCommunityCache { Next = listing };
        var vm = Page(cache, seen, account, urls, status, pageSize);
        await vm.PendingRefresh;
        return vm;
    }
}

public class CommunityPageTests
{
    private static CommunityEntry E(string slug, string name = "Sample", string login = "alice", int assets = 100,
        string[]? tags = null, string collection = "Coll", DateTimeOffset? updated = null, DateTimeOffset? created = null) =>
        CommunityFixtures.Entry(slug, name, login, assets: assets, tags: tags, collection: collection, updated: updated,
            created: created);

    private static string[] Names(CommunityViewModel vm) => vm.Tiles.Select(t => t.Name).ToArray();

    [Fact]
    public async Task Search_filters_by_name_owner_and_collection()
    {
        var listing = CommunityFixtures.Listing([
            E("a", "Dragons", "alice", collection: "Pets"),
            E("b", "Robots", "bob", collection: "Machines"),
            E("c", "Plants", "carol", collection: "Dragonfruit")]);
        using var vm = await CommunityFixtures.Opened(listing);

        vm.Query = "drag";
        Assert.Equal(["Dragons", "Plants"], Names(vm).Order().ToArray());   // name, and the collection "Dragonfruit"

        vm.Query = "@BOB";
        Assert.Equal(["Robots"], Names(vm));

        vm.Query = "carol";
        Assert.Equal(["Plants"], Names(vm));

        vm.Query = "nothing like it";
        Assert.Empty(vm.Tiles);
    }

    [Fact]
    public async Task Tag_filter_and_sort_compose()
    {
        var listing = CommunityFixtures.Listing([
            E("a", "Zed", assets: 10, tags: ["pets", "pixel"]),
            E("b", "Amy", assets: 300, tags: ["pets"]),
            E("c", "Mia", assets: 50, tags: ["pets"]),
            E("d", "Bob", assets: 999, tags: ["robots"])]);
        using var vm = await CommunityFixtures.Opened(listing);

        vm.SelectTagCommand.Execute("pets");
        vm.SortByCommand.Execute(CommunitySort.Name);
        Assert.Equal(["Amy", "Mia", "Zed"], Names(vm));

        vm.SortByCommand.Execute(CommunitySort.Assets);
        Assert.Equal(["Amy", "Mia", "Zed"], Names(vm));          // 300, 50, 10

        vm.Query = "m";                                           // Amy, Mia - and not Zed
        Assert.Equal(["Amy", "Mia"], Names(vm));

        vm.SelectTagCommand.Execute("");
        vm.Query = "";
        Assert.Equal(["Bob", "Amy", "Mia", "Zed"], Names(vm));    // 999, 300, 50, 10
        Assert.Contains(vm.TagChoices, c => c.Value == "pets" && !c.IsOn);
        Assert.True(vm.TagChoices[0].IsOn);
    }

    [Fact]
    public async Task Recently_updated_is_the_default_sort()
    {
        var old = CommunityFixtures.T0;
        var listing = CommunityFixtures.Listing([
            E("old", "Old", updated: old),
            E("new", "New", updated: old.AddDays(5)),
            E("mid-b", "MidB", updated: old.AddDays(2)),
            E("mid-a", "MidA", updated: old.AddDays(2))]);
        using var vm = await CommunityFixtures.Opened(listing);

        Assert.Equal(CommunitySort.RecentlyUpdated, vm.Sort);
        Assert.Equal(["New", "MidA", "MidB", "Old"], Names(vm));    // ties by id, ordinal
    }

    [Fact]
    public async Task Empty_states_say_which_emptiness()
    {
        // 1. entries exist, none match: Clear search undoes it.
        using var matching = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("a")]));
        matching.Query = "zzz";
        Assert.Equal(CommunityEmptyKind.NoMatches, matching.EmptyKind);
        Assert.Contains("zzz", matching.EmptyText);
        Assert.Equal("Clear search", matching.EmptyActionText);
        matching.EmptyActionCommand.Execute(null);
        Assert.Equal("", matching.Query);
        Assert.Equal(CommunityEmptyKind.None, matching.EmptyKind);

        // 2. the gallery is reachable and empty: a different sentence, and the click fetches again.
        using var empty = await CommunityFixtures.Opened(CommunityFixtures.Listing([]));
        Assert.Equal(CommunityEmptyKind.EmptyGallery, empty.EmptyKind);
        Assert.DoesNotContain("matches", empty.EmptyText);
        Assert.Equal("Try again", empty.EmptyActionText);

        // 3. never fetched, offline.
        using var offline = await CommunityFixtures.Opened(new CommunityListing(null, DateTimeOffset.UtcNow, ShareRules.Unreachable));
        Assert.Equal(CommunityEmptyKind.NeverFetchedOffline, offline.EmptyKind);
        Assert.Equal(ShareRules.Unreachable, offline.StatusLine);
        Assert.True(offline.StatusIsWarning);
        Assert.NotEqual(empty.EmptyText, offline.EmptyText);
    }

    [Fact]
    public async Task Browsing_works_with_a_revoked_token()
    {
        // The account's client throws, as a revoked token's does; browsing never asks for it.
        var account = new FakeGitHubAccount { Login = "alice" };
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("a"), E("b", "Other", "bob")]), account: account);

        vm.SelectCommand.Execute(vm.Tiles[0]);
        Assert.True(vm.DownloadCommand.CanExecute(null));
        Assert.True(vm.ReportCommand.CanExecute(null));
        Assert.Equal(2, vm.Tiles.Count);
        Assert.False(vm.StatusIsWarning);
    }

    [Fact]
    public async Task Offline_disables_download_with_the_offline_sentence()
    {
        var notice = ShareRules.Offline(new DateTime(2026, 11, 9, 13, 0, 0));
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("a")], notice));

        vm.SelectCommand.Execute(vm.Tiles[0]);

        Assert.False(vm.DownloadCommand.CanExecute(null));
        Assert.Equal(notice, vm.DownloadNote);
        Assert.Equal(notice, vm.StatusLine);
        Assert.True(vm.StatusIsWarning);
        Assert.True(vm.ReportCommand.CanExecute(null));   // reporting is a browser action, not a download
    }

    [Fact]
    public async Task A_newer_index_notice_shows_in_the_status_line_and_keeps_download_on()
    {
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("a")], ShareRules.IndexTooNew));

        vm.SelectCommand.Execute(vm.Tiles[0]);

        Assert.Equal(ShareRules.IndexTooNew, vm.StatusLine);
        Assert.True(vm.StatusIsWarning);
        Assert.True(vm.DownloadCommand.CanExecute(null));
        Assert.Equal("", vm.DownloadNote);
    }

    [Fact]
    public async Task Report_url_carries_only_the_entry_id()
    {
        var opener = new NoopUrlOpener();
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("blue-dragons")]), urls: opener);
        vm.SelectCommand.Execute(vm.Tiles[0]);

        vm.ReportCommand.Execute(null);

        var expected = $"https://github.com/{CommunityEndpoints.Default.GalleryRepo}/issues/new?template=report.yml&entry=7/blue-dragons";
        Assert.Equal(expected, opener.Last!.AbsoluteUri);
        Assert.DoesNotContain("alice", opener.Last.AbsoluteUri);
        Assert.DoesNotContain("Sample", opener.Last.AbsoluteUri);
    }

    [Fact]
    public void Report_url_refuses_an_id_that_is_not_digits_and_a_slug()
    {
        Assert.Null(CommunityViewModel.ReportUrl(CommunityFixtures.Entry("fine", ownerId: 0)));
        Assert.Null(CommunityViewModel.ReportUrl(CommunityFixtures.Entry("Not A Slug!")));
        Assert.Null(CommunityViewModel.ReportUrl(CommunityFixtures.Entry("a/b")));
        Assert.NotNull(CommunityViewModel.ReportUrl(CommunityFixtures.Entry("fine")));
    }

    [Fact]
    public async Task Status_line_counts_new_and_updated()
    {
        var root = Directory.CreateTempSubdirectory("nfty-seen-").FullName;
        var clock = CommunityFixtures.T0.AddDays(1);
        var seen = new CommunitySeen(root, () => clock);                        // first visit: Nov 2
        var oldE = E("old", "Old", created: CommunityFixtures.T0.AddDays(-30), updated: CommunityFixtures.T0.AddDays(-30));
        seen.See(oldE);                                                         // seen as of its own update
        var listing = CommunityFixtures.Listing([
            E("n1", "N1", created: CommunityFixtures.T0.AddDays(3), updated: CommunityFixtures.T0.AddDays(3)),
            E("n2", "N2", created: CommunityFixtures.T0.AddDays(4), updated: CommunityFixtures.T0.AddDays(4)),
            oldE with { Updated = CommunityFixtures.T0.AddDays(2) },
            E("quiet", "Quiet", created: CommunityFixtures.T0.AddDays(-9), updated: CommunityFixtures.T0.AddDays(-9))]);

        using var vm = await CommunityFixtures.Opened(listing, seen);

        Assert.Equal(2, vm.NewCount);
        Assert.Equal(1, vm.UpdatedCount);
        Assert.EndsWith(" · 2 new · 1 updated", vm.StatusLine);
        Assert.StartsWith("Updated ", vm.StatusLine);
        Assert.False(vm.StatusIsWarning);

        vm.MarkAllSeenCommand.Execute(null);
        Assert.Equal(0, vm.NewCount + vm.UpdatedCount);
        Assert.DoesNotContain("new", vm.StatusLine);
        Assert.All(vm.Tiles, t => Assert.Equal(SeenMark.None, t.Mark));
    }

    [Fact]
    public async Task Selecting_a_new_tile_clears_its_badge()
    {
        var root = Directory.CreateTempSubdirectory("nfty-seen-").FullName;
        var seen = new CommunitySeen(root, () => CommunityFixtures.T0);
        var listing = CommunityFixtures.Listing([
            E("n1", "N1", created: CommunityFixtures.T0.AddDays(1), updated: CommunityFixtures.T0.AddDays(2)),
            E("n2", "N2", created: CommunityFixtures.T0.AddDays(1), updated: CommunityFixtures.T0.AddDays(1))]);
        using var vm = await CommunityFixtures.Opened(listing, seen);
        var first = vm.Tiles.Single(t => t.Name == "N1");
        Assert.Equal(SeenMark.New, first.Mark);
        Assert.Equal(1.0, first.MarkOpacity);
        Assert.Equal(2, vm.NewCount);

        vm.SelectCommand.Execute(first);

        Assert.Equal(SeenMark.None, first.Mark);
        Assert.Equal(0.0, first.MarkOpacity);
        Assert.Equal(1, vm.NewCount);
        Assert.Equal(SeenMark.None, seen.MarkOf(first.Entry));      // recorded, so it survives a restart
        Assert.True(first.IsSelected);
    }

    [Fact]
    public async Task The_rail_dates_name_when_it_was_shared_and_when_it_was_updated()
    {
        var shared = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        var listing = CommunityFixtures.Listing([
            E("a", "A", created: shared, updated: new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero)),
            E("b", "B", created: shared, updated: shared.AddHours(2))]);
        using var vm = await CommunityFixtures.Opened(listing);

        vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Name == "A"));
        Assert.Equal("Shared 3 Nov 2026 · updated 9 Nov 2026", vm.DatesText);

        vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Name == "B"));
        Assert.Equal("Shared 3 Nov 2026", vm.DatesText);
    }

    [Fact]
    public async Task A_tile_says_assets_canvas_and_size()
    {
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("a", assets: 1234)]));

        var tile = vm.Tiles[0];

        Assert.Equal("1,234 · 16 x 16 · " + SizeText.Of(4_194_304), tile.FactsText);
        Assert.Equal("@alice", tile.OwnerText);
    }

    [Fact]
    public async Task Unshare_is_offered_only_for_your_own_entry()
    {
        var account = new FakeGitHubAccount { Login = "Alice" };
        var removed = new List<string>();
        using var vm = await CommunityFixtures.Opened(
            CommunityFixtures.Listing([E("mine", "Mine", "alice"), E("theirs", "Theirs", "bob")]), account: account);
        vm.UnshareHandler = e => { removed.Add(e.Slug); return Task.CompletedTask; };

        vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Name == "Theirs"));
        Assert.False(vm.CanUnshareSelected);
        Assert.False(vm.UnshareCommand.CanExecute(null));

        vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Name == "Mine"));
        Assert.True(vm.CanUnshareSelected);
        await vm.UnshareCommand.ExecuteAsync(null);
        Assert.Equal(["mine"], removed);
    }

    [Fact]
    public async Task Download_calls_the_handler_with_the_selected_entry()
    {
        var got = new List<string>();
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("a")]));
        vm.DownloadHandler = e => { got.Add(e.Id); return Task.CompletedTask; };

        Assert.False(vm.DownloadCommand.CanExecute(null));          // nothing selected
        vm.SelectCommand.Execute(vm.Tiles[0]);
        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(["7/a"], got);
    }

    [Fact]
    public async Task The_default_download_says_it_is_not_available_yet()
    {
        var status = new StatusService();
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing([E("a")]), status: status);
        vm.SelectCommand.Execute(vm.Tiles[0]);

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Contains("isn't available yet", status.Last);
    }

    [Fact]
    public async Task Paging_keeps_the_first_tile_when_the_page_size_changes()
    {
        var entries = Enumerable.Range(0, 20).Select(i => E($"e{i:00}", $"N{i:00}", updated: CommunityFixtures.T0.AddDays(-i)));
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing(entries), pageSize: 6);

        vm.NextPageCommand.Execute(null);
        Assert.Equal("N06", vm.Tiles[0].Name);
        Assert.Equal("7–12 of 20", vm.PageText);

        vm.PageSize = 4;                                  // first tile (index 6) now lives on page 1
        Assert.Equal(1, vm.PageIndex);
        Assert.Equal(4, vm.Tiles.Count);
        Assert.Equal(5, vm.PageCount);
        Assert.True(vm.PrevPageCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_resets_to_the_first_page()
    {
        var entries = Enumerable.Range(0, 20).Select(i => E($"e{i:00}", $"N{i:00}"));
        using var vm = await CommunityFixtures.Opened(CommunityFixtures.Listing(entries), pageSize: 5);
        vm.NextPageCommand.Execute(null);
        vm.NextPageCommand.Execute(null);

        vm.Query = "N1";

        Assert.Equal(0, vm.PageIndex);
        Assert.Equal(10, vm.MatchCount);
    }

    [Fact]
    public async Task A_refresh_that_fails_keeps_the_page_and_says_why()
    {
        var cache = new FakeCommunityCache { Next = CommunityFixtures.Listing([E("a")]) };
        using var vm = CommunityFixtures.Page(cache);
        await vm.PendingRefresh;
        Assert.Single(vm.Tiles);

        cache.Next = CommunityFixtures.Listing([E("a")], ShareRules.Offline(new DateTime(2026, 11, 9, 13, 0, 0)));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Single(vm.Tiles);
        Assert.True(vm.IsOffline);
    }
}
