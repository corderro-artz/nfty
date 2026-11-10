using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class CommunitySearchTests
{
    private static CommunityEntry E(string name, string login, string collection, params string[] tags) =>
        new(1, name.ToLowerInvariant(), new CommunityOwner(7, login), "nfty-public", "v1", name, collection, "", tags,
            new CommunityCanvas(8, 8), 4, false, false, null,
            new CommunityFile("https://example.test/s.set", 10, new string('a', 64)),
            new CommunityFile("https://example.test/p.png", 3, new string('b', 64)),
            "fp", "1.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Hash_token_is_a_tag()
    {
        var q = CommunitySearch.Parse("#Pets # #props");
        Assert.Equal(["pets", "props"], q.Tags);
        Assert.Empty(q.Owners);
        Assert.Empty(q.Words);
        Assert.True(CommunitySearch.Matches(E("A", "x", "c", "pets", "props"), q));
        Assert.False(CommunitySearch.Matches(E("A", "x", "c", "pets"), q));
    }

    [Fact]
    public void At_token_is_an_owner()
    {
        var q = CommunitySearch.Parse("@Alice @");
        Assert.Equal(["Alice"], q.Owners);
        Assert.True(CommunitySearch.Matches(E("A", "alice", "c"), q));
        Assert.False(CommunitySearch.Matches(E("A", "alicia", "c"), q));   // equals, not contains
    }

    [Fact]
    public void Plain_words_match_name_or_collection()
    {
        var q = CommunitySearch.Parse("drag");
        Assert.True(CommunitySearch.Matches(E("Dragons", "x", "Pets"), q));
        Assert.True(CommunitySearch.Matches(E("Plants", "x", "Dragonfruit"), q));
        Assert.False(CommunitySearch.Matches(E("Robots", "drag", "Machines"), q));   // an owner is not a word
    }

    [Fact]
    public void Terms_combine_with_and()
    {
        var q = CommunitySearch.Parse("#pets @alice dra");
        Assert.True(CommunitySearch.Matches(E("Dragons", "alice", "c", "pets"), q));
        Assert.False(CommunitySearch.Matches(E("Dragons", "bob", "c", "pets"), q));
        Assert.False(CommunitySearch.Matches(E("Dragons", "alice", "c", "props"), q));
        Assert.False(CommunitySearch.Matches(E("Robots", "alice", "c", "pets"), q));
        Assert.True(CommunitySearch.Matches(E("Anything", "x", "c"), CommunitySearch.Parse("  ")));
        Assert.True(CommunitySearch.Parse(null).IsEmpty);
    }

    [Fact]
    public void Toggle_adds_then_removes_a_token()
    {
        var on = CommunitySearch.Toggle("dragons", "#pets");
        Assert.Equal("dragons #pets", on);
        Assert.True(CommunitySearch.Has(on, "#PETS"));
        Assert.Equal("dragons", CommunitySearch.Toggle("  dragons   #pets ", "#pets"));
        Assert.Equal("#pets", CommunitySearch.Toggle(null, "#pets"));
        Assert.Equal("", CommunitySearch.Toggle("#pets", "#pets"));
    }
}
