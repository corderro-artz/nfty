using System.Collections.Concurrent;
using Nfty.Core.Formats;
using Nfty.Core.Output;
using Nfty.Core.Publish;

namespace Nfty.Core.Tests;

/// <summary>
/// Core is a library: it must never resume on whoever called it. A UI caller would otherwise get PNG
/// codec work back on its own thread, and a sync twin that blocks on its async one would deadlock
/// against a caller whose context is single-threaded.
/// </summary>
public class NoContextCaptureTests
{
    /// <summary>
    /// A synchronization context that runs everything on one dedicated thread, the way a UI thread
    /// does. A continuation posted here while that thread is blocked waiting for it never runs.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public SingleThreadContext()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "single-threaded context" };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        private void Pump()
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }

        /// <summary>Runs <paramref name="work"/> on the context thread and returns its result.</summary>
        public Task<T> Run<T>(Func<T> work)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(_ =>
            {
                try { done.SetResult(work()); }
                catch (Exception ex) { done.SetException(ex); }
            }, null);
            return done.Task;
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("SetExporter.Export with a watermark and a sheet")]
    [InlineData("MenuWriter.Write")]
    [InlineData("SpriteSheet.Write")]
    [InlineData("CookBookArchive Read then Write")]
    public async Task The_sync_twins_do_not_deadlock_under_a_single_threaded_context(string twin)
    {
        string setDir = SpriteSheetFixture.Cook(3, canvas: 16);
        string outDir = Directory.CreateTempSubdirectory("nfty-nocapture-").FullName;
        try
        {
            using var context = new SingleThreadContext();
            Func<int> call = twin switch
            {
                "SetExporter.Export with a watermark and a sheet" => () =>
                {
                    var options = new ExportOptions { NumberWatermark = true, SpriteSheet = true };
                    return SetExporter.Export(setDir, Path.Combine(outDir, "export"), options)
                        .Plan.Entries.Count;
                },
                "MenuWriter.Write" => () =>
                {
                    using var book = SpriteSheetTests.Book(3, 4);
                    MenuWriter.Write(book, Path.Combine(outDir, "book.mnu"), new MenuWriteOptions());
                    return 0;
                },
                "SpriteSheet.Write" => () =>
                {
                    using var set = SetReader.Read(setDir);
                    SpriteSheet.Write(set.Items, SpriteSheet.Fit(3, 16, 16), Path.Combine(outDir, "sheet.png"));
                    return 0;
                },
                _ => () =>
                {
                    using var book = SpriteSheetTests.Book(3, 4);
                    string cbk = Path.Combine(outDir, "book.cbk");
                    CookBookArchive.Write(cbk, book.Manifest, book.Recipes);
                    using var again = CookBookArchive.Read(cbk);
                    CookBookArchive.Write(Path.Combine(outDir, "again.cbk"), again.Manifest, again.Recipes);
                    return 0;
                },
            };

            // A timeout here is the deadlock: the call blocked the context thread on a task whose
            // continuation was posted back to that same thread.
            await context.Run(call).WaitAsync(Deadline);
        }
        finally
        {
            Directory.Delete(setDir, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    /// <summary>Holds the export at its first progress report until the test opens the gate, and
    /// remembers which thread made that report.</summary>
    private sealed class GatedProgress : IProgress<ExportProgress>
    {
        private int _reports;
        public ManualResetEventSlim Gate { get; } = new();
        public int ReportingThread { get; private set; } = -1;
        public int Reports => Volatile.Read(ref _reports);

        public void Report(ExportProgress value)
        {
            if (Interlocked.Increment(ref _reports) != 1) return;
            ReportingThread = Environment.CurrentManagedThreadId;
            Gate.Wait(Deadline);
        }
    }

    [Fact]
    public async Task Export_returns_control_to_the_caller_before_doing_any_work()
    {
        // The export is held at its first progress report until the call has returned. Checking
        // IsCompleted or the output folder against a free-running export raced: once it yields, it runs
        // on the pool, and a two-asset export could finish before the caller looked.
        string setDir = SpriteSheetFixture.Cook(2);
        string outDir = Path.Combine(Directory.CreateTempSubdirectory("nfty-yield-").FullName, "export");
        var progress = new GatedProgress();
        try
        {
            using var context = new SingleThreadContext();
            // No watermark and no sheet: nothing in the export awaits real I/O before its sync tail,
            // so without an explicit yield the whole export runs inside this call - and the gate
            // would hold the caller's own thread until the deadline.
            var (task, returnedCompleted) = await context.Run(() =>
            {
                var started = SetExporter.ExportAsync(setDir, outDir, new ExportOptions(), progress: progress);
                return (started, started.IsCompleted);
            }).WaitAsync(Deadline);
            progress.Gate.Set();

            Assert.False(returnedCompleted);
            var result = await task.WaitAsync(Deadline);
            Assert.True(progress.Reports > 0, "the export reported no progress, so the gate held nothing");
            Assert.NotEqual(context.ThreadId, progress.ReportingThread);
            Assert.True(Directory.Exists(outDir));
            Assert.NotEmpty(result.Plan.Entries);
        }
        finally
        {
            progress.Gate.Set();
            Directory.Delete(setDir, recursive: true);
            Directory.Delete(Path.GetDirectoryName(outDir)!, recursive: true);
        }
    }
}
