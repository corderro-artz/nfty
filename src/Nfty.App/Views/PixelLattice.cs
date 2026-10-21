using Avalonia;
using Avalonia.Media;

namespace Nfty.App.Views;

/// <summary>
/// The lattice of an art's own pixels, as a tiling brush: squares <c>cell</c> control pixels wide,
/// phased so a square boundary falls exactly on a pixel boundary of the art.
/// </summary>
/// <remarks>The arithmetic is the Ingredient Editor's, shared with the Rise page. The phase is the
/// point, not the size: a brush of the right size tiled from the wrong origin is the same bug with
/// better arithmetic.</remarks>
internal static class PixelLattice
{
    /// <summary>Builds the checker brush.</summary>
    /// <param name="cell">One square's side in control pixels.</param>
    /// <param name="artX">The art's left edge, in the painted control's coordinates.</param>
    /// <param name="artY">The art's top edge, in the painted control's coordinates.</param>
    /// <param name="light">The ground.</param>
    /// <param name="dark">The two squares per tile.</param>
    /// <returns>The brush, or null when a square is under two device pixels — half a lattice that fine
    /// averages to a flat tone and the other half aliases into a moire, so the caller draws a flat
    /// ground, which is what it would have averaged to.</returns>
    internal static DrawingBrush? Build(double cell, double artX, double artY, IBrush light, IBrush dark)
    {
        if (cell < 2) return null;

        double tile = cell * 2;
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing
        {
            Brush = light,
            Geometry = new RectangleGeometry(new Rect(0, 0, tile, tile)),
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = dark,
            Geometry = new RectangleGeometry(new Rect(0, 0, cell, cell)),
        });
        group.Children.Add(new GeometryDrawing
        {
            Brush = dark,
            Geometry = new RectangleGeometry(new Rect(cell, cell, cell, cell)),
        });

        // The modulo keeps the start on the panel (a negative DestinationRect is not honoured) while
        // leaving the corner on a tile boundary, which is all "in phase" means.
        double startX = Mod(artX, tile);
        double startY = Mod(artY, tile);

        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            SourceRect = new RelativeRect(0, 0, tile, tile, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(startX, startY, tile, tile, RelativeUnit.Absolute),
            Stretch = Stretch.None,
        };
    }

    /// <summary>A modulo that returns a non-negative remainder — C#'s <c>%</c> keeps the dividend's
    /// sign, and the art can sit at a negative offset in a pane narrower than the tile.</summary>
    private static double Mod(double value, double by)
    {
        double r = value % by;
        return r < 0 ? r + by : r;
    }
}
