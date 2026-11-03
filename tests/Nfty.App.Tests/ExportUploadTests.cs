using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Ipfs;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The export card's UPLOAD page: it names the provider and its live connection, refuses what
/// cannot be uploaded in words, runs the whole drop through a (fake) service, shows the addresses a
/// contract needs, and counts as busy to the close guard while it runs.
/// </summary>
public class ExportUploadTests
{
    private sealed class FixedFolder(string dir) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(dir);
    }

    /// <summary>Answers a CID per folder; optionally holds every upload until released.</summary>
    internal sealed class FakePinning(bool reachable, TaskCompletionSource? gate = null) : IPinningService
    {
        public List<string> Labels { get; } = new();
        public string Provider => "Local IPFS";
        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PinningStatus(reachable, Provider, reachable ? "connected · kubo 0.29.0" : "not reachable",
                reachable ? "Connected to kubo 0.29.0 at http://127.0.0.1:5001."
                    : "Local IPFS is not reachable at http://127.0.0.1:5001. Start IPFS Desktop or Kubo (ipfs daemon)."));

        public async Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (gate is not null) await gate.Task.WaitAsync(cancellationToken);
            Labels.Add(label);
            return $"bafyfake{Labels.Count}000000000000000000";
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
    }

    internal static PublishingSettingsService Publishing(IPinningService pinning) =>
        new(StateStore.InMemory(), new InMemorySecretStore(), (_, _, _) => pinning);

    private static ExportDialogViewModel Dialog(IPinningService pinning, string outDir, NoopClipboardService? clipboard = null)
    {
        var vm = new ExportDialogViewModel(VisualCapture.ExportCaptureSet(), new FixedFolder(outDir),
            new NoopFolderRevealer(), new FakeDialogs(), publishing: Publishing(pinning), clipboard: clipboard);
        vm.ApplyPresetCommand.Execute(vm.Presets.Single(p => p.Preset == ExportPreset.Marketplace));
        vm.TokenIdNames = true;
        vm.Packed = false;
        vm.Tab = ExportTab.Upload;
        return vm;
    }

    [AvaloniaFact]
    public async Task A_connected_provider_uploads_and_shows_the_contract_addresses()
    {
        var outDir = Directory.CreateTempSubdirectory("nfty-upload-").FullName;
        var pin = new FakePinning(true);
        var clipboard = new NoopClipboardService();
        var vm = Dialog(pin, outDir, clipboard);
        await vm.PendingUploadCheck;

        Assert.True(vm.IsUploadConnected);
        Assert.Equal("", vm.UploadProblem);
        Assert.True(vm.UploadCommand.CanExecute(null));

        await vm.UploadCommand.ExecuteAsync(null);

        Assert.True(vm.IsDone);
        Assert.True(vm.HasUploadResult);
        Assert.Equal("ipfs://bafyfake2000000000000000000/", vm.UploadBaseUri);
        Assert.Equal("ipfs://bafyfake2000000000000000000/collection.json", vm.UploadContractUri);
        Assert.Equal("ipfs://bafyfake1000000000000000000/", vm.UploadImagesUri);
        Assert.Equal(["-images", "-metadata"], pin.Labels.Select(l => l[l.LastIndexOf('-')..]));

        // The local export carries the uploaded address.
        var meta = Directory.EnumerateFiles(Path.Combine(vm.OutputPath, "metadata")).Order(StringComparer.Ordinal).First();
        using var doc = JsonDocument.Parse(File.ReadAllText(meta));
        Assert.StartsWith("ipfs://bafyfake1000000000000000000/", doc.RootElement.GetProperty("image").GetString(), StringComparison.Ordinal);

        await vm.CopyCommand.ExecuteAsync(vm.UploadBaseUri);
        Assert.Equal(vm.UploadBaseUri, clipboard.Last);
    }

    [AvaloniaFact]
    public async Task An_unreachable_provider_disables_Upload_and_says_what_to_start()
    {
        var vm = Dialog(new FakePinning(false), Directory.CreateTempSubdirectory().FullName);
        await vm.PendingUploadCheck;

        Assert.False(vm.IsUploadConnected);
        Assert.False(vm.UploadCommand.CanExecute(null));
        Assert.Contains("Start IPFS Desktop or Kubo", vm.UploadProblem, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task What_cannot_be_uploaded_is_refused_in_Cores_words()
    {
        var vm = Dialog(new FakePinning(true), Directory.CreateTempSubdirectory().FullName);
        await vm.PendingUploadCheck;
        vm.OpenSeaMetadata = false;
        Assert.Equal(MarketplaceUpload.Problem(ExportOptions.For(ExportPreset.Marketplace) with { OpenSeaMetadata = false }),
            vm.UploadProblem);
        Assert.False(vm.UploadCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task A_running_upload_is_busy_to_the_close_guard_and_cancels()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = Dialog(new FakePinning(true, gate), Directory.CreateTempSubdirectory().FullName);
        await vm.PendingUploadCheck;

        var run = vm.UploadCommand.ExecuteAsync(null);
        await Until(() => vm.IsRunning);

        Assert.True(vm.IsBusy);
        Assert.False(await vm.ConfirmLeaveAsync());
        vm.CancelCommand.Execute(null);
        await run;

        Assert.False(vm.IsRunning);
        Assert.False(vm.IsDone);
        Assert.Contains("stays pinned", vm.PhaseText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task The_footer_action_follows_the_page_and_the_card_keeps_its_height()
    {
        var vm = Dialog(new FakePinning(true), Directory.CreateTempSubdirectory().FullName);
        await vm.PendingUploadCheck;
        var view = new Views.ExportDialogView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            var upload = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "UploadButton");
            var export = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ExportButton");
            Assert.True(upload.IsVisible);
            Assert.False(export.IsVisible);
            double uploadHeight = card.Bounds.Height;

            vm.Tab = ExportTab.Contents;
            Dispatcher.UIThread.RunJobs();
            Assert.False(upload.IsVisible);
            Assert.True(export.IsVisible);
            Assert.Equal(card.Bounds.Height, uploadHeight);
        }
        finally { window.Close(); }
    }
}
