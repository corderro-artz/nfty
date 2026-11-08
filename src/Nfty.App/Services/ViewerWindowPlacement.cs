using Avalonia;

namespace Nfty.App.Services;

/// <summary>Where a viewer window opens: a remembered position clamped onto a screen that still
/// exists, or nothing, which the host reads as "center it".</summary>
public static class ViewerWindowPlacement
{
    // The part of the window, in screen pixels, that must still be on a screen for the position to count.
    private const int MinVisibleWidth = 120;
    private const int MinVisibleHeight = 60;

    /// <summary>Resolves a remembered position.</summary>
    /// <param name="entry">The remembered window.</param>
    /// <param name="workingAreas">The working area of every screen, in screen pixels.</param>
    /// <returns>The position to open at, shifted just far enough to sit inside the screen it
    /// overlaps most, or null when no remembered position survives (none was saved, or it is off
    /// every screen, as after unplugging a monitor).</returns>
    public static PixelPoint? Resolve(ViewerWindowEntry entry, IReadOnlyList<PixelRect> workingAreas)
        => Resolve(entry, workingAreas.Select(area => (area, 1.0)).ToArray());

    /// <summary>Resolves a remembered DIP size and pixel position against each screen's DPI.</summary>
    /// <param name="entry">The remembered window; sizes are DIP and positions are screen pixels.</param>
    /// <param name="screens">Each screen's working area in pixels and its pixels per DIP.</param>
    /// <returns>The clamped pixel position on the most-overlapped surviving screen, or null to center.</returns>
    public static PixelPoint? Resolve(ViewerWindowEntry entry,
        IReadOnlyList<(PixelRect WorkingArea, double Scaling)> screens)
    {
        if (entry.X is not { } x || entry.Y is not { } y) return null;

        PixelRect? best = null;
        PixelRect window = default;
        long bestArea = 0;
        foreach (var (area, scaling) in screens)
        {
            var candidate = new PixelRect((int)Math.Round(x), (int)Math.Round(y),
                (int)Math.Round(entry.Width * scaling), (int)Math.Round(entry.Height * scaling));
            var overlap = area.Intersect(candidate);
            if (overlap.Width < Math.Min(MinVisibleWidth, candidate.Width)
                || overlap.Height < Math.Min(MinVisibleHeight, candidate.Height)) continue;
            long size = (long)overlap.Width * overlap.Height;
            if (size > bestArea) { best = area; window = candidate; bestArea = size; }
        }
        if (best is not { } screen) return null;

        // Shifted inside the screen where it fits; a window larger than the screen keeps its corner
        // on the screen's corner.
        int cx = window.Width >= screen.Width ? screen.X : Math.Clamp(window.X, screen.X, screen.Right - window.Width);
        int cy = window.Height >= screen.Height ? screen.Y : Math.Clamp(window.Y, screen.Y, screen.Bottom - window.Height);
        return new PixelPoint(cx, cy);
    }
}
