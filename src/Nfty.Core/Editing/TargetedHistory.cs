namespace Nfty.Core.Editing;

/// <summary>
/// One undo/redo history across many surfaces: each step remembers WHICH surface it edited, so
/// undoing restores that surface and says which one it was.
/// </summary>
/// <remarks>
/// The same contract as <see cref="EditHistory{TPixel}"/> (a no-op edit is not recorded, a new edit
/// clears redo), keyed. The caller resolves a key to its surface at undo time, so surfaces may be
/// created lazily and outlive whatever the page is currently showing.
/// </remarks>
/// <typeparam name="TKey">Identifies a surface.</typeparam>
/// <typeparam name="TPixel">The pixel the surfaces store.</typeparam>
public sealed class TargetedHistory<TKey, TPixel>
    where TKey : notnull
    where TPixel : struct
{
    private readonly Stack<(TKey Key, IEditCommand<TPixel> Cmd)> _undo = new();
    private readonly Stack<(TKey Key, IEditCommand<TPixel> Cmd)> _redo = new();

    /// <summary>Whether there is an applied step to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether an undone step is available to reapply.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>The surface the next undo would touch, or default when there is none.</summary>
    public TKey? PeekUndo => _undo.Count > 0 ? _undo.Peek().Key : default;

    /// <summary>The surface the next redo would touch, or default when there is none.</summary>
    public TKey? PeekRedo => _redo.Count > 0 ? _redo.Peek().Key : default;

    /// <summary>Applies a command to a surface and records it. A no-op edit is not recorded.</summary>
    /// <param name="key">The surface's key.</param>
    /// <param name="cmd">The edit.</param>
    /// <param name="surface">The surface to edit.</param>
    /// <returns>True when the edit changed something and was recorded.</returns>
    public bool Do(TKey key, IEditCommand<TPixel> cmd, IEditSurface<TPixel> surface)
    {
        if (!cmd.Apply(surface)) return false;
        _undo.Push((key, cmd));
        _redo.Clear();
        return true;
    }

    /// <summary>Undoes the most recent step on the surface it touched.</summary>
    /// <param name="surfaceOf">Resolves a key to its surface.</param>
    /// <param name="key">The surface that was restored.</param>
    /// <returns>False when there was nothing to undo.</returns>
    public bool Undo(Func<TKey, IEditSurface<TPixel>> surfaceOf, out TKey key)
    {
        ArgumentNullException.ThrowIfNull(surfaceOf);
        if (_undo.Count == 0) { key = default!; return false; }
        var (k, cmd) = _undo.Pop();
        cmd.Undo(surfaceOf(k));
        _redo.Push((k, cmd));
        key = k;
        return true;
    }

    /// <summary>Reapplies the most recently undone step on the surface it touched.</summary>
    /// <param name="surfaceOf">Resolves a key to its surface.</param>
    /// <param name="key">The surface that was edited.</param>
    /// <returns>False when there was nothing to redo.</returns>
    public bool Redo(Func<TKey, IEditSurface<TPixel>> surfaceOf, out TKey key)
    {
        ArgumentNullException.ThrowIfNull(surfaceOf);
        if (_redo.Count == 0) { key = default!; return false; }
        var (k, cmd) = _redo.Pop();
        cmd.Apply(surfaceOf(k));
        _undo.Push((k, cmd));
        key = k;
        return true;
    }

    /// <summary>Forgets every step.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
