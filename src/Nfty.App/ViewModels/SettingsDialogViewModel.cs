using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core;
using Nfty.Core.Publish.Ipfs;

namespace Nfty.App.ViewModels;

/// <summary>Which page of the Settings card is showing.</summary>
public enum SettingsTab
{
    /// <summary>The theme, and the canvas a new CookBook or Ingredient starts at.</summary>
    General,

    /// <summary>Worker threads.</summary>
    Performance,

    /// <summary>Where an in-app upload goes.</summary>
    Publishing,

    /// <summary>What the app remembers about itself, and the folder it keeps it in.</summary>
    Data,
}

/// <summary>How the PUBLISHING page's connection chip reads.</summary>
public enum ConnectionState
{
    /// <summary>A check is under way.</summary>
    Checking,

    /// <summary>The service answered and accepts the credentials.</summary>
    Connected,

    /// <summary>It did not, or cannot be asked yet.</summary>
    NotReachable,

    /// <summary>A choice changed since the last check.</summary>
    Unchecked,
}

/// <summary>
/// The Settings card: four tabbed pages over the app's own preferences — GENERAL (theme, starting
/// canvas), PERFORMANCE (worker threads), PUBLISHING (where an upload goes, and whether nfty can talk
/// to it), DATA (what the app remembers, and where).
/// </summary>
/// <remarks>
/// <para>Every page is one tab of the export card's underline vocabulary, and all four share one
/// reserved height, so switching tabs moves nothing below them.</para>
/// <para><b>The connection check never blocks.</b> It runs on open, on a provider change and on
/// Test connection, each cancelling the one before; <see cref="PendingCheck"/> is the handle a test
/// awaits.</para>
/// <para><b>The Pinata key is write-only from here.</b> It goes from the password box straight to
/// the OS credential store and the box is cleared; the card only ever shows its last four
/// characters behind a mask.</para>
/// </remarks>
public partial class SettingsDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IParallelismSetting _setting;
    private readonly IAppSettings _general;
    private readonly IThemeService? _theme;
    private readonly IPublishingSettings _publishing;
    private readonly IRecentsService? _recents;
    private readonly IStateStore? _store;
    private readonly IFolderRevealer? _revealer;
    private readonly IViewerWindowState? _viewerState;
    private readonly IEditorViewState? _editorState;
    private bool _syncing;
    private CancellationTokenSource? _checkCts;

    /// <summary>Creates the card over the settings.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="setting">The worker-thread setting.</param>
    /// <param name="general">Theme and starting canvas; a session-only one when null.</param>
    /// <param name="theme">Applies a theme choice at once; null records it for the next launch only.</param>
    /// <param name="publishing">Where an upload goes; a session-only one that checks nothing when null.</param>
    /// <param name="recents">The Recent list DATA can clear; null dims that row with its reason.</param>
    /// <param name="store">The <c>.nfty</c> store DATA names and opens.</param>
    /// <param name="revealer">Opens the store's folder.</param>
    /// <param name="viewerState">The viewer windows' remembered sizes, which DATA can reset.</param>
    /// <param name="editorState">The editor's remembered view, which DATA can reset.</param>
    public SettingsDialogViewModel(IDialogService dialogs, IParallelismSetting setting,
        IAppSettings? general = null, IThemeService? theme = null, IPublishingSettings? publishing = null,
        IRecentsService? recents = null, IStateStore? store = null, IFolderRevealer? revealer = null,
        IViewerWindowState? viewerState = null, IEditorViewState? editorState = null)
    {
        _dialogs = dialogs;
        _setting = setting;
        _general = general ?? new AppSettingsService(StateStore.InMemory());
        _theme = theme;
        _publishing = publishing ?? new PublishingSettingsService(StateStore.InMemory(), new InMemorySecretStore(),
            (_, _, _) => new UnconfiguredPinning("Local IPFS", "Not checked in this session."));
        _recents = recents;
        _store = store;
        _revealer = revealer;
        _viewerState = viewerState;
        _editorState = editorState;

        _threads = setting.Current.Effective;
        _canvasSide = _general.DefaultCanvasSide;
        _kuboApi = _publishing.KuboApi;
        PendingCheck = CheckAsync();
    }

    // ================================================================ tabs

    /// <summary>Which page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab), nameof(IsPerformanceTab), nameof(IsPublishingTab), nameof(IsDataTab))]
    private SettingsTab _tab = SettingsTab.General;

    /// <summary>Whether GENERAL is showing.</summary>
    public bool IsGeneralTab => Tab == SettingsTab.General;
    /// <summary>Whether PERFORMANCE is showing.</summary>
    public bool IsPerformanceTab => Tab == SettingsTab.Performance;
    /// <summary>Whether PUBLISHING is showing.</summary>
    public bool IsPublishingTab => Tab == SettingsTab.Publishing;
    /// <summary>Whether DATA is showing.</summary>
    public bool IsDataTab => Tab == SettingsTab.Data;

    /// <summary>Shows a page.</summary>
    /// <param name="tab">Which one.</param>
    [RelayCommand] private void ShowTab(SettingsTab tab) => Tab = tab;

    [RelayCommand] private void Close()
    {
        _checkCts?.Cancel();
        _dialogs.Close(null);
    }

    // ================================================================ GENERAL

    /// <summary>Whether the app follows the operating system's theme.</summary>
    public bool IsSystemTheme => _general.Theme == ThemeChoice.System;
    /// <summary>Whether the app is always light.</summary>
    public bool IsLightTheme => _general.Theme == ThemeChoice.Light;
    /// <summary>Whether the app is always dark.</summary>
    public bool IsDarkTheme => _general.Theme == ThemeChoice.Dark;

    /// <summary>Chooses the theme, applies it now and remembers it.</summary>
    /// <param name="choice">Which one.</param>
    [RelayCommand]
    private void UseTheme(ThemeChoice choice)
    {
        _general.SetTheme(choice);
        _theme?.Apply(choice);
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    /// <summary>The side a new canvas starts at, as the stepper shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasNote), nameof(LargeCanvasOpacity))]
    private double _canvasSide;

    /// <summary>The largest starting side the stepper allows.</summary>
    public double CanvasMaximum => AppSettingsService.MaxCanvasSide;

    partial void OnCanvasSideChanged(double value)
    {
        var side = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        _general.SetDefaultCanvasSide(side);
    }

    /// <summary>What the starting canvas is for.</summary>
    public string CanvasNote =>
        $"New CookBooks and loose Ingredients start at {Side} × {Side}; the new-item form can still change it.";

    private int Side => _general.DefaultCanvasSide;

    /// <summary>Ink for the large-canvas warning: always laid out, so crossing the line moves nothing.</summary>
    public double LargeCanvasOpacity => CanvasAdvice.IsLarge(Side, Side) ? 1 : 0;

    /// <summary>The large-canvas warning, worded where every canvas form words it.</summary>
    public string LargeCanvasText => CanvasAdvice.LargeWarning;

    // ================================================================ PERFORMANCE

    /// <summary>The machine's logical processor count: the stepper's ceiling.</summary>
    public double Maximum => Parallelism.LogicalProcessors;

    /// <summary>True when <c>NFTY_THREADS</c> decides this session.</summary>
    public bool IsEnvLocked => _setting.EnvironmentText is not null;

    /// <summary>True when the choice is Auto.</summary>
    public bool IsAuto => !IsEnvLocked && _setting.Saved is null;

    /// <summary>True when the choice is a count (also while the environment fixes one).</summary>
    public bool IsCustom => !IsAuto;

    /// <summary>The tray is live unless the environment decides.</summary>
    public bool TrayEnabled => !IsEnvLocked;

    /// <summary>The stepper is live in Custom, and only there.</summary>
    public bool StepperEnabled => !IsEnvLocked && IsCustom;

    /// <summary>The count shown in the stepper.</summary>
    [ObservableProperty] private double _threads;

    /// <summary>The muted line under the row: what the current choice means.</summary>
    public string Note
    {
        get
        {
            var lp = Parallelism.LogicalProcessors;
            if (_setting.EnvironmentText is { } env)
                return $"Set by NFTY_THREADS={env} for this session.";
            if (IsAuto)
                return $"Auto uses {Parallelism.DefaultDegree} of {lp} logical processors.";
            if (_setting.Saved is { } saved && saved > lp)
                return $"Saved as {saved}; this machine has {lp} logical processors, so {lp} is used.";
            return "Results are identical at any thread count; fewer threads leave more of the machine free.";
        }
    }

    partial void OnThreadsChanged(double value)
    {
        if (_syncing || !StepperEnabled) return;
        var rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        _setting.Set(Math.Clamp(rounded, 1, Math.Max(1, Parallelism.LogicalProcessors)));
        RefreshThreads();
    }

    [RelayCommand]
    private void UseAuto()
    {
        if (IsEnvLocked) return;
        _setting.Set(null);
        RefreshThreads();
    }

    [RelayCommand]
    private void UseCustom()
    {
        if (IsEnvLocked || IsCustom) return;
        _setting.Set(Parallelism.DefaultDegree);
        RefreshThreads();
    }

    private void RefreshThreads()
    {
        _syncing = true;
        try { Threads = _setting.Current.Effective; }
        finally { _syncing = false; }
        OnPropertyChanged(nameof(IsAuto));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(StepperEnabled));
        OnPropertyChanged(nameof(Note));
    }

    // ================================================================ PUBLISHING

    /// <summary>Whether uploads go to the local node.</summary>
    public bool IsLocalIpfs => _publishing.Provider == PinningProvider.LocalIpfs;
    /// <summary>Whether uploads go to Pinata.</summary>
    public bool IsPinata => _publishing.Provider == PinningProvider.Pinata;

    /// <summary>Chooses the provider and checks it.</summary>
    /// <param name="provider">Which one.</param>
    [RelayCommand]
    private void UseProvider(PinningProvider provider)
    {
        if (provider == _publishing.Provider) return;
        _publishing.SetProvider(provider);
        OnPropertyChanged(nameof(IsLocalIpfs));
        OnPropertyChanged(nameof(IsPinata));
        OnPropertyChanged(nameof(KeyLine));
        OnPropertyChanged(nameof(ApiProblem));
        OnPropertyChanged(nameof(KeyPlaceholder));
        PendingCheck = CheckAsync();
    }

    /// <summary>The local node's RPC address, as typed. Saved when valid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApiProblem))]
    private string _kuboApi;

    /// <summary>Why the address cannot be used; empty when it can, or when Pinata is chosen.</summary>
    public string ApiProblem => IsLocalIpfs ? KuboPinning.ApiProblem(KuboApi) ?? "" : "";

    partial void OnKuboApiChanged(string value)
    {
        if (KuboPinning.ApiProblem(value) is not null) return;
        if (value.Trim() == _publishing.KuboApi) return;
        _publishing.SetKuboApi(value);
        if (IsLocalIpfs) SetStatus(ConnectionState.Unchecked, "not checked", "The address changed. Test the connection.");
    }

    /// <summary>The Pinata key as it is being pasted. Cleared the moment it is saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveKeyCommand))]
    private string _pinataKeyInput = "";

    /// <summary>The key box's placeholder: only while the box is live. A disabled box is dimmed to
    /// 0.38 by the app, and placeholder ink is already muted - the two together are unreadable, so the
    /// dim box shows nothing rather than a hint nobody can read.</summary>
    public string KeyPlaceholder => IsPinata ? "paste the API key's JWT" : "";

    /// <summary>Whether a key can be kept on this machine at all.</summary>
    public bool CanStoreKey => _publishing.SecureStorageAvailable;

    /// <summary>The line under the key box: what is saved (masked), or why nothing can be.</summary>
    public string KeyLine => !CanStoreKey
        ? _publishing.SecureStorageProblem ?? "Secure storage is unavailable."
        : _publishing.SavedKeyHint is { } hint
            ? $"saved · {hint} · kept in the system credential store"
            : "no key saved · it is kept in the system credential store, never in a file";

    /// <summary>Whether Forget has anything to forget.</summary>
    public bool HasSavedKey => CanStoreKey && _publishing.SavedKeyHint is not null;

    private bool CanSaveKey() => CanStoreKey && PinataKeyInput.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSaveKey))]
    private async Task SaveKeyAsync()
    {
        try
        {
            _publishing.SavePinataKey(PinataKeyInput);
        }
        catch (InvalidOperationException ex)
        {
            await _dialogs.ShowErrorAsync("Could not save the key", ErrorText.For(ex));
            return;
        }
        finally
        {
            PinataKeyInput = "";
        }
        KeyChanged();
    }

    private bool CanForgetKey() => HasSavedKey;

    [RelayCommand(CanExecute = nameof(CanForgetKey))]
    private async Task ForgetKeyAsync()
    {
        try { _publishing.ForgetPinataKey(); }
        catch (InvalidOperationException ex)
        {
            await _dialogs.ShowErrorAsync("Could not forget the key", ErrorText.For(ex));
            return;
        }
        KeyChanged();
    }

    private void KeyChanged()
    {
        OnPropertyChanged(nameof(KeyLine));
        OnPropertyChanged(nameof(HasSavedKey));
        ForgetKeyCommand.NotifyCanExecuteChanged();
        if (IsPinata) PendingCheck = CheckAsync();
    }

    /// <summary>The chip's state.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsChecking))]
    private ConnectionState _connection = ConnectionState.Checking;

    /// <summary>Whether the chip's dot is the success ink.</summary>
    public bool IsConnected => Connection == ConnectionState.Connected;

    /// <summary>Whether the chip's dot is the muted ink: a check under way, or one owed.</summary>
    public bool IsChecking => Connection is ConnectionState.Checking or ConnectionState.Unchecked;

    /// <summary>A few words on the chip: <c>connected · kubo 0.29.0</c>, <c>not reachable</c>.</summary>
    [ObservableProperty] private string _statusSummary = "checking…";

    /// <summary>The sentence under the chip.</summary>
    [ObservableProperty] private string _statusDetail = "";

    /// <summary>The check in flight, for a test to await. Never faults.</summary>
    public Task PendingCheck { get; private set; }

    /// <summary>Checks the chosen service again.</summary>
    [RelayCommand]
    private Task TestConnection() => PendingCheck = CheckAsync();

    private async Task CheckAsync()
    {
        _checkCts?.Cancel();
        var cts = _checkCts = new CancellationTokenSource();
        SetStatus(ConnectionState.Checking, "checking…", $"Asking {(IsPinata ? "Pinata" : _publishing.KuboApi)}…");
        try
        {
            var status = await _publishing.CreateService().StatusAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            SetStatus(status.Reachable ? ConnectionState.Connected : ConnectionState.NotReachable,
                status.Summary, status.Detail);
        }
        catch (OperationCanceledException) { /* superseded by a newer check */ }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
                SetStatus(ConnectionState.NotReachable, "not reachable", ErrorText.For(ex));
        }
    }

    private void SetStatus(ConnectionState state, string summary, string detail)
    {
        Connection = state;
        StatusSummary = summary;
        StatusDetail = detail;
    }

    // ================================================================ DATA

    /// <summary>Where the app keeps what it remembers, as a sentence.</summary>
    public string StoreText => _store?.Resolution.Description ?? "This session keeps nothing on disk.";

    /// <summary>How many files Recent remembers.</summary>
    public string RecentsText => _recents is null ? "not available here"
        : _recents.Items.Count switch { 0 => "none remembered", 1 => "1 remembered", var n => $"{n} remembered" };

    /// <summary>What the last DATA action did, or empty.</summary>
    [ObservableProperty] private string _dataNote = "";

    private bool CanClearRecents() => _recents is { Items.Count: > 0 };

    [RelayCommand(CanExecute = nameof(CanClearRecents))]
    private void ClearRecents()
    {
        _recents?.Clear();
        OnPropertyChanged(nameof(RecentsText));
        ClearRecentsCommand.NotifyCanExecuteChanged();
        DataNote = "Recent files cleared. The files themselves are untouched.";
    }

    private bool CanResetViews() => _viewerState is not null || _editorState is not null;

    [RelayCommand(CanExecute = nameof(CanResetViews))]
    private void ResetViews()
    {
        if (_viewerState is { } v)
            v.Save(v.Current with { TwoD = ViewerWindowState.DefaultTwoD, ThreeD = ViewerWindowState.DefaultThreeD });
        _editorState?.Save(new EditorViewState());
        DataNote = "Viewer window sizes and the editor's grid and rail are back to their defaults.";
    }

    private bool CanOpenDataFolder() => _revealer is not null && _store?.Resolution.Directory is not null;

    [RelayCommand(CanExecute = nameof(CanOpenDataFolder))]
    private void OpenDataFolder()
    {
        if (_store?.Resolution.Directory is { } dir) _revealer?.Reveal(dir);
    }
}
