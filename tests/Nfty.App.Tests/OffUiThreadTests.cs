using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Work that read every pixel of a book or unpacked a whole Set used to run on the UI thread. These
/// pin each of those jobs behind a busy card or off the constructor, and pin the limit the thumbnail
/// decode keeps.
/// </summary>
public class OffUiThreadTests
{
    /// <summary>Records every dialog shown, in order, and answers a chosen one with a chosen result.</summary>
    private sealed class RecordingDialogs(object? cookAnswer = null) : IDialogService
    {
        public List<ViewModelBase> Shown { get; } = [];
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed;
        public int CloseCount;

        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            Shown.Add(dialog);
            Active = dialog;
            Changed?.Invoke();
            return Task.FromResult(dialog is CookDialogViewModel && cookAnswer is TResult t ? t : default);
        }

        public void Close(object? result) { CloseCount++; Active = null; Changed?.Invoke(); }
    }

    private sealed class StubPicker(string? path) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Landing(string? pick, FakeNav nav, RecordingDialogs dialogs, IParallelismSetting? threads = null)
    {
        var session = new CookBookSession();
        return new LandingViewModel(nav, dialogs, new StubPicker(pick),
            new RecentsService(Directory.CreateTempSubdirectory().FullName), session,
            book => new ExplorerViewModel(book, nav, dialogs, new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs), session,
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService()),
            set => new SetBrowserViewModel(set),
            (_, _, _) => null!, threads: threads);
    }

    [AvaloniaFact]
    public async Task Opening_a_book_from_landing_does_not_decode_on_the_ui_thread()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(tmp.FullName, "b.cbk");
            using (var b = ExplorerViewModelTests.TwoRecipeBook()) CookBookArchive.Write(path, b.Manifest, b.Recipes);

            var nav = new FakeNav();
            var dialogs = new RecordingDialogs();
            var vm = Landing(path, nav, dialogs);
            await vm.OpenCookBookCommand.ExecuteAsync(null);

            // The handler returned with the read still owed: it is a task the gesture started, and the
            // busy card went up for it. A synchronous read would show neither.
            Assert.NotNull(vm.PendingOpen);
            var busy = Assert.Single(dialogs.Shown.OfType<BusyViewModel>());
            Assert.Equal("Opening CookBook", busy.Title);

            await vm.PendingOpen!;
            Assert.IsType<ExplorerViewModel>(nav.Current);
            Assert.Equal(1, dialogs.CloseCount);              // the card came down
            Assert.Null(dialogs.Active);
        }
        finally { tmp.Delete(true); }
    }

    [AvaloniaFact]
    public async Task A_failed_open_closes_the_busy_card_and_shows_the_error()
    {
        var nav = new FakeNav();
        var dialogs = new RecordingDialogs();
        var vm = Landing("does-not-exist.cbk", nav, dialogs);
        await vm.OpenCookBookCommand.ExecuteAsync(null);
        await vm.PendingOpen!;

        Assert.Null(nav.Current);
        Assert.IsType<ErrorDialogViewModel>(dialogs.Shown.Last());
    }

    [AvaloniaFact]
    public async Task Opening_the_set_after_a_cook_goes_through_the_busy_card()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using (var source = ExplorerViewModelTests.TwoRecipeBook())
            using (var cooked = Generator.Generate(source, new GenerateOptions(2, "seed1")))
                SetWriter.Write(cooked, dir, pack: false);

            var nav = new FakeNav();
            var dialogs = new RecordingDialogs(cookAnswer: dir);
            var session = new CookBookSession();
            using var book = ExplorerViewModelTests.TwoRecipeBook();
            var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs), session,
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs),
                new StatusService(), setBrowserFactory: set => new SetBrowserViewModel(set));

            var card = Assert.IsType<CookBookDetailViewModel>(explorer.CurrentDetail);
            card.CookCommand.Execute(null);
            PumpUntil(() => nav.Current is SetBrowserViewModel);

            Assert.IsType<SetBrowserViewModel>(nav.Current);
            var busy = Assert.Single(dialogs.Shown.OfType<BusyViewModel>());
            Assert.Equal("Opening Set", busy.Title);
            Assert.Equal(1, dialogs.CloseCount);
            (nav.Current as IDisposable)?.Dispose();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- the cook dialog's scan ---

    /// <summary>Answers the scan only when told, and records the thread it ran on.</summary>
    private sealed class HeldScan
    {
        public readonly ManualResetEventSlim Release = new();
        public readonly ManualResetEventSlim Started = new();
        public int Calls;
        public int ThreadId;
        public Parallelism? Seen;

        public int Run(Nfty.Core.Formats.LoadedCookBook book, Parallelism p)
        {
            Interlocked.Increment(ref Calls);
            ThreadId = Environment.CurrentManagedThreadId;
            Seen = p;
            Started.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
            return 2;
        }
    }

    private sealed class FixedFolder(string dir) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(dir);
    }

    private sealed class NoReveal : IFolderRevealer { public void Reveal(string path) { } }

    private sealed class Threads(int degree) : IParallelismSetting
    {
        public int Degree = degree;
        public Parallelism Current => new(Degree);
        public bool IsAuto => false;
        public int? Saved => Degree;
        public string? EnvironmentText => null;
        public void Set(int? d) { }
        public event Action? Changed { add { } remove { } }
    }

    [AvaloniaFact]
    public async Task The_cook_dialog_constructor_does_not_scan_pixels()
    {
        var held = new HeldScan();
        using var book = CookDialogModelsTests.Book();
        var ctorThread = Environment.CurrentManagedThreadId;
        var vm = new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs(),
            scan: held.Run);
        try
        {
            // The constructor has RETURNED while the scan is still blocked: nothing waited on it, and
            // it did not run here.
            Assert.False(vm.ScanLanded);
            Assert.False(vm.BookIsTwoDOnly);
            Assert.True(held.Started.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(ctorThread, held.ThreadId);
        }
        finally { held.Release.Set(); await vm.ScanTask; }

        Assert.True(vm.ScanLanded);
        Assert.True(vm.BookIsTwoDOnly);
        Assert.Contains("2 variants have partly transparent pixels", vm.ModelsNote);
    }

    [AvaloniaFact]
    public async Task The_cook_button_waits_for_the_partial_alpha_scan()
    {
        var held = new HeldScan();
        using var book = CookDialogModelsTests.Book();
        var vm = new CookDialogViewModel(book, new FixedFolder("x"), new NoReveal(), new FakeDialogs(),
            scan: held.Run);
        Assert.False(vm.CookCommand.CanExecute(null));       // not offered while the answer is owed

        held.Release.Set();
        await vm.ScanTask;
        Assert.True(vm.CookCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Cook_waits_for_the_scan_even_when_the_button_is_bypassed()
    {
        // A generated command's ExecuteAsync does not consult CanExecute, so a disabled button is a
        // label: the gate that decides whether a model can be made has to be re-checked INSIDE.
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var held = new HeldScan();
            using var book = CookDialogModelsTests.Book();
            var vm = new CookDialogViewModel(book, new FixedFolder(dir), new NoReveal(), new FakeDialogs(),
                scan: held.Run);
            vm.Count = 1; vm.Seed = "s"; vm.Unlimited = true; vm.IncludeModels = true;

            var run = vm.CookCommand.ExecuteAsync(null);
            Assert.False(run.IsCompleted);
            Assert.False(vm.IsRunning);                      // nothing has started

            held.Release.Set();                              // the answer: 2D only
            await run;

            Assert.True(vm.IsDone);
            Assert.False(Directory.Exists(Path.Combine(dir, "models")),
                "the box was ticked, but the scan said the book cannot be modeled");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task The_scan_runs_at_the_threads_setting_and_a_cook_reads_it_when_it_starts()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var held = new HeldScan();
            var threads = new Threads(1);
            using var book = CookDialogModelsTests.Book(alpha: 255);
            var vm = new CookDialogViewModel(book, new FixedFolder(dir), new NoReveal(), new FakeDialogs(),
                threads, held.Run);
            held.Release.Set();
            await vm.ScanTask;
            Assert.Equal(new Parallelism(1), held.Seen);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // --- the thumbnail decode limit ---

    private static LoadedSet CookedSet(out string dir, int count)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using (var source = CookDialogModelsTests.Book(variants: 3))
        using (var cooked = Generator.Generate(source, new GenerateOptions(count, "t", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, dir, pack: false);
        return SetReader.Read(dir);
    }

    private static void PumpUntil(Func<bool> done, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task Thumbnail_decodes_never_exceed_the_thread_count()
    {
        var loaded = CookedSet(out var dir, count: 12);
        try
        {
            var gate = new ThumbnailGate(2);
            gate.Probe = () => Thread.Sleep(40);             // hold each decode open so others overlap
            var rows = loaded.Items.Select(i => new SetItemRow(i.Number, i.ImagePath, i, gate)).ToList();
            foreach (var r in rows) _ = r.Thumbnail;          // a screenful of tiles realized at once
            await Task.WhenAll(rows.Select(r => r.PendingDecode));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(12, rows.Sum(r => r.DecodesRun));
            Assert.Equal(2, gate.Peak);                       // reached the limit, never passed it
            foreach (var r in rows) r.Dispose();
        }
        finally { loaded.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async Task A_row_disposed_before_its_decode_starts_never_decodes()
    {
        var loaded = CookedSet(out var dir, count: 3);
        try
        {
            var gate = new ThumbnailGate(1);
            var row = new SetItemRow(loaded.Items[0].Number, loaded.Items[0].ImagePath, loaded.Items[0], gate);

            await gate.WaitAsync();                           // the only slot is taken: the row must queue
            _ = row.Thumbnail;
            row.Dispose();                                    // torn down while it waits
            gate.Release();
            await row.PendingDecode;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, row.DecodesRun);
            Assert.Equal(0, gate.Peak);
        }
        finally { loaded.Dispose(); Directory.Delete(dir, recursive: true); }
    }

}
