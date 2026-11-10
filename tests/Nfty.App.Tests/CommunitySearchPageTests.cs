using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Community page's one search box: tray chips, tile and rail links all edit its text, and the text alone
/// decides what the grid shows.</summary>
public class CommunitySearchPageTests
{
    private static CommunityEntry E(string slug, string name, string login, string collection, params string[] tags) =>
        CommunityFixtures.Entry(slug, name, login, tags: tags, collection: collection);

    private static readonly string[] Gallery = ["characters", "creatures", "props", "items", "scenery", "effects", "ui", "abstract"];

    private static async Task<CommunityViewModel> Open(IEnumerable<CommunityEntry> entries, string[]? tags = null)
    {
        var cache = new FakeCommunityCache { Next = CommunityFixtures.Listing(entries), Tags = tags ?? Gallery };
        var vm = CommunityFixtures.Page(cache);
        await vm.PendingRefresh;
        return vm;
    }

    private static string[] Names(CommunityViewModel vm) => vm.Tiles.Select(t => t.Name).Order().ToArray();

    [Fact]
    public async Task The_tray_is_the_galleries_fixed_list_with_the_overflow_in_the_dropdown()
    {
        using var vm = await Open([E("a", "A", "alice", "c", "props")]);

        Assert.Equal(["characters", "creatures", "props", "items"], vm.TrayTags.Select(t => t.Label));
        Assert.Equal(["scenery", "effects", "ui", "abstract"], vm.MoreTags.Select(t => t.Label));
        Assert.True(vm.HasMoreTags);
        Assert.True(vm.HasTags);

        using var few = await Open([E("a", "A", "alice", "c")], ["props", "items"]);
        Assert.Equal(["props", "items"], few.TrayTags.Select(t => t.Label));
        Assert.False(few.HasMoreTags);

        using var none = await Open([E("a", "A", "alice", "c")], []);
        Assert.False(none.HasTags);
    }

    [Fact]
    public async Task Tray_chip_writes_hash_tag_into_search()
    {
        using var vm = await Open([
            E("a", "Cat", "alice", "c", "creatures"),
            E("b", "Chest", "alice", "c", "props"),
            E("c", "Gem", "bob", "c", "items")]);
        vm.Query = "c";

        var props = vm.TrayTags.Single(t => t.Label == "props");
        props.ToggleCommand.Execute(null);
        Assert.Equal("c #props", vm.Query);
        Assert.True(props.IsOn);
        Assert.Equal(["Chest"], Names(vm));

        props.ToggleCommand.Execute(null);
        Assert.Equal("c", vm.Query);
        Assert.False(props.IsOn);
        Assert.Equal(["Cat", "Chest", "Gem"], Names(vm));

        // a tag typed by hand lights its chip, including one in the dropdown, and lights the dropdown button
        vm.Query = "#SCENERY";
        Assert.True(vm.MoreTags.Single(t => t.Label == "scenery").IsOn);
        Assert.True(vm.MoreIsOn);
        vm.Query = "";
        Assert.False(vm.MoreIsOn);

        vm.MoreTags[0].ToggleCommand.Execute(null);
        Assert.Equal("#scenery", vm.Query);
    }

    [Fact]
    public async Task Owner_link_on_a_tile_writes_at_login()
    {
        using var vm = await Open([
            E("a", "Cat", "alice", "c", "creatures"),
            E("b", "Chest", "bob", "c", "props")]);

        var tile = vm.Tiles.Single(t => t.Name == "Cat");
        Assert.Equal("@alice", tile.OwnerLink.Label);
        tile.OwnerLink.ToggleCommand.Execute(null);
        Assert.Equal("@alice", vm.Query);
        Assert.True(tile.OwnerLink.IsOn);
        Assert.Equal(["Cat"], Names(vm));

        tile.TagLinks.Single().ToggleCommand.Execute(null);
        Assert.Equal("@alice #creatures", vm.Query);

        tile.OwnerLink.ToggleCommand.Execute(null);
        Assert.Equal("#creatures", vm.Query);
        Assert.False(tile.OwnerLink.IsOn);

        // the rail shows the same links for the selected collection
        vm.SelectCommand.Execute(tile);
        Assert.Same(tile.OwnerLink, vm.Selected!.OwnerLink);
        Assert.Equal(["#creatures"], vm.Selected.TagLinks.Select(l => l.Label));
    }

    [Fact]
    public async Task A_tile_line_carries_only_tags_that_fit_whole()
    {
        using var vm = await Open([
            E("a", "A", "alice", "c", "props", "items", "scenery"),          // @alice #props #items fits; #scenery does not
            E("b", "B", "alice", "c", "creatures", "characters")]);           // only the first fits

        Assert.Equal(["#props", "#items"], vm.Tiles.Single(t => t.Name == "A").TileTagLinks.Select(l => l.Label));
        Assert.Equal(["#creatures"], vm.Tiles.Single(t => t.Name == "B").TileTagLinks.Select(l => l.Label));
        Assert.Equal(3, vm.Tiles.Single(t => t.Name == "A").TagLinks.Count);   // the rail still has every one
    }

    [Fact]
    public async Task Search_filters_by_all_three_kinds()
    {
        using var vm = await Open([
            E("a", "Dragons", "alice", "Pets", "creatures"),
            E("b", "Dragon Eggs", "alice", "Pets", "items"),
            E("c", "Dragons", "bob", "Pets", "creatures"),
            E("d", "Robots", "alice", "Machines", "creatures")]);

        vm.Query = "#creatures";
        Assert.Equal(["Dragons", "Dragons", "Robots"], Names(vm));
        vm.Query = "#creatures @alice";
        Assert.Equal(["Dragons", "Robots"], Names(vm));
        vm.Query = "#creatures @alice dragon";
        Assert.Equal(["Dragons"], Names(vm));
        vm.Query = "dragon eggs";
        Assert.Equal(["Dragon Eggs"], Names(vm));
        vm.Query = "machines";                               // the collection name counts as a word
        Assert.Equal(["Robots"], Names(vm));
        vm.Query = "#creatures @carol";
        Assert.Empty(vm.Tiles);
        Assert.Equal(CommunityEmptyKind.NoMatches, vm.EmptyKind);
        Assert.Contains("#creatures @carol", vm.EmptyText);
        Assert.Equal("Clear search", vm.EmptyActionText);

        vm.EmptyActionCommand.Execute(null);
        Assert.Equal("", vm.Query);
        Assert.Equal(4, vm.MatchCount);
    }

    [Fact]
    public void Share_card_offers_the_gallery_tags()
    {
        var cache = new FakeCommunityCache { Tags = ["props", "ui"] };
        using var provider = new ServiceCollection().AddNftyApp()
            .AddSingleton<IStateStore>(StateStore.InMemory())
            .AddSingleton<ISecretStore>(new InMemorySecretStore())
            .AddSingleton<ICommunityCache>(cache)
            .BuildServiceProvider();
        var source = ShareFixtures.Source(out var dir);
        try
        {
            using var card = provider.GetRequiredService<Func<ShareSource, ShareDialogViewModel>>()(source);
            Assert.Equal(["props", "ui"], card.TagChips.Select(c => c.Name));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
