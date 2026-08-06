namespace Nfty.Core.Editing;

/// <summary>One reversible edit over a <see cref="ValueMap"/>. Apply captures enough to Undo.</summary>
public interface IEditCommand
{
    /// <summary>Applies the edit and returns whether it changed any pixel — a redundant edit
    /// (nothing to fill, or painting the value a pixel already holds) returns false so history can
    /// skip an empty entry. Re-applying (redo) reports the same verdict the first Apply did.</summary>
    bool Apply(ValueMap map);
    /// <summary>Restores what <c>Apply</c> overwrote.</summary>
    /// <param name="map">The map to restore.</param>
    void Undo(ValueMap map);
}
