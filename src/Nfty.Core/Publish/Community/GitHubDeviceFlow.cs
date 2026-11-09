using System.Globalization;
using System.Text.Json;

namespace Nfty.Core.Publish.Community;

/// <summary>One sign-in in progress: what to show the person and what to poll with.</summary>
/// <param name="Device">GitHub's device code (secret; never shown).</param>
/// <param name="UserCode">The short code the person types on GitHub.</param>
/// <param name="VerificationUri">Where the person types it.</param>
/// <param name="ExpiresIn">How long the code lives.</param>
/// <param name="Interval">How often GitHub allows a poll.</param>
public sealed record DeviceCode(string Device, string UserCode, Uri VerificationUri, TimeSpan ExpiresIn, TimeSpan Interval);

/// <summary>A GitHub sign-in that did not finish; the message is a <see cref="ShareRules"/> sentence, safe to show.</summary>
public sealed class GitHubAuthException(string message) : Exception(message);

/// <summary>
/// GitHub's OAuth device flow: no client secret, no browser callback. <see cref="StartAsync"/> asks for a code to show,
/// <see cref="WaitForTokenAsync"/> polls until the person approves it on GitHub.
/// </summary>
/// <param name="http">The client every request goes through.</param>
/// <param name="clientId">The OAuth app's client id (public).</param>
/// <param name="delay">Replaces <c>Task.Delay</c> between polls; tests inject one.</param>
public sealed class GitHubDeviceFlow(HttpClient http, string clientId, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly Uri CodeUrl = new("https://github.com/login/device/code");
    private static readonly Uri TokenUrl = new("https://github.com/login/oauth/access_token");
    private static readonly TimeSpan SlowDownStep = TimeSpan.FromSeconds(5);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    /// <summary>Asks GitHub for a device code (scope <c>public_repo</c>).</summary>
    /// <exception cref="GitHubAuthException">GitHub refused or answered in a way nfty doesn't understand.</exception>
    public async Task<DeviceCode> StartAsync(CancellationToken cancellationToken)
    {
        using var doc = await PostAsync(CodeUrl, [("client_id", clientId), ("scope", "public_repo")], cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        string? device = Str(root, "device_code"), user = Str(root, "user_code"), uri = Str(root, "verification_uri");
        if (device is null || user is null || uri is null
            || !Uri.TryCreate(uri, UriKind.Absolute, out var verification) || verification.Scheme != Uri.UriSchemeHttps
            || Seconds(root, "expires_in") is not { } expires || Seconds(root, "interval") is not { } interval)
            throw new GitHubAuthException(ShareRules.SignInFailed);
        return new DeviceCode(device, user, verification, expires, interval);
    }

    /// <summary>Polls until the person approves, returning the access token.</summary>
    /// <exception cref="GitHubAuthException">Canceled on GitHub, expired, or an unexpected answer.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task<string> WaitForTokenAsync(DeviceCode code, CancellationToken cancellationToken)
    {
        var interval = code.Interval;
        var waited = TimeSpan.Zero;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _delay(interval, cancellationToken).ConfigureAwait(false);
            waited += interval;
            if (waited > code.ExpiresIn) throw new GitHubAuthException(ShareRules.SignInExpired);

            using var doc = await PostAsync(TokenUrl,
                [("client_id", clientId), ("device_code", code.Device), ("grant_type", "urn:ietf:params:oauth:grant-type:device_code")],
                cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            if (Str(root, "access_token") is { Length: > 0 } token) return token;
            switch (Str(root, "error"))
            {
                case "authorization_pending": break;
                case "slow_down":
                    interval = Seconds(root, "interval") ?? interval + SlowDownStep;
                    break;
                case "expired_token": throw new GitHubAuthException(ShareRules.SignInExpired);
                case "access_denied": throw new GitHubAuthException(ShareRules.SignInCanceled);
                default: throw new GitHubAuthException(ShareRules.SignInFailed);
            }
        }
    }

    private async Task<JsonDocument> PostAsync(Uri url, (string Key, string Value)[] form, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.UserAgent.ParseAdd("nfty");
        try
        {
            using var response = await http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new GitHubAuthException(ShareRules.SignInFailed);
            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                throw new GitHubAuthException(ShareRules.SignInFailed);
            }
            return doc;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // The HttpClient's own timeout, not the person canceling.
            throw new GitHubAuthException(ShareRules.TimedOut);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new GitHubAuthException(ShareRules.SignInFailed);
        }
    }

    private static string? Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static TimeSpan? Seconds(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int s) && s > 0
            ? TimeSpan.FromSeconds(s) : null;
}
