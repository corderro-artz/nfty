namespace Nfty.Core.Editing;

/// <summary>Undo/redo stack of reversible edit commands over one surface.</summary>
/// <typeparam name="TPixel">The pixel that surface stores.</typeparam>
public sealed class EditHistory<TPixel> where TPixel : struct
{
    private readonly Stack<IEditCommand<TPixel>> _undo = new();
    private readonly Stack<IEditCommand<TPixel>> _redo = new();

    /// <summary>Whether there is an applied command to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Whether an undone command is available to reapply.</summary>
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Applies <paramref name="cmd"/> and records it for undo. A no-op edit (Apply reports
    /// no pixel changed) is not recorded and returns false, so CanUndo does not light for an edit
    /// that produced nothing.</summary>
    /// <param name="cmd">The edit to apply.</param>
    /// <param name="surface">The surface to edit.</param>
    /// <returns>True when the edit changed something and was recorded.</returns>
    public bool Do(IEditCommand<TPixel> cmd, IEditSurface<TPixel> surface)
    {
        if (!cmd.Apply(surface)) return false;
        _undo.Push(cmd);
        _redo.Clear();
        return true;
    }

    /// <summary>Undoes the most recent command.</summary>
    /// <param name="surface">The surface to restore.</param>
    public void Undo(IEditSurface<TPixel> surface)
    {
        if (_undo.Count == 0) return;
        var cmd = _undo.Pop();
        cmd.Undo(surface);
        _redo.Push(cmd);
    }

    /// <summary>Reapplies the most recently undone command.</summary>
    /// <param name="surface">The surface to edit.</param>
    public void Redo(IEditSurface<TPixel> surface)
    {
        if (_redo.Count == 0) return;
        var cmd = _redo.Pop();
        cmd.Apply(surface);
        _undo.Push(cmd);
    }
}
