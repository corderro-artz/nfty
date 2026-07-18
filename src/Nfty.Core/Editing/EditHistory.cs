namespace Nfty.Core.Editing;

/// <summary>Undo/redo stack of reversible edit commands.</summary>
public sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Do(IEditCommand cmd, ValueMap map)
    {
        cmd.Apply(map);
        _undo.Push(cmd);
        _redo.Clear();
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
