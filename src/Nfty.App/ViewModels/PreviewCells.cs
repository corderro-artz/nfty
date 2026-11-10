using Nfty.Core.Publish;
using SixLabors.ImageSharp;

namespace Nfty.App.ViewModels;

/// <summary>Where a preview sheet's cells are, trusting the grid it carries only as far as the picture backs it up.</summary>
internal static class PreviewCells
{
    /// <summary>How many cells the preview shows.</summary>
    /// <param name="grid">The grid the picture carries, or null.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <returns>The grid's count when the grid fits the picture, else 1 (the whole picture).</returns>
    public static int Count(SheetGrid? grid, int width, int height) =>
        grid is not null && grid.FitsWithin(width, height) ? Math.Max(1, grid.Count) : 1;

    /// <summary>The rectangle of one cell, always inside the picture and never empty.</summary>
    /// <param name="grid">The grid the picture carries, or null.</param>
    /// <param name="index">The cell, from 0.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <returns>The cell's rectangle; the whole picture when there is no believable grid.</returns>
    /// <remarks>The grid is a chunk of the downloaded file, so a hostile or damaged one must not make a crop fall outside the picture.</remarks>
    public static Rectangle Rect(SheetGrid? grid, int index, int width, int height)
    {
        var whole = new Rectangle(0, 0, width, height);
        if (grid is null || !grid.FitsWithin(width, height) || index < 0 || index >= Math.Max(1, grid.Count)) return whole;
        int col = index % grid.Columns, row = index / grid.Columns;
        var cell = Rectangle.Intersect(new Rectangle(col * grid.CellWidth, row * grid.CellHeight, grid.CellWidth, grid.CellHeight), whole);
        return cell.Width > 0 && cell.Height > 0 ? cell : whole;
    }
}
