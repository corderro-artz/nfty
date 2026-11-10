using Nfty.Core.Publish.Community;

namespace Nfty.App.Services;

/// <summary>
/// The GitHub account nfty shares through: connected once on the Settings card's PUBLISHING page, then
/// handed out as a ready <see cref="GitHubShareClient"/> to anything that shares or unshares.
/// </summary>
/// <remarks>
/// <b>The token lives in the OS credential store and nowhere else.</b> <c>settings.json</c> holds only the
/// login (key <c>githubLogin</c>), so the card can say who is connected without reading the secret; no
/// property, log line or exception message here carries the token.
/// </remarks>
public interface IGitHubAccount
{
    /// <summary>The connected account's login, or null when not connected.</summary>
    string? Login { get; }

    /// <summary>Whether an account is connected.</summary>
    bool IsConnected { get; }

    /// <summary>The connected account's GitHub id (what a share's entry id is made from), or null when none is connected or it
    /// has not been read yet; <see cref="EnsureUserIdAsync"/> reads it.</summary>
    long? UserId { get; }

    /// <summary>The account's id, asking GitHub once when it is not known (an account connected before the id was kept).</summary>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The id, or null when no account is connected.</returns>
    /// <exception cref="CommunityException">GitHub refused, in <see cref="ShareRules"/>' words.</exception>
    Task<long?> EnsureUserIdAsync(CancellationToken cancellationToken);

    /// <summary>Why connecting is impossible (no secure storage, or a build without an OAuth app); null when it is possible.</summary>
    string? UnavailableReason { get; }

    /// <summary>Asks GitHub for a sign-in code to show the person.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The code and where to type it.</returns>
    /// <exception cref="GitHubAuthException">Connecting is unavailable, or GitHub refused.</exception>
    Task<DeviceCode> BeginAsync(CancellationToken cancellationToken);

    /// <summary>Waits for the person to approve <paramref name="code"/> on GitHub, checks the token it yields, and keeps it.</summary>
    /// <param name="code">The code <see cref="BeginAsync"/> returned.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <exception cref="GitHubAuthException">The sign-in was canceled on GitHub, expired or failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    Task ConnectAsync(DeviceCode code, CancellationToken cancellationToken);

    /// <summary>Forgets the token and the login.</summary>
    /// <exception cref="InvalidOperationException">Secure storage failed.</exception>
    void Disconnect();

    /// <summary>A client for the connected account, or null when none is connected. Its HTTP client has no overall timeout
    /// (a 200 MB upload must outlast 100 seconds); the client's own body-stall timeouts still apply. Browsing never uses it.</summary>
    /// <returns>The client, or null.</returns>
    GitHubShareClient? Client();

    /// <summary>Raised after the connection changes.</summary>
    event EventHandler? Changed;
}

/// <inheritdoc cref="IGitHubAccount"/>
public sealed class GitHubAccountService : IGitHubAccount
{
    /// <summary>The credential-store name the token is kept under.</summary>
    public const string Target = "nfty/github";

    /// <summary>The <c>settings.json</c> key the login is cached under.</summary>
    public const string LoginKey = "githubLogin";

    /// <summary>The <c>settings.json</c> key the account's GitHub id is cached under.</summary>
    public const string UserIdKey = "githubUserId";

    private readonly IStateStore _store;
    private readonly ISecretStore _secrets;
    private readonly CommunityEndpoints _endpoints;
    private readonly Func<TimeSpan, CancellationToken, Task>? _delay;
    private readonly HttpClient _flowHttp;
    private readonly HttpClient _uploadHttp;

    /// <summary>Creates the account over its stores.</summary>
    /// <param name="store">Where the login is cached.</param>
    /// <param name="secrets">Where the token is kept.</param>
    /// <param name="endpoints">The gallery endpoints; <see cref="CommunityEndpoints.Default"/> when null.</param>
    /// <param name="handler">Replaces the network; tests pass a fake so nothing reaches GitHub.</param>
    /// <param name="delay">Replaces the wait between sign-in polls; tests pass an instant one.</param>
    public GitHubAccountService(IStateStore store, ISecretStore secrets, CommunityEndpoints? endpoints = null,
        HttpMessageHandler? handler = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(secrets);
        _store = store;
        _secrets = secrets;
        _endpoints = endpoints ?? CommunityEndpoints.Default;
        _delay = delay;
        handler ??= new HttpClientHandler();
        _flowHttp = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) };
        _uploadHttp = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

        // A login with no token behind it (the entry was removed outside nfty) is not a connection.
        var login = SettingsJson.String(SettingsJson.Read(store), LoginKey);
        if (login is { Length: > 0 } && _secrets.IsAvailable && TryReadToken() is not null)
        {
            Login = login;
            UserId = ReadUserId();
        }
    }

    private long? ReadUserId() =>
        SettingsJson.Read(_store).FirstOrDefault(p => string.Equals(p.Key, UserIdKey, StringComparison.OrdinalIgnoreCase)).Value
            is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out long id) && id > 0 ? id : null;

    /// <inheritdoc />
    public string? Login { get; private set; }

    /// <inheritdoc />
    public bool IsConnected => Login is not null;

    /// <inheritdoc />
    public long? UserId { get; private set; }

    /// <inheritdoc />
    public async Task<long?> EnsureUserIdAsync(CancellationToken cancellationToken)
    {
        if (!IsConnected) return null;
        if (UserId is { } known) return known;
        var client = Client() ?? throw new CommunityException(ShareRules.NotConnected);
        var user = await client.GetViewerAsync(cancellationToken).ConfigureAwait(false);
        SettingsJson.Set(_store, UserIdKey, System.Text.Json.Nodes.JsonValue.Create(user.Id));
        UserId = user.Id;
        return user.Id;
    }

    /// <inheritdoc />
    public string? UnavailableReason =>
        !_secrets.IsAvailable ? _secrets.UnavailableReason ?? "Secure storage is unavailable."
        : _endpoints.ClientId.Length == 0 ? ShareRules.NotSetUp
        : null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task<DeviceCode> BeginAsync(CancellationToken cancellationToken)
    {
        if (UnavailableReason is { } why) throw new GitHubAuthException(why);
        return new GitHubDeviceFlow(_flowHttp, _endpoints.ClientId, _delay).StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ConnectAsync(DeviceCode code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (UnavailableReason is { } why) throw new GitHubAuthException(why);
        string token = await new GitHubDeviceFlow(_flowHttp, _endpoints.ClientId, _delay)
            .WaitForTokenAsync(code, cancellationToken).ConfigureAwait(false);
        // Validate before keeping anything: a token GitHub will not use for this account is not stored.
        var user = await new GitHubShareClient(_flowHttp, token, _endpoints).GetViewerAsync(cancellationToken).ConfigureAwait(false);
        _secrets.Write(Target, token);
        SettingsJson.Set(_store, LoginKey, user.Login);
        SettingsJson.Set(_store, UserIdKey, System.Text.Json.Nodes.JsonValue.Create(user.Id));
        Login = user.Login;
        UserId = user.Id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        if (_secrets.IsAvailable) _secrets.Delete(Target);
        SettingsJson.Set(_store, LoginKey, null);
        SettingsJson.Set(_store, UserIdKey, null);
        Login = null;
        UserId = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public GitHubShareClient? Client() =>
        IsConnected && TryReadToken() is { } token ? new GitHubShareClient(_uploadHttp, token, _endpoints) : null;

    private string? TryReadToken()
    {
        try { return _secrets.Read(Target); }
        catch (InvalidOperationException) { return null; }
    }
}
