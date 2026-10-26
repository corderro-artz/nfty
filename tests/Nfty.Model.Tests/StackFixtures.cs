using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>Stacks with two face owners, for the tests of the atlas and the seam-split walls.</summary>
internal static class StackFixtures
{
    public static readonly Rgba32 Red = new(200, 30, 30, 255);
    public static readonly Rgba32 Blue = new(30, 30, 200, 255);

    /// <summary>A solid-colored picture: every '#' of <paramref name="rows"/> takes <paramref name="color"/>.</summary>
    public static Image<Rgba32> Flat(Rgba32 color, params string[] rows)
    {
        var img = new Image<Rgba32>(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                img[x, y] = rows[y][x] == '#' ? color : new Rgba32(0, 0, 0, 0);
        return img;
    }

    /// <summary>A body (front 2, back 9) in red with a strap (front 4, back 0) in blue on its middle
    /// pixel: the strap owns the front, the body owns the back, and the seam is at z = −0.5.</summary>
    public static StackRise BodyAndStrap(params string[] bodyRows)
    {
        string[] strapRows = bodyRows.Select((r, y) => new string(r.Select((_, x) =>
            y == bodyRows.Length / 2 && x == r.Length / 2 ? '#' : '.').ToArray())).ToArray();
        using var body = Flat(Red, bodyRows);
        using var strap = Flat(Blue, strapRows);
        return StackDepth.Combine(
        [
            (body, Art.Depth(body, (_, _) => (2, 9))),
            (strap, Art.Depth(strap, (_, _) => (4, 0))),
        ]);
    }

    /// <summary>The strapped body over a plus-shaped silhouette.</summary>
    public static StackRise BodyAndStrapOverPlus() => BodyAndStrap(".#.", "###", ".#.");
}
