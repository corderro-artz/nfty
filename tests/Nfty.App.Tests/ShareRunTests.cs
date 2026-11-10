using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>GitHub, scripted: every call is recorded, every outcome chosen, nothing reaches the network.</summary>
internal sealed class ScriptedBackend : IShareBackend
{
    private int _running;
    public int MaxConcurrent { get; private set; }
    public List<ShareRequest> Shared { get; } = [];
    public List<string> Unshared { get; } = [];
    public List<PendingShare> Finished { get; } = [];
    public Dictionary<int, GitHubPull> Pulls { get; } = [];
    public Dictionary<int, IReadOnlyList<string>> Comments { get; } = [];
    public TaskCompletionSource? Gate { get; set; }
    public Exception? FailWith { get; set; }
    public Exception? PullFails { get; set; }
    public int NextPull { get; set; } = 12;
    public IReadOnlyCollection<string> LastProtected { get; private set; } = [];

    private async Task Enter(CancellationToken ct)
    {
        MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _running));
        try
        {
            if (Gate is { } gate) await gate.Task.WaitAsync(ct);
        }
        finally { Interlocked.Decrement(ref _running); }
    }

    public async Task<ShareResult> ShareAsync(ShareRequest request, IReadOnlyCollection<string> protectedTags, IProgress<ShareProgress>? progress,
        CancellationToken ct)
    {
        Shared.Add(request);
        LastProtected = protectedTags;
        progress?.Report(new ShareProgress(ShareStage.Exporting, 0.5));
        await Enter(ct);
        if (FailWith is { } fail) throw fail;
        int n = NextPull++;
        string slug = ShareSlug.Of(request.Name);
        return new ShareResult(n, PullUrl(n), slug, slug + "-v1", new PendingShare(n, slug, request.ReplaceSharedVersion ? slug + "-v0" : null, null));
    }

    public async Task<int> UnshareAsync(string slug, CancellationToken ct)
    {
        Unshared.Add(slug);
        await Enter(ct);
        if (FailWith is { } fail) throw fail;
        return NextPull++;
    }

    public Task<GitHubPull> GetPullAsync(int number, CancellationToken ct)
    {
        if (PullFails is { } fail) throw fail;
        return Task.FromResult(Pulls.GetValueOrDefault(number, new GitHubPull("open", false)));
    }

    public Task<IReadOnlyList<string>> GetCommentsAsync(int number, CancellationToken ct) =>
        Task.FromResult(Comments.GetValueOrDefault(number, []));

    public Task FinishAsync(PendingShare pending, CancellationToken ct)
    {
        Finished.Add(pending);
        return Task.CompletedTask;
    }

    public Uri PullUrl(int number) => new($"https://github.com/nfty-gallery/nfty-gallery/pull/{number}");
}

/// <summary>The share run, pending shares on the Community page, finishing merged ones, and Unshare.</summary>
public class ShareRunTests
{
    private const long Me = 7;

    private sealed class Rig : IDisposable
    {
        public ScriptedBackend Backend { get; } = new();
        public FakeCommunityCache Cache { get; } = new();
        public FakeGitHubAccount Account { get; } = new() { Login = "me", UserId = Me };
        public ShareScriptedDialogs Dialogs { get; } = new();
        public StatusService Status { get; } = new();
        public CommunitySharing Sharing { get; }
        public DateTimeOffset Now { get; set; } = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        public Rig() => Sharing = new CommunitySharing(Backend, Cache, Account, () => Now);

        public CommunityViewModel Page(params CommunityEntry[] entries)
        {
            Cache.Next = CommunityFixtures.Listing(entries);
            return new CommunityViewModel(Cache, new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName), Account,
                new FakeNav(), Dialogs, Status, _ => Task.CompletedTask, sharing: Sharing) { PageSize = 8 };
        }

