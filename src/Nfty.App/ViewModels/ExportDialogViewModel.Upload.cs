using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Ipfs;

namespace Nfty.App.ViewModels;

/// <summary>
/// The UPLOAD page: the marketplace drop's two steps done in one, through the provider chosen in
/// Settings — the art pinned, the metadata written with its address and pinned, and the export
/// written to the chosen folder with the same addresses (<see cref="MarketplaceUpload"/>).
/// </summary>
/// <remarks>
/// <para><b>The footer's accent button follows the page.</b> On UPLOAD it reads "Upload" and runs
/// the upload; everywhere else it reads "Export". One primary action per card, named for what it
/// will do from where the reader is.</para>
/// <para>A running upload is a running export to the close guard: <see cref="IsBusy"/> already
/// reads <see cref="IsRunning"/>, which this sets.</para>
/// </remarks>
public partial class ExportDialogViewModel
{
    private readonly IPublishingSettings? _publishing;
    private readonly IClipboardService? _clipboard;
    private CancellationTokenSource? _uploadCheckCts;

    /// <summary>
    /// The manifest's warnings as the UPLOAD page reads them. Three of Core's sentences tell the
    /// reader to upload first and export again - the very step this page performs - so here they
    /// say what the upload will do instead.
    /// </summary>
    /// <remarks>
    /// <b>Reworded, never dropped.</b> The manifest is reserved at one height for every page and each
    /// consequence is one line, so a page that showed FEWER lines than its neighbors would change the
    /// card's height when the tab changed; <c>Every_tab_is_the_same_height_with_the_models_row</c> holds it.
    /// </remarks>
    private static IEnumerable<string> UploadPhrased(IEnumerable<string> warnings) =>
        warnings.Select(w =>
            w.StartsWith("Image links are relative", StringComparison.Ordinal)
                ? "Image links are relative until the upload writes its address: each becomes ipfs://<imagesCID>/ and its file name."
            : w.StartsWith("Models are not linked", StringComparison.Ordinal)
                ? "Models are not linked yet: the upload writes ipfs://<modelsCID>/ into each glb's animation_url."
            : w.StartsWith("collection.json has no image", StringComparison.Ordinal)
                ? "collection.json has no image yet: the upload gives it the first token's ipfs:// image."
            : w);

    /// <summary>Whether the footer's primary button exports (every page but UPLOAD).</summary>
    public bool IsExportAction => !IsUploadTab;

    /// <summary>Where uploads go, as one line: <c>Local IPFS · http://127.0.0.1:5001</c>.</summary>
    public string UploadProviderLabel => _publishing?.ProviderLabel ?? "No upload provider here";

    /// <summary>The provider's connection, as the chip shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUploadConnected), nameof(IsUploadChecking))]
    private ConnectionState _connection = ConnectionState.Checking;

    /// <summary>Whether the chip's dot is the success ink.</summary>
    public bool IsUploadConnected => Connection == ConnectionState.Connected;

    /// <summary>Whether the chip's dot is the muted ink.</summary>
    public bool IsUploadChecking => Connection is ConnectionState.Checking or ConnectionState.Unchecked;

    /// <summary>A few words on the chip.</summary>
    [ObservableProperty] private string _uploadStatusSummary = "checking…";

    /// <summary>The sentence under the chip.</summary>
    [ObservableProperty] private string _uploadStatusDetail = "";

    /// <summary>The check in flight, for a test to await. Never faults.</summary>
    public Task PendingUploadCheck { get; private set; } = Task.CompletedTask;

    /// <summary>The upload in flight, for a test to await. Never faults.</summary>
    public Task PendingUpload { get; private set; } = Task.CompletedTask;

    /// <summary>Why Upload is unavailable, in words; empty when it is available.</summary>
    public string UploadProblem
    {
        get
        {
            if (_publishing is null) return "No upload provider is available here.";
            if (MarketplaceUpload.Problem(Options) is { } options) return options;
            if (Problem.Length > 0) return Problem;
            return Connection switch
            {
                ConnectionState.Connected => "",
                ConnectionState.Checking => "Checking the connection…",
                ConnectionState.Unchecked => "Check the connection first.",
                _ => UploadStatusDetail,
            };
        }
    }

    private void UploadInputsChanged()
    {
        OnPropertyChanged(nameof(UploadProblem));
        UploadCommand.NotifyCanExecuteChanged();
    }

    partial void OnUploadStatusDetailChanged(string value) => UploadInputsChanged();

