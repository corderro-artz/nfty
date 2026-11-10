using System.Net;
using System.Text;
using Nfty.App.Services;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>A GitHub that answers the sign-in and the account lookup from a script; nothing reaches the network.</summary>
internal sealed class FakeGitHubHandler : HttpMessageHandler
{
    public const string Token = "gho_secret-token-bytes-0123456789";

    public int UserStatus { get; set; } = 200;
    public string Login { get; set; } = "octo";
    public List<string> Urls { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.AbsoluteUri;
        Urls.Add(url);
        (HttpStatusCode, string) reply = url switch
        {
            "https://github.com/login/device/code" => (HttpStatusCode.OK,
                "{\"device_code\":\"dev123\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":900,\"interval\":5}"),
            "https://github.com/login/oauth/access_token" => (HttpStatusCode.OK, $"{{\"access_token\":\"{Token}\"}}"),
            "https://api.github.com/user" => ((HttpStatusCode)UserStatus, UserStatus == 200
                ? $"{{\"id\":7,\"login\":\"{Login}\",\"created_at\":\"2020-01-01T00:00:00Z\"}}" : "{\"message\":\"Bad credentials\"}"),
            _ => (HttpStatusCode.NotFound, "{}"),
        };
        return Task.FromResult(new HttpResponseMessage(reply.Item1)
        {
            Content = new StringContent(reply.Item2, Encoding.UTF8, "application/json"),
        });
    }
}

public class GitHubAccountTests
{
    private static readonly CommunityEndpoints Endpoints = CommunityEndpoints.Default with { ClientId = "client-xyz" };

    private static GitHubAccountService Account(IStateStore store, ISecretStore secrets, FakeGitHubHandler? handler = null,
        CommunityEndpoints? endpoints = null) =>
        new(store, secrets, endpoints ?? Endpoints, handler ?? new FakeGitHubHandler(), (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Connect_stores_the_token_in_the_secret_store_only()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-github-").FullName;
        var store = StateStore.At(dir);
        var secrets = new InMemorySecretStore();
        var account = Account(store, secrets);

        var code = await account.BeginAsync(CancellationToken.None);
        await account.ConnectAsync(code, CancellationToken.None);

        Assert.Equal("ABCD-1234", code.UserCode);
        Assert.True(account.IsConnected);
        Assert.Equal("octo", account.Login);
        Assert.Equal(FakeGitHubHandler.Token, secrets.Read(GitHubAccountService.Target));
        Assert.Equal("octo", SettingsJson.String(SettingsJson.Read(StateStore.At(dir)), GitHubAccountService.LoginKey));
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            Assert.DoesNotContain(FakeGitHubHandler.Token, File.ReadAllText(file), StringComparison.Ordinal);
        Assert.NotNull(account.Client());
    }

    [Fact]
    public async Task A_token_GitHub_will_not_use_is_not_kept()
    {
        var store = StateStore.InMemory();
        var secrets = new InMemorySecretStore();
        var account = Account(store, secrets, new FakeGitHubHandler { UserStatus = 401 });
        var code = await account.BeginAsync(CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(() => account.ConnectAsync(code, CancellationToken.None));

        Assert.Null(secrets.Read(GitHubAccountService.Target));
        Assert.False(account.IsConnected);
        Assert.Null(SettingsJson.String(SettingsJson.Read(store), GitHubAccountService.LoginKey));
        Assert.Null(account.Client());
    }

    [Fact]
    public async Task Disconnect_deletes_the_secret()
    {
        var store = StateStore.InMemory();
        var secrets = new InMemorySecretStore();
        var account = Account(store, secrets);
        await account.ConnectAsync(await account.BeginAsync(CancellationToken.None), CancellationToken.None);
        int changes = 0;
        account.Changed += (_, _) => changes++;

        account.Disconnect();

        Assert.Null(secrets.Read(GitHubAccountService.Target));
        Assert.False(account.IsConnected);
        Assert.Null(account.Login);
        Assert.Null(account.Client());
        Assert.Null(SettingsJson.String(SettingsJson.Read(store), GitHubAccountService.LoginKey));
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_login_survives_a_restart_only_while_its_token_does()
    {
        var store = StateStore.InMemory();
        var secrets = new InMemorySecretStore();
        var first = Account(store, secrets);
        await first.ConnectAsync(await first.BeginAsync(CancellationToken.None), CancellationToken.None);

        Assert.Equal("octo", Account(store, secrets).Login);

        secrets.Delete(GitHubAccountService.Target);   // removed outside nfty
        Assert.False(Account(store, secrets).IsConnected);
    }

    [Fact]
    public async Task Settings_json_keeps_the_other_keys_when_the_login_is_written()
    {
        var store = StateStore.InMemory();
        SettingsJson.Set(store, "theme", "dark");
        var account = Account(store, new InMemorySecretStore());

        await account.ConnectAsync(await account.BeginAsync(CancellationToken.None), CancellationToken.None);
        account.Disconnect();

        Assert.Equal("dark", SettingsJson.String(SettingsJson.Read(store), "theme"));
    }

    [Fact]
    public async Task Without_secure_storage_or_an_oauth_app_connecting_says_why_and_asks_nothing()
    {
        var handler = new FakeGitHubHandler();
        var noStore = Account(StateStore.InMemory(), new UnavailableSecretStore("Secure storage is unavailable: no keyring."), handler);
        Assert.Equal("Secure storage is unavailable: no keyring.", noStore.UnavailableReason);
        await Assert.ThrowsAsync<GitHubAuthException>(() => noStore.BeginAsync(CancellationToken.None));

        var noApp = Account(StateStore.InMemory(), new InMemorySecretStore(), handler, CommunityEndpoints.Default with { ClientId = "" });
        Assert.Equal(ShareRules.NotSetUp, noApp.UnavailableReason);
        await Assert.ThrowsAsync<GitHubAuthException>(() => noApp.BeginAsync(CancellationToken.None));

        Assert.Empty(handler.Urls);
    }
}
