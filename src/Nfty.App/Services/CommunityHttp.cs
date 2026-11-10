namespace Nfty.App.Services;

/// <summary>
/// The one HTTP client community downloads share. It carries no credentials, and no overall timeout:
/// <c>CommunityDownload</c> gives up on its own when the body stops arriving, which a fixed timeout
/// would instead apply to a large Set that is simply still coming.
/// </summary>
public sealed class CommunityHttp : IDisposable
{
    /// <summary>The client.</summary>
    public HttpClient Client { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Frees the client.</summary>
    public void Dispose() => Client.Dispose();
}
