namespace Nfty.Model.Tests;

/// <summary>The fixture shapes and depth patterns the spec's Testing section names.</summary>
internal static class Shapes
{
    public static readonly IReadOnlyDictionary<string, string[]> All = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["single"] = ["#"],
        ["strip"] = ["##"],
        ["square"] = ["###", "###", "###"],
        ["plus"] = [".#.", "###", ".#."],
        ["L"] = ["#..", "#..", "###"],
        ["hole"] = ["###", "#.#", "###"],
        ["checkerboard"] = ["#.#", ".#.", "#.#"],
    };

    public static readonly IReadOnlyDictionary<string, Func<int, int, (int Front, int Back)>> Patterns =
        new Dictionary<string, Func<int, int, (int Front, int Back)>>(StringComparer.Ordinal)
        {
            ["paper"] = (_, _) => (0, 0),
            ["front"] = (_, _) => (2, 0),
            ["back"] = (_, _) => (0, 3),
            ["both"] = (_, _) => (1, 2),
            // Neighbors differing on BOTH sides: a raised column beside a paper one exposes a wall
            // toward the viewer AND one away from them, so its seam wall splits in two.
            ["alternate"] = (x, y) => (x + y) % 2 == 0 ? (2, 2) : (0, 0),
            ["staircase"] = (x, y) => (x, y),
        };

    /// <summary>Patterns under which every column has the same interval.</summary>
    public static readonly string[] Uniform = ["paper", "front", "back", "both"];

    public static TheoryData<string, string> EveryShapeAndPattern()
    {
        var data = new TheoryData<string, string>();
        foreach (string shape in All.Keys)
            foreach (string pattern in Patterns.Keys)
                data.Add(shape, pattern);
        return data;
    }

    public static TheoryData<string, string> UnpinchedShapesUnderUniformDepth()
    {
        var data = new TheoryData<string, string>();
        foreach (string shape in All.Keys.Where(s => s != "checkerboard"))
            foreach (string pattern in Uniform)
                data.Add(shape, pattern);
        return data;
    }

    /// <summary>The solid's exact volume in voxels: one column of 1 + front + back per solid pixel.</summary>
    public static int Voxels(string shape, string pattern)
    {
        var rows = All[shape];
        int total = 0;
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '#')
                {
                    var (front, back) = Patterns[pattern](x, y);
                    total += 1 + front + back;
                }
        return total;
    }
}
