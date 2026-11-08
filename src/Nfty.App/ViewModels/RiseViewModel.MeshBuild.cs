using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Nfty.App.Rise;
using Nfty.Core.Diagnostics;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

/// <remarks>
/// <para><b>Snapshot, never borrow.</b> The art and the faces are cloned and the depth copied before the
/// build starts: the surface keeps changing under a drag, a recipe's faces are rewritten in place by its
/// recombine, and a reroll or a close disposes the subject. The build owns its copies and frees them.</para>
/// <para><b>Latest wins, in order.</b> At most one build runs and one waits. A request made while one runs
/// REPLACES the waiting one (the replaced snapshot is freed, never built). Results land in request order,
/// because builds run one after another, and a result is applied only when it is newer than what is shown,
/// so a superseded model can never be put back over a newer one. The build that finishes after the last
/// request is the last request's, so a release always lands the exact model.</para>
/// <para><b>A new subject is built at once.</b> Opening the page and moving to another item (or a reroll)
/// build on the UI thread, as before: the camera is framed on the new model's box the moment the page says
/// the subject changed, and an old model must never be drawn over a new texture. Anything still running
/// for the old subject is dropped when it lands.</para>
/// <para><b>Closing is safe.</b> A build in flight reads only its own copies; when it lands on a closed
/// page it is dropped. <see cref="PendingMeshBuild"/> is the handle a test awaits.</para>
/// </remarks>
public sealed partial class RiseViewModel
{
    /// <summary>What one build reads, owned by the build.</summary>
    private sealed class MeshJob(long seq, Image<Rgba32> art, DepthMap? depth, StackFaces? faces, string title) : IDisposable
    {
        public long Seq { get; } = seq;
        public Image<Rgba32> Art { get; } = art;
        public DepthMap? Depth { get; } = depth;
        public StackFaces? Faces { get; } = faces;
        public string Title { get; } = title;

        public void Dispose()
        {
            Art.Dispose();
            Faces?.Dispose();
        }
    }

    private long _meshSeq;          // the last request's number
    private long _meshShown;        // the request whose result is on screen (or that is dropped below)
    private bool _meshRunning;
    private MeshJob? _meshWaiting;
    private TaskCompletionSource? _meshIdle;
    private Task _meshWorker = Task.CompletedTask;

    /// <summary>The actual worker, including freeing its snapshots and queuing its result.</summary>
    internal Task PendingMeshWorker => _meshWorker;

    /// <summary>Completes when no mesh build is running or waiting and the last one requested is on screen.
    /// A test awaits it (through <c>UiThreadWait.For</c>, which pumps the dispatcher the result is posted to);
    /// it faults when that build threw.</summary>
    public Task PendingMeshBuild => _meshIdle?.Task ?? Task.CompletedTask;

    /// <summary>Awaited on the worker before each build; a test holds builds back with it to make them pile up.</summary>
    internal Func<Task>? BeforeMeshBuild { get; set; }

    /// <summary>Called on the UI thread with the request number of every result that is applied; a test
    /// checks the order with it.</summary>
    internal Action<long>? MeshApplied { get; set; }

    /// <summary>How many builds actually ran (a replaced waiting request never does).</summary>
    internal int MeshBuildsRun { get; private set; }

    /// <summary>The last request's number; for tests.</summary>
    internal long MeshRequested => _meshSeq;

    /// <summary>Asks for the model of the subject as it is now: built off the UI thread, applied when it lands.</summary>
    private void RequestMesh()
    {
        var subject = Subject;
        long seq = ++_meshSeq;
        if (!subject.IsRisable)
        {
            // Nothing to build: the answer is known now, and anything still running is older than it.
            DropWaiting();
            ApplyMesh(null, seq);
            return;
        }
        MeshJob job;
        using (Perf.Measure("Rise.MeshSnapshot"))
            job = new MeshJob(seq, subject.Art.Clone(), subject.Depth.ToDepthMap(), subject.Faces?.Clone(), Source.Title);
        if (_meshRunning)
        {
            DropWaiting();
            _meshWaiting = job;
            return;
        }
        Start(job);
    }

    /// <summary>Builds the subject's model on the UI thread, now, and drops whatever is still running or waiting.</summary>
    private void BuildMeshNow()
    {
        DropWaiting();
        long seq = ++_meshSeq;
        ApplyMesh(BuildMesh(), seq);
    }

    private void DropWaiting()
    {
        _meshWaiting?.Dispose();
        _meshWaiting = null;
    }

    private void Start(MeshJob job)
    {
        _meshRunning = true;
        if (_meshIdle is null || _meshIdle.Task.IsCompleted)
            _meshIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = BeforeMeshBuild;
        var dispatcher = Dispatcher.UIThread;
        _meshWorker = Task.Run(async () =>
        {
            Mesh? mesh = null;
            ExceptionDispatchInfo? error = null;
            try
            {
                if (gate is not null) await gate().ConfigureAwait(false);
                using (Perf.Measure("Rise.MeshRebuild"))
                    mesh = Riser.TryBuild(job.Art, job.Depth, new RiseOptions(), job.Title, job.Faces);
            }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
            finally { job.Dispose(); }
            if (Volatile.Read(ref _disposed))
            {
                // No UI lifetime remains to drain a result. The job's snapshots are already freed;
                // settle its handle here rather than asking another application's dispatcher.
                MeshBuildsRun++;
                _meshIdle?.TrySetResult();
            }
            else dispatcher.Post(() => Landed(job.Seq, mesh, error));
        });
    }

    private void CompleteDisposedMesh()
    {
        if (_meshIdle is not { } idle || idle.Task.IsCompleted) return;
        // A result may already be queued when Dispose runs. Its old UI queue need never drain;
        // completion still waits for the worker's finally (which frees every snapshot).
        _ = _meshWorker.ContinueWith(worker =>
        {
            if (worker.IsFaulted) idle.TrySetException(worker.Exception.InnerExceptions);
            else idle.TrySetResult();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>A build finished: apply it if it is still the newest answer, then start the waiting one.</summary>
    private void Landed(long seq, Mesh? mesh, ExceptionDispatchInfo? error)
    {
        _meshRunning = false;
        MeshBuildsRun++;
        if (_disposed)
        {
            DropWaiting();
            _meshIdle?.TrySetResult();
            return;
        }
        if (seq > _meshShown)
        {
            if (error is null) ApplyMesh(mesh, seq);
            else
            {
                // No model we can stand behind: the tile says so (no mesh) rather than showing a stale one,
                // and the handle carries the failure to whoever waits on it.
                ApplyMesh(null, seq);
                if (_meshWaiting is null) { _meshIdle?.TrySetException(error.SourceException); return; }
            }
        }
        if (_meshWaiting is { } next)
        {
            _meshWaiting = null;
            Start(next);
        }
        else _meshIdle?.TrySetResult();
    }

    private void ApplyMesh(Mesh? mesh, long seq)
    {
        _meshShown = seq;
        Mesh = mesh;
        if (mesh is { } m) _preview?.SetSubject(m.Min, m.Max);
        MeshApplied?.Invoke(seq);
        ViewerChanged?.Invoke();
    }
}
