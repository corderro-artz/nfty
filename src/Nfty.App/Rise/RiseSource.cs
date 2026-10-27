using System.Globalization;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Rise;

/// <summary>What the Rise page is showing.</summary>
public enum RiseSourceKind
{
    /// <summary>The variants of one layer.</summary>
    Variant,

    /// <summary>One rolled recipe.</summary>
    Recipe,

    /// <summary>The assets of a cooked Set.</summary>
    Set,

    /// <summary>One loose picture.</summary>
    Picture,

    /// <summary>A loose spritesheet: its cells, in reading order.</summary>
    Sheet,
}

/// <summary>A picture that cannot rise, and how many of its pixels are partially transparent.</summary>
/// <param name="Name">The item's name.</param>
/// <param name="CountText">Pixel count as text, e.g. "1 px".</param>
public sealed record PartialAlphaRow(string Name, string CountText);

/// <summary>What making a source voxel-ready did.</summary>
/// <param name="Snapped">How many variants (or cells) had partial pixels snapped.</param>
/// <param name="Note">The line the slot shows afterwards, e.g. "Voxel-ready: 9 variants snapped".</param>
public sealed record VoxelReadyOutcome(int Snapped, string Note);

/// <summary>
/// Where the Rise page's items come from. The page talks to this and nothing else, so a new kind of
/// source (a loose picture, a sheet) is a new subclass, not a change to an existing one.
/// </summary>
public abstract class RiseSource : IDisposable
{
    private Task? _saveInFlight;

    /// <summary>The write running now, or null. A second save asks for it rather than starting its own.</summary>
    protected Task? SaveInFlight => _saveInFlight;

    /// <summary>
    /// Runs <paramref name="work"/> as THE write: one writer per destination. A second caller must not be
    /// told "saved" while the first is still writing, or about to fail, so it gets the first's task and
    /// its outcome. The write is published as in flight BEFORE <paramref name="work"/> starts, so a save
    /// that runs partly synchronously (or re-enters from a handler) is already visible as running.
    /// </summary>
    /// <param name="work">The write.</param>
    /// <param name="after">Runs when the write ends, success or failure, before its task completes.</param>
    /// <returns>The write's task, which faults with the write's own exception.</returns>
    protected Task SingleFlight(Func<Task> work, Action? after = null)
    {
        if (_saveInFlight is { } running) return running;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _saveInFlight = done.Task;
        _ = RunAsync(work, after, done);
        return done.Task;
    }

    private async Task RunAsync(Func<Task> work, Action? after, TaskCompletionSource done)
    {
        Exception? failure = null;
        try { await work(); }
        catch (Exception ex) { failure = ex; }
        _saveInFlight = null;
        after?.Invoke();
        if (failure is null) done.SetResult(); else done.SetException(failure);
    }

    /// <summary>Which kind of source this is.</summary>
    public abstract RiseSourceKind Kind { get; }

    /// <summary>Left header text, e.g. "BODY · VARIANTS".</summary>
    public abstract string Kicker { get; }

    /// <summary>What is being risen, e.g. "Body / Oak"; used in messages and the leave card.</summary>
    public abstract string Title { get; }

    /// <summary>The rail's rows.</summary>
    public abstract IReadOnlyList<RiseItem> Items { get; }

    /// <summary>The row that opens on the canvas.</summary>
    public abstract RiseItem InitialItem { get; }

    /// <summary>Chips under the header.</summary>
    public abstract IReadOnlyList<string> Facts { get; }

    /// <summary>Whether nothing in this source can rise: all of it is flat 2D art.</summary>
    public virtual bool IsTwoDOnly => false;

    /// <summary>Whether depth may be painted and saved.</summary>
    public virtual bool IsEditable => false;

    /// <summary>Editable, but the edit lock is closed: painting is scratch and Save writes nothing.</summary>
    public virtual bool IsLocked => false;

    /// <summary>Whether several items can be exported at once.</summary>
    public virtual bool CanBatch => false;

    /// <summary>Why nothing may be exported, or null.</summary>
    public virtual string? ExportRefusal => null;