    // Opening the page re-asks a provider that was not reachable: the reader may have just started it.
    partial void OnTabChanged(ExportTab value)
    {
        Refresh();   // the manifest words its warnings for the upload on UPLOAD (UploadPhrased)
        if (value == ExportTab.Upload && Connection is ConnectionState.NotReachable or ConnectionState.Unchecked)
            PendingUploadCheck = CheckUploadAsync();
    }

    /// <summary>Checks the provider again.</summary>
    [RelayCommand]
    private Task RecheckUpload() => PendingUploadCheck = CheckUploadAsync();

    private async Task CheckUploadAsync()
    {
        if (_publishing is null)
        {
            SetUploadStatus(ConnectionState.NotReachable, "unavailable", "No upload provider is available here.");
            return;
        }
        _uploadCheckCts?.Cancel();
        var cts = _uploadCheckCts = new CancellationTokenSource();
        SetUploadStatus(ConnectionState.Checking, "checking…", "Asking " + _publishing.ProviderLabel + "…");
        try
        {
            var status = await _publishing.CreateService().StatusAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            SetUploadStatus(status.Reachable ? ConnectionState.Connected : ConnectionState.NotReachable,
                status.Summary, status.Detail);
        }
        catch (OperationCanceledException) { /* superseded */ }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) SetUploadStatus(ConnectionState.NotReachable, "not reachable", ex.Message);
        }
    }

    private void SetUploadStatus(ConnectionState state, string summary, string detail)
    {
        UploadStatusSummary = summary;
        UploadStatusDetail = detail;
        Connection = state;
    }

    private bool CanUpload() => !IsRunning && !IsDone && UploadProblem.Length == 0;

    /// <summary>Picks a destination, uploads, and writes the export with the uploaded addresses.</summary>
    [RelayCommand(CanExecute = nameof(CanUpload))]
    private async Task UploadAsync()
    {
        if (_publishing is null) return;
        var dir = await _picker.PickFolderAsync("Export the uploaded Set to folder");
        if (dir is null) return;
        PendingUpload = RunUploadAsync(dir);
        await PendingUpload;
    }

    private async Task RunUploadAsync(string dir)
    {
        IsRunning = true;
        Progress = 0;
        PhaseText = "Starting...";
        _cts = new CancellationTokenSource();
        try
        {
            var reporter = new Progress<UploadProgress>(p =>
            {
                Progress = p.Fraction;
                PhaseText = p.Phase;
            });
            var result = await MarketplaceUpload.RunAsync(_setDirectory, dir, Options, _publishing!.CreateService(),
                CookBookPath, reporter, _cts.Token, _threads?.Current);

            _written = result.Local.Path;
            OutputPath = result.Local.Path;
            UploadBaseUri = result.BaseUri;
            UploadContractUri = result.ContractUri ?? "none: collection.json is off";
            UploadImagesUri = $"ipfs://{result.ImageCid}/";
            UploadModelsUri = result.ModelCid is { } m ? $"ipfs://{m}/" : "none";
            UploadMetadataUri = $"ipfs://{result.MetadataCid}/";
            UploadWarnings = result.Warnings;
            HasUploadResult = true;
            ResultText = $"Uploaded {result.Local.Plan.Count} assets to {result.Provider} and exported them with the same addresses.";
            IsDone = true;
        }
        catch (OperationCanceledException)
        {
            PhaseText = "Canceled. Anything already uploaded stays pinned.";
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Upload failed", ex.Message);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Whether the upload finished and its addresses are showing.</summary>
    [ObservableProperty] private bool _hasUploadResult;

    /// <summary>The contract's <c>baseURI</c>.</summary>
    [ObservableProperty] private string _uploadBaseUri = "";

    /// <summary>The contract's <c>contractURI</c>, or why there is none.</summary>
    [ObservableProperty] private string _uploadContractUri = "";

    /// <summary>The uploaded <c>images/</c> folder.</summary>
    [ObservableProperty] private string _uploadImagesUri = "";

    /// <summary>The uploaded <c>models/</c> folder, or <c>none</c>.</summary>
    [ObservableProperty] private string _uploadModelsUri = "";

    /// <summary>The uploaded <c>metadata/</c> folder.</summary>
    [ObservableProperty] private string _uploadMetadataUri = "";

    /// <summary>What about the result is worth stopping for, in Core's words.</summary>
    [ObservableProperty] private IReadOnlyList<string> _uploadWarnings = Array.Empty<string>();

    /// <summary>Puts an address on the clipboard.</summary>
    /// <param name="text">The address.</param>
    [RelayCommand]
    private async Task CopyAsync(string? text)
    {
        if (text is { Length: > 0 } && _clipboard is not null) await _clipboard.SetTextAsync(text);
    }
}
