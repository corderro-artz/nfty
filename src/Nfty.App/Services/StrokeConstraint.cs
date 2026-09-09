using System;
using System.Collections.Generic;
using Nfty.App.ViewModels;

namespace Nfty.App.Services;

/// <summary>
/// The modifier conventions every drawing tool is expected to honor, as a transform on the gesture's
/// pixel path.
/// </summary>
/// <remarks>
/// <para>It is a transform on the PATH rather than a branch inside each tool because that is what
/// keeps the live preview and the commit in step: both ask for the same effective gesture and get
/// the same answer, so a constrained line cannot preview at one angle and commit at another. It
/// also means no edit command learns about the keyboard — <c>BuildCommand</c> already reduces a
/// line to its two ends, and <c>StampDiscs</c> already joins two points with Bresenham, so a
/// two-point path IS a straight stroke for the brush and the eraser too.</para>
/// <para><b>Shift is the constrain key everywhere in this class of software</b>, and Ctrl is
/// accepted for it as well. That is one rule with two keys rather than two rules: a user who
/// reaches for Ctrl is reaching for "make this straight", and refusing the key teaches nothing.
/// Alt draws a shape from its CENTER, which is the other half of the same convention.</para>
/// </remarks>
public static class StrokeConstraint
{
    // tan(22.5 degrees): the half-angle between horizontal and the 45-degree diagonal, so a drag is
    // snapped to whichever of the eight directions it is actually closest to. Comparing against a
    // simpler ratio (half, say) puts the boundary at 26.6 degrees and makes the diagonal harder to
    // hit than the axes, which reads as the diagonal "sticking".
    private const double Tan22 = 0.41421356237;

    /// <summary>The path the tools should act on, given which modifiers are held.</summary>
    /// <param name="tool">The active tool; each has its own idea of what constraining means.</param>
    /// <param name="constrain">Shift or Ctrl: 45° for a line or a move, a square bounding box for a shape.</param>
    /// <param name="fromCenter">Alt: the press point is the shape's center rather than a corner.</param>
    /// <param name="movingSelection">Whether a Select drag is moving the marquee rather than marking one.</param>
    /// <param name="points">The gesture's pixel path.</param>
    /// <param name="width">Canvas width, so a constrained endpoint stays on the canvas.</param>
    /// <param name="height">Canvas height.</param>
    /// <returns>The path to preview and to commit — the input itself when no modifier applies.</returns>
    public static IReadOnlyList<(int x, int y)> Apply(EditorTool tool, bool constrain, bool fromCenter,
        bool movingSelection, IReadOnlyList<(int x, int y)> points, int width, int height)
    {
        if (points.Count == 0) return points;
        // Fill acts on the press point alone, so there is nothing a modifier could constrain.
        if (tool == EditorTool.Fill) return points;

        bool boxed = tool is EditorTool.Rectangle or EditorTool.Circle or EditorTool.Triangle
            || (tool == EditorTool.Select && !movingSelection);
        bool centered = fromCenter && tool is EditorTool.Rectangle or EditorTool.Circle or EditorTool.Triangle;
        if (!constrain && !centered) return points;

        var a = points[0];
        var b = points[^1];
        int dx = b.x - a.x, dy = b.y - a.y;

        if (constrain) (dx, dy) = boxed ? Square(dx, dy) : Snap45(dx, dy);

        // From center: the press point is the middle, so the far corner keeps its offset and the
        // near one is reflected through it. Done after the constraint so a centered square is still
        // square.
        var start = centered ? (x: a.x - dx, y: a.y - dy) : a;
        var end = (x: a.x + dx, y: a.y + dy);

        return new[] { Clamp(start, width, height), Clamp(end, width, height) };
    }

    /// <summary>Snaps a delta to the nearest of the eight compass directions, keeping its length
    /// along the dominant axis so the stroke still reaches where the pointer is.</summary>
    private static (int dx, int dy) Snap45(int dx, int dy)
    {
        int adx = Math.Abs(dx), ady = Math.Abs(dy);
        if (ady <= adx * Tan22) return (dx, 0);
        if (adx <= ady * Tan22) return (0, dy);
        int m = Math.Max(adx, ady);
        return (Math.Sign(dx) * m, Math.Sign(dy) * m);
    }

    /// <summary>Squares a bounding box off its LARGER side, so the shape follows the pointer on the
    /// axis the hand actually moved along rather than shrinking to the smaller one.</summary>
    private static (int dx, int dy) Square(int dx, int dy)
    {
        int m = Math.Max(Math.Abs(dx), Math.Abs(dy));
        // A zero delta has no side to take a sign from; +1 keeps a one-pixel drag one pixel.
        return ((dx < 0 ? -1 : 1) * m, (dy < 0 ? -1 : 1) * m);
    }

    private static (int x, int y) Clamp((int x, int y) p, int width, int height) =>
        (Math.Clamp(p.x, 0, Math.Max(0, width - 1)), Math.Clamp(p.y, 0, Math.Max(0, height - 1)));
}
