namespace Nfty.Core.Editing;

/// <summary>Undo/redo stack of reversible edit commands.</summary>
public sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Applies <paramref name="cmd"/> and records it for undo. A no-op edit (Apply reports
    /// no pixel changed) is not recorded and returns false, so CanUndo does not light for an edit
    /// that produced nothing.</summary>
    public bool Do(IEditCommand cmd, ValueMap map)
    {
        if (!cmd.Apply(map)) return false;
        _undo.Push(cmd);
        _redo.Clear();
        return true;
    }

    public void Undo(ValueMap map)
    {
        if (_undo.Count == 0) return;
        var cmd = _undo.Pop();
        cmd.Undo(map);
        _redo.Push(cmd);
    }

    public void Redo(ValueMap map)
    {
        if (_redo.Count == 0) return;
        var cmd = _redo.Pop();
        cmd.Apply(map);
        _undo.Push(cmd);
    }
}
