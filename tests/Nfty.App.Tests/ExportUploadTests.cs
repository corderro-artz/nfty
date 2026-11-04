using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    internal sealed class FixedFolder(string dir) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(dir);
    }

    /// <summary>Answers a CID per folder; optionally holds every upload until released.</summary>
    internal sealed class FakePinning(bool reachable, TaskCompletionSource? gate = null, bool pinata = false) : IPinningService
    {
        public List<string> Labels { get; } = new();
        public string Provider => pinata ? "Pinata" : "Local IPFS";
        public Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(pinata
                ? new PinningStatus(reachable, Provider, reachable ? "connected · pinata" : "key refused",
                    reachable ? "Pinata accepted the key." : "Pinata refused the key (401). Check it, or save a new one.")
                : new PinningStatus(reachable, Provider, reachable ? "connected · kubo 0.29.0" : "not reachable",
                reachable ? "Connected to kubo 0.29.0 at http://127.0.0.1:5001."
                    : "Local IPFS is not reachable at http://127.0.0.1:5001. Start IPFS Desktop or Kubo (ipfs daemon)."));

        public Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

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

    private static ExportDialogViewModel ModeledMarketplace()
    {
        var vm = new ExportDialogViewModel(VisualCapture.ExportCaptureSet(withModels: true), new FixedFolder("x"),
            new NoopFolderRevealer(), new FakeDialogs(), publishing: Publishing(new FakePinning(true)));
        vm.ApplyPresetCommand.Execute(vm.Presets.Single(p => p.Preset == ExportPreset.Marketplace));
        return vm;
    }

    [AvaloniaFact]
    public async Task After_an_upload_the_manifest_describes_what_was_written_and_empty_rows_cannot_be_copied()
    {
        var vm = Dialog(new FakePinning(true), Directory.CreateTempSubdirectory("nfty-upload-").FullName);
        await vm.PendingUploadCheck;

        // Before the run, on UPLOAD, "will do" notes are not warnings: none of them is listed.
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("relative", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("no image", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Consequences, c => c.Contains('<'));
        Assert.Contains("token id", vm.UploadChoicesNote, StringComparison.Ordinal);

        await vm.UploadCommand.ExecuteAsync(null);

        // After it, the plan is made from the addresses the upload gave: nothing stale is left.
        vm.Tab = ExportTab.Contents;
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("relative", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("no image", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("--", StringComparison.Ordinal));

        // This Set has no models: the row says none, and has nothing to copy. The contract row has one.
        Assert.False(vm.HasUploadModels);
        Assert.True(vm.HasUploadContract);
    }

    [AvaloniaFact]
    public void The_dialog_words_its_warnings_with_field_names_not_flags()
    {
        var vm = new ExportDialogViewModel(VisualCapture.ExportCaptureSet(), new FixedFolder("x"),
            new NoopFolderRevealer(), new FakeDialogs());
        Assert.NotEmpty(vm.Consequences);
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("--", StringComparison.Ordinal));
        Assert.Contains(vm.Consequences, c => c.Contains("Image base URI", StringComparison.Ordinal));
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
        // An address in the field, so no page carries the "links are relative" warnings: at this window
        // those outgrow the manifest's reserved height on every page, which clamps the card and hides
        // a height change behind the clamp. Without them the card is free to grow, so equal heights
        // here mean the footer swap and the UPLOAD page really move nothing.
        vm.ImageBaseUri = "ipfs://bafyexample/";
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
            string Rows() => string.Join(",", ((Grid)card.Child!).Children.Select(c => $"{c.GetType().Name}:{c.Bounds.Height:0.#}"));
            string up = Rows();

            vm.Tab = ExportTab.Contents;
            Dispatcher.UIThread.RunJobs();
            Assert.False(upload.IsVisible);
            Assert.True(export.IsVisible);
            Assert.True(card.Bounds.Height == uploadHeight, up + " vs " + Rows());
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("ready")]
    [InlineData("unreachable")]
    [InlineData("running")]
    [InlineData("done")]
    // The marketplace preset with no address yet, on CONTENTS: two link warnings in the manifest.
    // Wrapped, they pushed the card past the window and every tab scrolled; one line each fits.
    [InlineData("contents")]
    // The same with a Set that has models: a third link warning (models not linked) must not scroll it.
    [InlineData("contents-models")]
    // A plain export that finished: the result line and path strip join the page.
    [InlineData("export-done")]
    public async Task The_upload_page_never_scrolls_at_the_smallest_window(string state)
    {
        ExportDialogViewModel vm = state switch
        {
            "done" => VisualCapture.UploadDone(new FakeDialogs()),
            "contents-models" => ModeledMarketplace(),
            _ => Dialog(new FakePinning(state != "unreachable"), Directory.CreateTempSubdirectory().FullName),
        };
        await vm.PendingUploadCheck;
        if (state is "contents" or "contents-models") vm.Tab = ExportTab.Contents;
        if (state == "export-done") { vm.Tab = ExportTab.Contents; vm.OutputPath = Path.Combine(Path.GetTempPath(), "Chest Demo.set"); vm.ResultText = "Exported 12 assets."; vm.IsDone = true; }
        if (state == "running") { vm.IsRunning = true; vm.PhaseText = "Uploading images... 1.2 MB of 3.1 MB"; }
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
            var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"{state}: the body wants {scroller.Extent.Height:0} of {scroller.Viewport.Height:0}");
        }
        finally { window.Close(); }
    }
}