    /// <summary>Whether any item has depth that is not saved yet.</summary>
    public virtual bool IsDirty => Items.Any(i => i.IsDirty);

    /// <summary>How many pieces of unsaved depth there are (variants, pictures): what a save would write.</summary>
    public virtual int DirtyCount => Items.Count(i => i.IsDirty);

    /// <summary>What the page paints this subject's depth through. The same instance until the subject
    /// changes.</summary>
    /// <param name="subject">A subject of this source.</param>
    /// <returns>The editor.</returns>
    public virtual IDepthEditor EditorFor(RiseSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return _editors.GetValue(subject, s => new SubjectDepthEditor(s));
    }

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<RiseSubject, IDepthEditor> _editors = new();

    /// <summary>Raised after a save into a CookBook, with the book the session now holds. The Explorer,
    /// the recents and the open editor learn the new depth from this.</summary>
    public event Action<LoadedCookBook>? Saved;

    /// <summary>Raises <see cref="Saved"/>.</summary>
    /// <param name="book">The book the session now holds.</param>
    protected void RaiseSaved(LoadedCookBook book) => Saved?.Invoke(book);

    /// <summary>Whether <see cref="SaveAsync"/> would write something: editable and not locked.</summary>
    public virtual bool CanSave => IsEditable && !IsLocked;

    /// <summary>Why an editable, unlocked source still cannot save (its footer says so), or null.</summary>
    public virtual string? UnsavableNote => null;

    /// <summary>What a save writes into, for button labels and the leave card ("CookBook", a file name).</summary>
    public virtual string SaveTargetName => "";

    /// <summary>The Save button's words ("Save depth", "Save as .igt…").</summary>
    public virtual string SaveLabel => "Save depth";

    /// <summary>What the last successful save said it did (the status bar repeats it), or null.</summary>
    public virtual string? SaveResultNote => null;

    /// <summary>Stem of a batch export's folder or zip.</summary>
    public abstract string BatchStem { get; }

    /// <summary>Text of the read-only pill, or null for none.</summary>
    public virtual string? ReadOnlyPill => null;

    /// <summary>The explanation under the depth tools.</summary>
    public abstract string DepthNote { get; }

    /// <summary>What the status bar says when the page opens.</summary>
    public abstract string OpeningLine { get; }

    /// <summary>A note at the foot of the rail.</summary>
    public virtual string RailFootNote => "";

    /// <summary>Whether the rail foot's note is a read-only notice, drawn with the lock beside it. The
    /// icon keeps its place either way; only its ink changes.</summary>
    public virtual bool RailFootIsLock => true;

    /// <summary>Items known to have partial alpha (loaded now, or loaded once and let go), with how many pixels are partial.</summary>
    public virtual IReadOnlyList<PartialAlphaRow> PartialAlphaRows =>
        Items.Where(i => i.PartialCount > 0)
            .Select(i => new PartialAlphaRow(i.Name,
                string.Create(CultureInfo.InvariantCulture, $"{i.PartialCount:N0} px")))
            .ToList();

    /// <summary>Loads an item's subject if it is not loaded yet.</summary>
    /// <param name="item">One of <see cref="Items"/>.</param>
    /// <returns>The item's subject.</returns>
    public abstract RiseSubject Open(RiseItem item);

    /// <summary>Takes everything an export of these items reads. Called on the UI thread; what it
    /// returns is then safe to read from any thread, and nothing the page does afterwards reaches it.</summary>
    /// <param name="items">The items to export.</param>
    /// <returns>One entry per item, in order.</returns>
    /// <exception cref="InvalidOperationException">Nothing may leave this source (<see cref="ExportRefusal"/>).</exception>
    public virtual RiseExportSnapshot SnapshotExport(IReadOnlyList<RiseItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        // Refused here as well as where the button is enabled: a disabled button is a label.
        if (ExportRefusal is { } refusal) throw new InvalidOperationException(refusal);
        return new RiseExportSnapshot(items.Select(i =>
        {
            var s = Open(i);
            return RiseExportSnapshot.Frozen(s.Art, s.Depth.ToDepthMap(), s.Faces);
        }).ToList());
    }

