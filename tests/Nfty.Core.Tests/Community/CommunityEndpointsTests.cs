using System.Text.RegularExpressions;
using Nfty.Core.Publish.Community;

namespace Nfty.Core.Tests.Community;

public class CommunityEndpointsTests
{
    [Fact]
    public void Default_endpoints_are_filled()
    {
        var e = CommunityEndpoints.Default;
        Assert.Matches(new Regex(@"^[\w.-]+/nfty-gallery$"), e.GalleryRepo);
        Assert.False(string.IsNullOrWhiteSpace(e.ClientId));
        Assert.Equal("https", e.IndexUrl.Scheme);
        string owner = e.GalleryRepo.Split('/')[0];
        Assert.Equal($"https://{owner}.github.io/nfty-gallery/index.json", e.IndexUrl.ToString());
    }
}
