using System.Net;
using Nfty.Core.Publish.Community;

namespace Nfty.Core.Tests.Community;

public class GitHubDeviceFlowTests
{
    private const string CodePath = "/login/device/code";
    private const string TokenPath = "/login/oauth/access_token";

    private static string CodeJson(int interval = 5, int expires = 900) =>
        $"{{\"device_code\":\"dev123\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":{expires},\"interval\":{interval}}}";

    private static FakeGitHub Script() => new();

    private static (GitHubDeviceFlow Flow, List<TimeSpan> Waits) Flow(FakeGitHub fake)
    {
        var waits = new List<TimeSpan>();
        var flow = new GitHubDeviceFlow(fake.Client(), "client-xyz", (span, ct) => { waits.Add(span); return Task.CompletedTask; });
        return (flow, waits);
    }

    private static DeviceCode Code(int interval = 5, int expires = 900) => new("dev123", "ABCD-1234",
        new Uri("https://github.com/login/device"), TimeSpan.FromSeconds(expires), TimeSpan.FromSeconds(interval));

    [Fact]
    public async Task Start_asks_for_public_repo()
    {
        var fake = Script().Ok(HttpMethod.Post, CodePath, CodeJson());
        var (flow, _) = Flow(fake);

        var code = await flow.StartAsync(CancellationToken.None);

        var sent = Assert.Single(fake.Requests);
        Assert.Equal("https://github.com/login/device/code", sent.Url.ToString());
        Assert.Equal("client_id=client-xyz&scope=public_repo", sent.Body);
        Assert.Contains("application/json", sent.Headers["Accept"]);
        Assert.Contains("application/x-www-form-urlencoded", sent.Headers["Content-Type"]);
        Assert.Equal("dev123", code.Device);
        Assert.Equal("ABCD-1234", code.UserCode);
        Assert.Equal(new Uri("https://github.com/login/device"), code.VerificationUri);
        Assert.Equal(TimeSpan.FromSeconds(900), code.ExpiresIn);
        Assert.Equal(TimeSpan.FromSeconds(5), code.Interval);
    }

    [Fact]
    public async Task Pending_then_token_returns_the_token()
    {
        var fake = Script()
            .Ok(HttpMethod.Post, TokenPath, "{\"error\":\"authorization_pending\"}")
            .Ok(HttpMethod.Post, TokenPath, "{\"access_token\":\"gho_abc\",\"token_type\":\"bearer\",\"scope\":\"public_repo\"}");
        var (flow, waits) = Flow(fake);

        string token = await flow.WaitForTokenAsync(Code(), CancellationToken.None);

        Assert.Equal("gho_abc", token);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)], waits);
        var poll = fake.To(TokenPath)[0];
        Assert.Equal("https://github.com/login/oauth/access_token", poll.Url.ToString());
        Assert.Equal("client_id=client-xyz&device_code=dev123&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code", poll.Body);
        Assert.Contains("application/json", poll.Headers["Accept"]);
    }

    [Fact]
    public async Task Slow_down_adds_five_seconds()
    {
        var fake = Script()
            .Ok(HttpMethod.Post, TokenPath, "{\"error\":\"slow_down\"}")
            .Ok(HttpMethod.Post, TokenPath, "{\"access_token\":\"gho_abc\"}");
        var (flow, waits) = Flow(fake);

        await flow.WaitForTokenAsync(Code(5), CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], waits);
    }

    [Fact]
    public async Task Slow_down_honors_a_new_interval_from_github()
    {
        var fake = Script()
            .Ok(HttpMethod.Post, TokenPath, "{\"error\":\"slow_down\",\"interval\":20}")
            .Ok(HttpMethod.Post, TokenPath, "{\"access_token\":\"gho_abc\"}");
        var (flow, waits) = Flow(fake);

        await flow.WaitForTokenAsync(Code(5), CancellationToken.None);

        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)], waits);
    }

    [Fact]
    public async Task Denied_says_so()
    {
        var fake = Script().Ok(HttpMethod.Post, TokenPath, "{\"error\":\"access_denied\"}");
        var (flow, _) = Flow(fake);

        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => flow.WaitForTokenAsync(Code(), CancellationToken.None));

        Assert.Equal("GitHub sign-in was canceled.", ex.Message);
    }

    [Fact]
    public async Task Expired_says_so()
    {
        var fake = Script().Ok(HttpMethod.Post, TokenPath, "{\"error\":\"expired_token\"}");
        var (flow, _) = Flow(fake);

        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => flow.WaitForTokenAsync(Code(), CancellationToken.None));

        Assert.Equal("The sign-in code expired. Try again.", ex.Message);
    }

    [Fact]
    public async Task Waiting_past_the_lifetime_expires_without_an_answer()
    {
        var fake = Script();
        for (int i = 0; i < 10; i++) fake.Ok(HttpMethod.Post, TokenPath, "{\"error\":\"authorization_pending\"}");
        var (flow, waits) = Flow(fake);

        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => flow.WaitForTokenAsync(Code(5, 12), CancellationToken.None));

        Assert.Equal("The sign-in code expired. Try again.", ex.Message);
        Assert.Equal(3, waits.Count); // 5, 10, 15 > 12
        Assert.Equal(2, fake.To(TokenPath).Count);
    }

    [Fact]
    public async Task An_unexpected_error_answer_never_shows_githubs_text()
    {
        var fake = Script().Ok(HttpMethod.Post, TokenPath, "{\"error\":\"incorrect_client_credentials\",\"error_description\":\"secret detail\"}");
        var (flow, _) = Flow(fake);

        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => flow.WaitForTokenAsync(Code(), CancellationToken.None));

        Assert.Equal("GitHub sign-in failed. Try again.", ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public async Task A_failed_start_says_so()
    {
        var fake = Script().On(HttpMethod.Post, CodePath, HttpStatusCode.InternalServerError, "oops");
        var (flow, _) = Flow(fake);

        var ex = await Assert.ThrowsAsync<GitHubAuthException>(() => flow.StartAsync(CancellationToken.None));

        Assert.Equal("GitHub sign-in failed. Try again.", ex.Message);
    }

    [Fact]
    public async Task Cancellation_stops_polling()
    {
        var fake = Script().Ok(HttpMethod.Post, TokenPath, "{\"error\":\"authorization_pending\"}");
        using var cts = new CancellationTokenSource();
        var flow = new GitHubDeviceFlow(fake.Client(), "client-xyz", (span, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.WaitForTokenAsync(Code(), cts.Token));

        Assert.Empty(fake.Requests);
    }
}
