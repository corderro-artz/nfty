namespace Nfty.Core.Editing;

/// <summary>One reversible edit over an <see cref="IEditSurface{TPixel}"/>. Apply captures enough to
/// Undo.</summary>
/// <typeparam name="TPixel">The pixel the target surface stores.</typeparam>
public interface IEditCommand<TPixel> where TPixel : struct
{
    /// <summary>Applies the edit and returns whether it changed any pixel — a redundant edit
    /// (nothing to fill, or painting the value a pixel already holds) returns false so history can
    /// skip an empty entry. Re-applying (redo) reports the same verdict the first Apply did.</summary>
    /// <param name="surface">The surface to edit.</param>
    /// <returns>True when the edit changed anything.</returns>
    bool Apply(IEditSurface<TPixel> surface);

    /// <summary>Restores what <c>Apply</c> overwrote.</summary>
    /// <param name="surface">The surface to restore.</param>
    void Undo(IEditSurface<TPixel> surface);
}
