using System.Text;
using Nfty.Core.Formats;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class CommunityTagsTests
{
    private static CommunityTags Read(string json) => CommunityTags.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void A_good_file_reads_in_order()
    {
        var t = Read("""{"schemaVersion":1,"tags":["props","pixel-art","a1"]}""");
        Assert.Equal(["props", "pixel-art", "a1"], t.Tags);
    }

    [Fact]
    public void Tags_file_newer_is_refused_by_name()
    {
        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => Read("""{"schemaVersion":2,"tags":["x"]}"""));
        Assert.Equal(ShareRules.IndexTooNew, ex.Message);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"tags":["Props"]}""")]
    [InlineData("""{"schemaVersion":1,"tags":["-x"]}""")]
    [InlineData("""{"schemaVersion":1,"tags":["has space"]}""")]
    [InlineData("""{"schemaVersion":1,"tags":["aaaaaaaaaaaaaaaaaaaaaaaaa"]}""")]
    [InlineData("""{"schemaVersion":1,"tags":["a","a"]}""")]
    [InlineData("""{"schemaVersion":1,"tags":[null]}""")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("not json")]
    public void Tags_file_with_a_bad_tag_is_damaged(string json)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Read(json));
        Assert.Equal(ShareRules.ListDamaged, ex.Message);
    }

    [Fact]
    public void More_than_thirty_two_tags_is_damaged()
    {
        var many = string.Join(",", Enumerable.Range(0, 33).Select(i => $"\"t{i}\""));
        Assert.Throws<InvalidDataException>(() => Read($$"""{"schemaVersion":1,"tags":[{{many}}]}"""));
    }
}
