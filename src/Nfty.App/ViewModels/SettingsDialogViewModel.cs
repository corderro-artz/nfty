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

    /// <summary>Which files Windows opens with nfty. Shown on Windows only.</summary>
    System,
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
    private readonly IFileAssociations? _associations;
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
    /// <param name="associations">The machine's file associations; SYSTEM is shown only when these
    /// are supported (Windows), and is absent otherwise.</param>
    public SettingsDialogViewModel(IDialogService dialogs, IParallelismSetting setting,
        IAppSettings? general = null, IThemeService? theme = null, IPublishingSettings? publishing = null,
        IRecentsService? recents = null, IStateStore? store = null, IFolderRevealer? revealer = null,
        IViewerWindowState? viewerState = null, IEditorViewState? editorState = null,
        IFileAssociations? associations = null)
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
        _associations = associations is { IsSupported: true } ? associations : null;

        _threads = setting.Current.Effective;
        _canvasSide = _general.DefaultCanvasSide;
        _kuboApi = _publishing.KuboApi;
        PendingCheck = CheckAsync();
        if (_associations is not null) ReadAssociations(adoptChoice: true);
    }

    // ================================================================ tabs

    /// <summary>Which page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab), nameof(IsPerformanceTab), nameof(IsPublishingTab), nameof(IsDataTab), nameof(IsSystemTab))]
    private SettingsTab _tab = SettingsTab.General;

    /// <summary>Whether GENERAL is showing.</summary>
    public bool IsGeneralTab => Tab == SettingsTab.General;
    /// <summary>Whether PERFORMANCE is showing.</summary>
    public bool IsPerformanceTab => Tab == SettingsTab.Performance;
    /// <summary>Whether PUBLISHING is showing.</summary>
    public bool IsPublishingTab => Tab == SettingsTab.Publishing;
    /// <summary>Whether DATA is showing.</summary>
    public bool IsDataTab => Tab == SettingsTab.Data;
    /// <summary>Whether SYSTEM is showing.</summary>
    public bool IsSystemTab => Tab == SettingsTab.System;

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

    // ================================================================ SYSTEM

    /// <summary>Whether this machine has file associations nfty can write: the SYSTEM tab exists
    /// only then. Fixed for the platform, so the tab row never changes while the card is open.</summary>
    public bool ShowsSystemTab => _associations is not null;

    private AssociationStatus? _registered;

    /// <summary>What nfty's own archives open in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNftyOff), nameof(IsNftyApp), nameof(IsNftyViewer))]
    private AssociationTarget _nftyTarget = AssociationTarget.Off;

    /// <summary>What pictures and 3D models open in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPicturesOff), nameof(IsPicturesViewer))]
    private AssociationTarget _picturesTarget = AssociationTarget.Off;

    /// <summary>What asset metadata JSON opens in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMetadataOff), nameof(IsMetadataViewer))]
    private AssociationTarget _metadataTarget = AssociationTarget.Off;

    /// <summary>nfty's archives: not registered.</summary>
    public bool IsNftyOff => NftyTarget == AssociationTarget.Off;
    /// <summary>nfty's archives: the app.</summary>
    public bool IsNftyApp => NftyTarget == AssociationTarget.App;
    /// <summary>nfty's archives: the viewer.</summary>
    public bool IsNftyViewer => NftyTarget == AssociationTarget.Viewer;
    /// <summary>Pictures and models: not registered.</summary>
    public bool IsPicturesOff => PicturesTarget == AssociationTarget.Off;
    /// <summary>Pictures and models: the viewer.</summary>
    public bool IsPicturesViewer => PicturesTarget == AssociationTarget.Viewer;
    /// <summary>Metadata: not registered.</summary>
    public bool IsMetadataOff => MetadataTarget == AssociationTarget.Off;
    /// <summary>Metadata: the viewer.</summary>
    public bool IsMetadataViewer => MetadataTarget == AssociationTarget.Viewer;

    /// <summary>nfty's archive extensions, as one line.</summary>
    public string NftyExtensions => FileTypeCatalog.ExtensionsOf(AssociationGroup.NftyFiles);
    /// <summary>The picture and model extensions, as one line.</summary>
    public string PictureExtensions => FileTypeCatalog.ExtensionsOf(AssociationGroup.PicturesAndModels);
    /// <summary>The metadata extension, as a line.</summary>
    public string MetadataExtensions => FileTypeCatalog.ExtensionsOf(AssociationGroup.Metadata) + " · a Set's asset files";

    /// <summary>nfty's archive types by name, for the row's tooltip.</summary>
    public string NftyTypesTip => TypesTip(AssociationGroup.NftyFiles)
        + " A Menu opens in the viewer either way: the app does not edit one.";
    /// <summary>The picture and model types by name.</summary>
    public string PictureTypesTip => TypesTip(AssociationGroup.PicturesAndModels);
    /// <summary>What registering .json means.</summary>
    public string MetadataTypesTip =>
        "nfty is offered for every .json file, and shows the ones that are an asset's metadata (nfty/NNNN.json, metadata/NNNN.json); any other JSON file says so.";

    private static string TypesTip(AssociationGroup group) =>
        string.Join(", ", FileTypeCatalog.Of(group).Select(t => $"{t.Extension} {t.FriendlyName}")) + ".";

    /// <summary>The choice the trays show.</summary>
    public AssociationChoice Choice => new(NftyTarget, PicturesTarget, MetadataTarget);

    /// <summary>Whether the trays differ from a complete registration. A broken one keeps saying
    /// "needs repair" whatever the trays show: Repair writes what they show.</summary>
    public bool HasPendingAssociationChange =>
        _registered is { State: AssociationState.Registered } r && Choice != r.Current;

    /// <summary>The chip's words.</summary>
    public string AssociationSummary => _registered is not { } r ? ""
        : HasPendingAssociationChange ? "not applied"
        : r.State switch
        {
            AssociationState.Registered => "registered",
            AssociationState.NeedsRepair => "needs repair",
            _ => "not registered",
        };

    /// <summary>The sentence under the chip.</summary>
    public string AssociationDetail => _registered is not { } r ? ""
        : HasPendingAssociationChange ? "Changed above. Apply registers the new choice."
        : r.Detail;

    /// <summary>Whether the chip's dot is the success ink.</summary>
    public bool IsAssociated => _registered is { State: AssociationState.Registered } && !HasPendingAssociationChange;

    /// <summary>Whether the chip's dot is the muted ink: nothing registered, or a change owed.</summary>
    public bool IsAssociationIdle => _registered is { State: AssociationState.NotRegistered } || HasPendingAssociationChange;

    /// <summary>The apply button's word: Repair when the registration is broken and unchanged.</summary>
    public string ApplyLabel => _registered is { State: AssociationState.NeedsRepair } && !HasPendingAssociationChange
        ? "Repair" : "Apply";

    /// <summary>The shared-type line: what Windows keeps for the user to decide.</summary>
    public string DefaultsNote =>
        "Windows lets only you make nfty the default for pictures, models and .json: Apply lists nfty, Default apps sets it.";

    /// <summary>Sets what nfty's archives open in.</summary>
    /// <param name="target">Off, App or Viewer.</param>
    [RelayCommand] private void UseNftyTarget(AssociationTarget target) => NftyTarget = target;

    /// <summary>Sets what pictures and models open in.</summary>
    /// <param name="target">Off or Viewer; anything else is the viewer for these.</param>
    [RelayCommand] private void UsePicturesTarget(AssociationTarget target) =>
        PicturesTarget = target == AssociationTarget.Off ? AssociationTarget.Off : AssociationTarget.Viewer;

    /// <summary>Sets what metadata JSON opens in.</summary>
    /// <param name="target">Off or Viewer.</param>
    [RelayCommand] private void UseMetadataTarget(AssociationTarget target) =>
        MetadataTarget = target == AssociationTarget.Off ? AssociationTarget.Off : AssociationTarget.Viewer;

    partial void OnNftyTargetChanged(AssociationTarget value) => AssociationChanged();
    partial void OnPicturesTargetChanged(AssociationTarget value) => AssociationChanged();
    partial void OnMetadataTargetChanged(AssociationTarget value) => AssociationChanged();

    private void AssociationChanged()
    {
        OnPropertyChanged(nameof(Choice));
        OnPropertyChanged(nameof(HasPendingAssociationChange));
        OnPropertyChanged(nameof(AssociationSummary));
        OnPropertyChanged(nameof(AssociationDetail));
        OnPropertyChanged(nameof(IsAssociated));
        OnPropertyChanged(nameof(IsAssociationIdle));
        OnPropertyChanged(nameof(ApplyLabel));
        ApplyAssociationsCommand.NotifyCanExecuteChanged();
        RemoveAssociationsCommand.NotifyCanExecuteChanged();
    }

    private void ReadAssociations(bool adoptChoice)
    {
        if (_associations is null) return;
        AssociationStatus status;
        try { status = _associations.Inspect(); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex))
        {
            status = new AssociationStatus(AssociationState.NeedsRepair, AssociationChoice.None, AssociationText.CannotRead);
        }
        _registered = status;
        if (adoptChoice)
        {
            // Nothing registered: the trays propose a starting point rather than three Offs, and the
            // chip still says "not registered" until Apply.
            var shown = status.State == AssociationState.NotRegistered ? AssociationChoice.Suggested : status.Current;
            NftyTarget = shown.NftyFiles;
            PicturesTarget = shown.PicturesAndModels;
            MetadataTarget = shown.Metadata;
        }
        AssociationChanged();
    }

    private bool CanApplyAssociations() => _associations is not null && Choice.Any
        && (_registered is not { State: AssociationState.Registered } || HasPendingAssociationChange);

    [RelayCommand(CanExecute = nameof(CanApplyAssociations))]
    private async Task ApplyAssociationsAsync()
    {
        if (_associations is null) return;
        try { _associations.Apply(Choice); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex))
        {
            ReadAssociations(adoptChoice: false);
            await _dialogs.ShowErrorAsync(AssociationText.ApplyFailedTitle, AssociationText.ApplyFailed);
            return;
        }
        ReadAssociations(adoptChoice: false);
    }

    private bool CanRemoveAssociations() => _registered is { State: not AssociationState.NotRegistered };

    [RelayCommand(CanExecute = nameof(CanRemoveAssociations))]
    private async Task RemoveAssociationsAsync()
    {
        if (_associations is null) return;
        try { _associations.Remove(); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex))
        {
            ReadAssociations(adoptChoice: false);
            await _dialogs.ShowErrorAsync(AssociationText.RemoveFailedTitle, AssociationText.RemoveFailed);
            return;
        }
        ReadAssociations(adoptChoice: true);
    }

    private bool CanOpenDefaultApps() => _associations is not null;

    [RelayCommand(CanExecute = nameof(CanOpenDefaultApps))]
    private async Task OpenDefaultAppsAsync()
    {
        try { _associations?.OpenDefaultApps(); }
        catch (Exception ex) when (AssociationText.IsRefusal(ex) || ex is InvalidOperationException)
        {
            await _dialogs.ShowErrorAsync(AssociationText.DefaultAppsFailedTitle, AssociationText.DefaultAppsFailed);
        }
    }
}
