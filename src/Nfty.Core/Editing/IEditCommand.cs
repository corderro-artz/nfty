namespace Nfty.Core.Editing;

/// <summary>One reversible edit over a <see cref="ValueMap"/>. Apply captures enough to Undo.</summary>
public interface IEditCommand
{
    void Apply(ValueMap map);
    void Undo(ValueMap map);
}
