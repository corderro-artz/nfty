using Nfty.Core.Publish.Ipfs;

namespace Nfty.App.Services;

/// <summary>Where an in-app upload goes.</summary>
public enum PinningProvider
{
    /// <summary>The local IPFS node (IPFS Desktop or Kubo). The default: no account, no key.</summary>
    LocalIpfs,

    /// <summary>Pinata, with the author's own API key.</summary>
    Pinata,
}

/// <summary>
/// The PUBLISHING page of the Settings card: which pinning service an upload uses, the local node's
/// address, and the Pinata key - which lives in the OS credential store, never in <c>settings.json</c>.
/// </summary>
public interface IPublishingSettings
{
    /// <summary>The chosen provider.</summary>
    PinningProvider Provider { get; }

    /// <summary>The local node's RPC address.</summary>
    string KuboApi { get; }

    /// <summary>Whether a Pinata key can be kept at all on this machine.</summary>
    bool SecureStorageAvailable { get; }

    /// <summary>Why it cannot, or null.</summary>
    string? SecureStorageProblem { get; }

    /// <summary>The saved key's last four characters behind a mask (<c>••••abcd</c>), or null when none
    /// is saved. Never more of it than that.</summary>
    string? SavedKeyHint { get; }

    /// <summary>The provider and its address as one line: <c>Local IPFS · http://127.0.0.1:5001</c>.</summary>
    string ProviderLabel { get; }

    /// <summary>Chooses the provider.</summary>
    /// <param name="provider">Which one.</param>
    void SetProvider(PinningProvider provider);

    /// <summary>Remembers the local node's address. Not validated here: the card shows
    /// <see cref="KuboPinning.ApiProblem"/> beside the field.</summary>
    /// <param name="api">The address as typed.</param>
    void SetKuboApi(string api);

    /// <summary>Saves the Pinata key in the OS credential store.</summary>
    /// <param name="jwt">The key.</param>
    /// <exception cref="InvalidOperationException">Secure storage is unavailable or failed.</exception>
    void SavePinataKey(string jwt);

    /// <summary>Forgets the saved Pinata key.</summary>
    /// <exception cref="InvalidOperationException">Secure storage is unavailable or failed.</exception>
    void ForgetPinataKey();

    /// <summary>The service the current choice names. Never throws: a choice that cannot work yet (no
    /// key, a bad address) yields a service whose status says so.</summary>
    /// <returns>The service.</returns>
    IPinningService CreateService();

    /// <summary>Raised after any choice changes.</summary>
    event Action? Changed;
}

/// <summary>A pinning service that cannot be used yet, saying why in its status.</summary>
/// <param name="provider">Which provider it stands in for.</param>
/// <param name="reason">Why it cannot be used.</param>
public sealed class UnconfiguredPinning(string provider, string reason) : IPinningService
{
    /// <inheritdoc />
    public string Provider { get; } = provider;

    /// <inheritdoc />
    public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PinningStatus(false, Provider, "not set up", reason));

    /// <inheritdoc />
    public Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromException<string>(new PinningException(reason));
}

/// <inheritdoc cref="IPublishingSettings"/>
public sealed class PublishingSettingsService : IPublishingSettings
{
    /// <summary>The credential-store name the Pinata key is kept under.</summary>
    public const string PinataTarget = "nfty/pinata";

    private static readonly Lazy<HttpClient> SharedHttp =
        new(() => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });

    private readonly IStateStore _store;
    private readonly ISecretStore _secrets;
    private readonly Func<PinningProvider, string, string?, IPinningService>? _factory;
    private string? _hint;
    private bool _hintRead;

    /// <summary>Creates the settings and loads the non-secret choices.</summary>
    /// <param name="store">Where the provider and address are kept.</param>
    /// <param name="secrets">Where the Pinata key is kept.</param>
    /// <param name="factory">Builds a service from (provider, address, key); tests pass a fake so
    /// nothing reaches a network. Null builds the real clients.</param>
    public PublishingSettingsService(IStateStore store, ISecretStore secrets,
        Func<PinningProvider, string, string?, IPinningService>? factory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(secrets);
        _store = store;
        _secrets = secrets;
        _factory = factory;
        var doc = SettingsJson.Read(store);
        Provider = Enum.TryParse<PinningProvider>(SettingsJson.String(doc, "pinningProvider"), true, out var p)
                   && Enum.IsDefined(p) ? p : PinningProvider.LocalIpfs;
        KuboApi = SettingsJson.String(doc, "kuboApi") is { Length: > 0 } api ? api : KuboPinning.DefaultApi;
    }

    /// <inheritdoc />
    public PinningProvider Provider { get; private set; }

    /// <inheritdoc />
    public string KuboApi { get; private set; }

    /// <inheritdoc />
    public bool SecureStorageAvailable => _secrets.IsAvailable;

    /// <inheritdoc />
    public string? SecureStorageProblem => _secrets.UnavailableReason;

    /// <inheritdoc />
    public string? SavedKeyHint
    {
        get
        {
            if (!_hintRead)
            {
                _hintRead = true;
                _hint = SecureStorageAvailable ? Hint(TryReadKey()) : null;
            }
            return _hint;
        }
    }

    /// <inheritdoc />
    public string ProviderLabel => Provider == PinningProvider.Pinata ? "Pinata" : $"Local IPFS · {KuboApi}";

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void SetProvider(PinningProvider provider)
    {
        if (provider == Provider) return;
        Provider = provider;
        SettingsJson.Set(_store, "pinningProvider", provider == PinningProvider.Pinata ? "pinata" : "localIpfs");
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void SetKuboApi(string api)
    {
        api = (api ?? "").Trim();
        if (api == KuboApi) return;
        KuboApi = api;
        SettingsJson.Set(_store, "kuboApi", api);
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void SavePinataKey(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) throw new InvalidOperationException("Paste the Pinata key (a JWT) first.");
        if (!SecureStorageAvailable) throw new InvalidOperationException(SecureStorageProblem);
        _secrets.Write(PinataTarget, jwt.Trim());
        _hint = Hint(jwt.Trim());
        _hintRead = true;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void ForgetPinataKey()
    {
        if (!SecureStorageAvailable) throw new InvalidOperationException(SecureStorageProblem);
        _secrets.Delete(PinataTarget);
        _hint = null;
        _hintRead = true;
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public IPinningService CreateService()
    {
        if (Provider == PinningProvider.LocalIpfs)
        {
            if (KuboPinning.ApiProblem(KuboApi) is { } problem) return new UnconfiguredPinning("Local IPFS", problem);
            return _factory?.Invoke(Provider, KuboApi, null) ?? new KuboPinning(SharedHttp.Value, KuboApi);
        }

        if (!SecureStorageAvailable)
            return new UnconfiguredPinning("Pinata", SecureStorageProblem ?? "Secure storage is unavailable.");
        string? key = TryReadKey();
        if (key is null)
            return new UnconfiguredPinning("Pinata", "No Pinata key is saved. Paste one in Settings, under Publishing.");
        return _factory?.Invoke(Provider, KuboApi, key) ?? new PinataPinning(SharedHttp.Value, key);
    }

    private string? TryReadKey()
    {
        try { return _secrets.Read(PinataTarget); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>The mask the card shows for a saved key: never more than its last four characters.</summary>
    /// <param name="key">The key, or null.</param>
    /// <returns><c>••••abcd</c>, or null.</returns>
    public static string? Hint(string? key) =>
        key is { Length: > 0 } k ? "••••" + (k.Length > 8 ? k[^4..] : "") : null;
}
