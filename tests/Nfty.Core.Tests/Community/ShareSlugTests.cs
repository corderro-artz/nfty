using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class ShareSlugTests
{
    [Fact] public void Slug_lowers_and_dashes() => Assert.Equal("vapor-chests", ShareSlug.Of("  Vapor  Chests!! "));
    [Fact] public void Slug_is_at_most_forty() => Assert.Equal(40, ShareSlug.Of(new string('a', 90)).Length);
    [Fact] public void Slug_never_ends_in_a_dash_after_trim() => Assert.Equal("ab", ShareSlug.Of("ab" + new string('-', 60) + "c")[..2]);

    [Fact]
    public void A_cut_that_lands_on_a_dash_is_trimmed()
    {
        var s = ShareSlug.Of(new string('a', 39) + " b");
        Assert.Equal(new string('a', 39), s);
        Assert.True(ShareSlug.IsValid(s));
    }

    [Fact]
    public void A_name_without_ascii_letters_gets_a_hash_slug()
    {
        var s = ShareSlug.Of("ドラゴン");
        Assert.Matches("^set-[0-9a-f]{8}$", s);
        Assert.Equal(s, ShareSlug.Of("ドラゴン"));
        Assert.NotEqual(s, ShareSlug.Of("!!!"));
        Assert.True(ShareSlug.IsValid(s));
    }

    [Fact]
    public void Valid_slug_rule()
    {
        Assert.True(ShareSlug.IsValid("a-1"));
        Assert.False(ShareSlug.IsValid("A"));
        Assert.False(ShareSlug.IsValid("-a"));
        Assert.False(ShareSlug.IsValid("a-"));
        Assert.False(ShareSlug.IsValid(""));
        Assert.False(ShareSlug.IsValid(new string('a', 41)));
        Assert.True(ShareSlug.IsValid(new string('a', 40)));
    }
}