    /// <summary>Completes when every item's depth is final, so an export taken after it is the one the
    /// page will go on showing (a Set is still looking for the book that cooked it). Never faults.</summary>
    public virtual Task ExportReady => Task.CompletedTask;

    /// <summary>The page has moved off an item; a source that can cheaply reload it may let its subject
    /// go. Called after the page has adopted the next item, so nothing still reads the old picture.</summary>
    /// <param name="item">The item the page left.</param>
    public virtual void Release(RiseItem item) { }

    /// <summary>Writes the painted depth to where it came from.</summary>
    /// <returns>A task that completes when it is written. A save already running is not started twice:
    /// the caller gets that save's task, so it waits for, and sees the failure of, the real write. A
    /// source that refuses (locked) returns a completed task, which is why callers check
    /// <see cref="CanSave"/> and say so rather than treating completion as proof of a write.</returns>
    public virtual Task SaveAsync() => throw new NotSupportedException("This source is read-only.");

    // ---------------------------------------------------------------- voxel-ready

    /// <summary>Whether this source has art that <see cref="MakeVoxelReadyAsync"/> would snap: it is a
    /// layer or a picture, and some pixel is partially transparent.</summary>
    public virtual bool CanMakeVoxelReady => false;

    /// <summary>Whether a voxel-ready made through this source can still be undone.</summary>
    public virtual bool CanUndoVoxelReady => false;

    /// <summary>Whether a voxel-ready snap exists only in this session, so leaving would lose it. False
    /// for a source that writes its snap where the art lives. It counts as unsaved work beside unsaved
    /// depth: it enables Save and the leave card asks about it.</summary>
    public virtual bool HasUnsavedSnap => false;

    /// <summary>What the last voxel-ready did ("Voxel-ready: 9 variants snapped"), or null.</summary>
    public virtual string? VoxelReadyNote => null;

    /// <summary>Lists what <see cref="MakeVoxelReadyAsync"/> would change. Writes nothing.</summary>
    /// <returns>One row per variant (or cell) with partial pixels.</returns>
    public virtual IReadOnlyList<VoxelReadyChange> PlanVoxelReady() => Array.Empty<VoxelReadyChange>();

    /// <summary>The pictures of one planned change, for the preview thumbnails.</summary>
    /// <param name="change">One of <see cref="PlanVoxelReady"/>.</param>
    /// <returns>Before and after, both owned by the caller.</returns>
    public virtual (Image<Rgba32> Before, Image<Rgba32> After) PreviewVoxelReady(VoxelReadyChange change) =>
        throw new NotSupportedException("This source cannot be made voxel-ready.");

    /// <summary>Snaps every partial pixel to solid or clear and writes it where the art lives.</summary>
    /// <returns>What it did, or null when it refused (locked, unsaved depth, nothing to snap, already running).</returns>
    public virtual Task<VoxelReadyOutcome?> MakeVoxelReadyAsync() => Task.FromResult<VoxelReadyOutcome?>(null);

    /// <summary>Puts back the pixels the last <see cref="MakeVoxelReadyAsync"/> replaced.</summary>
    /// <returns>A task that completes when it is written; it faults with the reason when an undo is no longer safe.</returns>
    public virtual Task UndoVoxelReadyAsync() => Task.CompletedTask;

    /// <summary>Raised when something the page shows has changed.</summary>
    public event Action? Changed;

    /// <summary>Tells the page the edit lock flipped. <see cref="IsLocked"/> is read from the lock's owner
    /// on demand and nothing pushes it, so whoever owns the lock (the Explorer, through the factory
    /// that builds the page) calls this when it changes: the Save button, the footer and the depth
    /// note then refresh at once instead of at the next edit.</summary>
    public void NotifyLockChanged() => RaiseChanged();

    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void RaiseChanged() => Changed?.Invoke();

    /// <inheritdoc />
    public virtual void Dispose()
    {
        foreach (var item in Items) item.Dispose();
    }
}