        public void Dispose() { }
    }

    private static ShareRequest Request(string name = "New Pets", bool replace = false) =>
        new("set", false, null, false, name, "", [], null, replace);

    private static ShareRunInfo Info(string name = "New Pets", bool listed = false) => new(name, 40, 16, 16, false, false, 1_000_000, listed);

    private static CommunityEntry Mine(string slug, string name = "Mine") => CommunityFixtures.Entry(slug, name, "me", ownerId: Me);

    /// <summary>A failure thrown by the framework, whose message is not nfty's wording.</summary>
    private static ShareResult ParseTrouble() => throw new FormatException(int.Parse("SECRET-STACK-TEXT").ToString());

    // ---- the card's run -----------------------------------------------------------------------------------------------

    private static async Task<(ShareDialogViewModel Vm, ShareDialogTests.Rig Rig)> CardAsync(
        Func<ShareRequest, ShareRunInfo, IProgress<ShareProgress>, CancellationToken, Task<ShareResult>> run,
        NoopUrlOpener? urls = null, NoopClipboardService? clipboard = null)
    {
        var rig = new ShareDialogTests.Rig();
        var vm = new ShareDialogViewModel(rig.Source, rig.Dialogs, rig.Account, rig.Cache, rig.Repo, rig.Checker,
            run: run, tags: () => [], urls: urls, clipboard: clipboard);
        await vm.PendingBook;
        await vm.PendingFacts;
        await vm.PendingRepo;
        return (vm, rig);
    }

    /// <summary>The card in a run state, for the layout, contrast and capture checks: <c>running</c> (parked mid-upload),
    /// <c>done</c> or <c>failed</c>.</summary>
    internal static async Task<(ShareDialogViewModel Vm, ShareDialogTests.Rig Rig)> RunStateAsync(string state)
    {
        var (vm, rig) = await CardAsync(async (_, _, progress, ct) =>
        {
            progress.Report(new ShareProgress(ShareStage.Uploading, 0.4));
            if (state == "running") await Task.Delay(Timeout.Infinite, ct);
            if (state == "failed") throw new CommunityException(ShareRules.Unreachable);
            return ShareFixtures.Result(31);
        });
        var run = vm.ShareCommand.ExecuteAsync(null);
        if (state == "running") Dispatcher.UIThread.RunJobs();
        else UiThreadWait.For(run);
        return (vm, rig);
    }

    [AvaloniaFact]
    public async Task Done_state_links_the_pull()
    {
        var urls = new NoopUrlOpener();
        var clipboard = new NoopClipboardService();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, rig) = await CardAsync(async (_, _, progress, ct) =>
        {
            progress.Report(new ShareProgress(ShareStage.Exporting, 0.5));
            await gate.Task.WaitAsync(ct);
            return ShareFixtures.Result(31);
        }, urls, clipboard);
        using var _ = rig;
        using var __ = vm;

        var run = vm.ShareCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ShareState.Running, vm.State);
        Assert.True(vm.IsRunning);
        Assert.Equal("Packing the Set…", vm.StageText);
        Assert.InRange(vm.Fraction, 0.1, 0.5);
        Assert.Equal("Stop", vm.CancelLabel);

        gate.SetResult();
        await run;

        Assert.Equal(ShareState.Done, vm.State);
        Assert.True(vm.IsDone);
        Assert.Equal("https://github.com/nfty-gallery/nfty-gallery/pull/31", vm.PullText);
        Assert.Equal("Submitted. The gallery checks it now; it usually lists within a few minutes.", ShareDialogViewModel.DoneText);
        Assert.Equal("Close", vm.CancelLabel);

        await vm.CopyPullCommand.ExecuteAsync(null);
        Assert.Equal(vm.PullText, clipboard.Last);
        vm.OpenPullCommand.Execute(null);
        Assert.Equal(vm.PullUrl, urls.Last);
    }

    [AvaloniaFact]
    public async Task A_failed_run_shows_the_sentence_verbatim_and_never_exception_text()
    {
        var (vm, rig) = await CardAsync((_, _, _, _) => throw new CommunityException(ShareRules.Unreachable));
        using var _ = rig;
        using var __ = vm;
        await vm.ShareCommand.ExecuteAsync(null);
        Assert.Equal(ShareState.Failed, vm.State);
        Assert.Equal(ShareRules.Unreachable, vm.Failure);
        Assert.Equal("Close", vm.CancelLabel);

        vm.TryAgainCommand.Execute(null);
        Assert.Equal(ShareState.Editing, vm.State);

        var (vm2, rig2) = await CardAsync((_, _, _, _) => Task.FromResult(ParseTrouble()));
        using var _2 = rig2;
        using var __2 = vm2;
        await vm2.ShareCommand.ExecuteAsync(null);
        Assert.Equal(ShareState.Failed, vm2.State);
        Assert.DoesNotContain("SECRET-STACK-TEXT", vm2.Failure, StringComparison.Ordinal);
        Assert.NotEmpty(vm2.Failure);
    }

    [AvaloniaFact]
    public async Task Window_close_is_refused_while_sharing()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, rig) = await CardAsync(async (_, _, _, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return ShareFixtures.Result();
        });
        using var rigGuard = rig;
        using var cardGuard = vm;
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService());
        var shown = dialogs.ShowAsync<object>(vm); // shown, not awaited: it completes when the card closes

        Assert.True(await shell.ConfirmCloseAsync());      // idle card: nothing is lost
        var run = vm.ShareCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.IsBusy);
        Assert.False(await vm.ConfirmLeaveAsync());
        Assert.False(await shell.ConfirmCloseAsync());

        // Escape on a running card stops it: the run unwinds to the form, taking back what it made.
        await vm.CancelAndWaitAsync();
        await run;
        Assert.Equal(ShareState.Editing, vm.State);
        Assert.False(vm.IsBusy);
        Assert.True(await shell.ConfirmCloseAsync());
    }

    // ---- the service ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Runs_are_serialized_app_wide()
    {
        using var rig = new Rig();
        rig.Backend.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = rig.Sharing.ShareAsync(Request("One"), Info("One"), null, CancellationToken.None);
        var second = rig.Sharing.UnshareAsync(Mine("two"), CancellationToken.None);
        await Task.Delay(50);
        Assert.Single(rig.Backend.Shared);
        Assert.Empty(rig.Backend.Unshared);               // waits its turn

        rig.Backend.Gate.SetResult();
        await first;
        await second;
        Assert.Equal(1, rig.Backend.MaxConcurrent);
        Assert.Equal(2, rig.Sharing.Pending.Count);
    }

    [Fact]
    public async Task A_stopped_run_leaves_nothing_pending()
    {
        using var rig = new Rig();
        rig.Backend.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var run = rig.Sharing.ShareAsync(Request(), Info(), null, cts.Token);
        await Task.Delay(30);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Empty(rig.Sharing.Pending);
        // and the next run is not blocked by the stopped one
        rig.Backend.Gate = null;
        await rig.Sharing.ShareAsync(Request("Next"), Info("Next"), null, CancellationToken.None);
        Assert.Single(rig.Sharing.Pending);
    }

    [Fact]
    public async Task A_run_is_told_the_live_version_and_every_open_pull_requests_tag_to_keep()
    {
        using var rig = new Rig();
        var live = Mine("new-pets") with { Release = "new-pets-v1" };
        rig.Cache.Cached = CommunityFixtures.Listing([live]);

        await rig.Sharing.ShareAsync(Request(replace: true), Info(listed: true), null, CancellationToken.None);
        Assert.Equal(["new-pets-v1"], rig.Backend.LastProtected.Order(StringComparer.Ordinal));
        Assert.Equal("new-pets-v1", Assert.Single(rig.Sharing.Pending).Tag);

        // a second update before the first merges: the first one's new tag is protected too (the fake names every run's tag v1,
        // and supersedes v0)
        await rig.Sharing.ShareAsync(Request(replace: true), Info(listed: true), null, CancellationToken.None);
        Assert.Contains("new-pets-v1", rig.Backend.LastProtected);
        Assert.Contains("new-pets-v0", rig.Backend.LastProtected);
    }

    [Fact]
    public void A_pending_file_with_no_list_reads_as_nothing_pending()
    {
        string root = Directory.CreateTempSubdirectory("nfty-pending-null-").FullName;
        File.WriteAllText(Path.Combine(root, "pending.json"), "{\"schemaVersion\":1,\"pending\":null}");
        Assert.Empty(new CommunityCache(root).Pending);
        File.WriteAllText(Path.Combine(root, "pending.json"), "{\"schemaVersion\":1}");
        Assert.Empty(new CommunityCache(root).Pending);
    }

    [Fact]
    public async Task Pending_survives_a_restart()
    {
        var root = Directory.CreateTempSubdirectory("nfty-pending-").FullName;
        var cache = new CommunityCache(root);
        using var rig = new Rig();
        var sharing = new CommunitySharing(rig.Backend, cache, rig.Account);
        await sharing.ShareAsync(Request(), Info(), null, CancellationToken.None);

        var again = new CommunityCache(root);
        var record = Assert.Single(again.Pending);
        Assert.Equal("new-pets", record.Slug);
        Assert.Equal($"{Me}/new-pets", record.Id);
        Assert.Equal(PendingRecord.ShareKind, record.Kind);
        Assert.Equal(12, record.PullNumber);

        again.SavePending([]);
        Assert.False(File.Exists(Path.Combine(root, "pending.json")));
    }

    // ---- the page ------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task Pending_tile_appears_before_merge()
    {
        using var rig = new Rig();
        await rig.Sharing.ShareAsync(Request("New Pets"), Info("New Pets"), null, CancellationToken.None);

        using var vm = rig.Page(Mine("old", "Old One"));
        await vm.PendingRefresh;

        var tile = Assert.Single(vm.Tiles, t => t.Name == "New Pets");   // the gallery does not list it yet
        Assert.True(tile.IsPending);
        Assert.Equal("Pending", tile.MarkText);
        Assert.Equal(1.0, tile.MarkOpacity);
        Assert.Equal(40, tile.Entry.Assets);
        Assert.Contains(vm.Tiles, t => t.Name == "Old One" && !t.IsPending);

        vm.SelectCommand.Execute(tile);
        Assert.False(vm.DownloadCommand.CanExecute(null));     // nothing to download until it is listed
        Assert.False(vm.CanUnshareSelected);
        Assert.Contains("Submitted", vm.DownloadNote, StringComparison.Ordinal);
        Assert.Empty(rig.Backend.Finished);                    // still open: nothing to finish
    }

    [AvaloniaFact]
    public async Task Merged_pending_runs_finish_once()
    {
        using var rig = new Rig();
        // an update of a share the gallery lists already, whose old Release goes once the new one merges
        await rig.Sharing.ShareAsync(Request("Mine", replace: true), Info("Mine", listed: true), null, CancellationToken.None);
        await rig.Sharing.ShareAsync(Request("Fresh"), Info("Fresh"), null, CancellationToken.None);
        rig.Backend.Pulls[12] = new GitHubPull("closed", true);
        rig.Backend.Pulls[13] = new GitHubPull("closed", true);

        using var vm = rig.Page(Mine("mine"));
        await vm.PendingRefresh;

        Assert.Equal(2, rig.Backend.Finished.Count);           // each merged request finished
        Assert.Equal("mine-v0", rig.Backend.Finished.Single(f => f.PullNumber == 12).SupersededTag);

        // The update's tile was already the right one, so its record is gone; the new share stays until the list shows it.
        var left = Assert.Single(rig.Sharing.Pending);
        Assert.Equal("fresh", left.Slug);
        Assert.True(left.Finished);

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, rig.Backend.Finished.Count);           // never again

        rig.Cache.Next = CommunityFixtures.Listing([Mine("mine"), Mine("fresh", "Fresh")]);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Empty(rig.Sharing.Pending);                     // listed now: dropped
        Assert.Equal(2, rig.Backend.Finished.Count);
        Assert.All(vm.Tiles, t => Assert.False(t.IsPending));
    }

    [AvaloniaFact]
    public async Task GitHub_trouble_never_blocks_browsing_and_is_tried_again()
    {
        using var rig = new Rig();
        await rig.Sharing.ShareAsync(Request(), Info(), null, CancellationToken.None);
        rig.Backend.PullFails = new CommunityException(ShareRules.Unreachable);

        using var vm = rig.Page(Mine("old"));
        await vm.PendingRefresh;
        Assert.Equal(2, vm.Tiles.Count);                       // the list shows, the pending tile stands
        Assert.Single(rig.Sharing.Pending);
        Assert.False(vm.StatusIsWarning);                      // nothing said about it

        rig.Backend.PullFails = null;
        rig.Backend.Pulls[12] = new GitHubPull("closed", true);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Single(rig.Backend.Finished);
    }

    [AvaloniaFact]
    public async Task Closed_pending_shows_the_actions_reasons()
    {
        using var rig = new Rig();
        await rig.Sharing.ShareAsync(Request("New Pets"), Info("New Pets"), null, CancellationToken.None);
        rig.Backend.Pulls[12] = new GitHubPull("closed", false);
        rig.Backend.Comments[12] =
        [
            "The gallery checked this share:\n\n- The canvas is larger than 512 x 512.\n* Another listing carries these assets.\n1. Numbered reason.\n<!-- nfty-check -->",
        ];

        using var vm = rig.Page(Mine("old"));
        await vm.PendingRefresh;

        var tile = Assert.Single(vm.Tiles, t => t.Name == "New Pets");
        Assert.True(tile.IsClosed);
        Assert.False(tile.IsPending);
        Assert.Equal("Closed", tile.MarkText);
        Assert.Empty(rig.Backend.Finished);

        vm.SelectCommand.Execute(tile);
        Assert.Equal(
            "The gallery checked this share: The canvas is larger than 512 x 512. Another listing carries these assets. Numbered reason.",
            vm.PendingNote);
        Assert.Equal(vm.PendingNote, vm.DownloadNote);
        Assert.True(vm.CanDismissSelected);

        // kept across refreshes until dismissed
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.NotEmpty(vm.PendingNote);

        vm.DismissPendingCommand.Execute(null);
        Assert.Empty(rig.Sharing.Pending);
        Assert.DoesNotContain(vm.Tiles, t => t.Name == "New Pets");
    }

    [AvaloniaFact]
    public async Task Unshare_asks_then_opens_the_pull()
    {
        using var rig = new Rig();
        using var vm = rig.Page(Mine("mine", "Mine"));
        await vm.PendingRefresh;
        vm.SelectCommand.Execute(vm.Tiles[0]);
        Assert.True(vm.CanUnshareSelected);

        // declined: nothing is sent
        rig.Dialogs.Answer = false;
        await vm.UnshareCommand.ExecuteAsync(null);
        var ask = Assert.IsType<ConfirmDialogViewModel>(rig.Dialogs.Shown[0]);
        Assert.Equal("Remove \"Mine\" from the gallery? Your nfty-public repository stays.", ask.Message);
        Assert.Empty(rig.Backend.Unshared);
        Assert.Empty(rig.Sharing.Pending);

        // confirmed: the removal pull request is opened and remembered
        rig.Dialogs.Answer = true;
        await vm.UnshareCommand.ExecuteAsync(null);
        Assert.Equal(["mine"], rig.Backend.Unshared);
        var record = Assert.Single(rig.Sharing.Pending);
        Assert.Equal(PendingRecord.UnshareKind, record.Kind);
        Assert.Equal(12, record.PullNumber);
        Assert.Equal("https://github.com/nfty-gallery/nfty-gallery/pull/12", record.PullUrl);
        Assert.Equal("v1", record.ReplacedTag);
        Assert.Contains("pull request #12", rig.Status.Last, StringComparison.Ordinal);
        Assert.True(vm.Tiles[0].IsPending);
        Assert.False(vm.CanUnshareSelected);                    // one request at a time per collection

        // merged and gone from the list: finished once (the Release goes), then forgotten
        rig.Backend.Pulls[12] = new GitHubPull("closed", true);
        rig.Cache.Next = CommunityFixtures.Listing([]);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Single(rig.Backend.Finished);
        Assert.Equal("v1", rig.Backend.Finished[0].ReplacedTag);
        Assert.Empty(rig.Sharing.Pending);
    }

    [AvaloniaFact]
    public async Task A_failed_unshare_says_the_sentence_and_leaves_nothing_pending()
    {
        using var rig = new Rig();
        using var vm = rig.Page(Mine("mine"));
        await vm.PendingRefresh;
        vm.SelectCommand.Execute(vm.Tiles[0]);
        rig.Dialogs.Answer = true;
        rig.Backend.FailWith = new CommunityException(ShareRules.Unreachable);

        await vm.UnshareCommand.ExecuteAsync(null);

        Assert.Empty(rig.Sharing.Pending);
        Assert.Contains(rig.Dialogs.Shown, d => d is ErrorDialogViewModel e && e.Message == ShareRules.Unreachable);
        Assert.True(vm.CanUnshareSelected);
    }

    [Fact]
    public void Reasons_lose_their_markdown_bullets()
    {
        var lines = CommunitySharing.ReasonLines(["Header\n- one\n  * two\n3) three\n**bold** and `code`\n\n<!-- hidden -->\n- one"]);
        Assert.Equal(["Header", "one", "two", "three", "bold and code"], lines);
    }
}
