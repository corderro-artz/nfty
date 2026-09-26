using Nfty.Core.Model;

namespace Nfty.Model.Tests;

/// <summary>Rise-and-write in one call, for the writer tests.</summary>
internal static class Models
{
    public static IReadOnlyList<ModelFile> Write(string[] rows, Func<int, int, (int Front, int Back)>? depth = null,
        RiseOptions? options = null, string baseName = "sprite")
    {
        options ??= new RiseOptions();
        using var art = Art.FromAscii(rows);
        DepthMap? map = depth is null ? null : Art.Depth(art, depth);
        var mesh = Riser.TryBuild(art, map, options) ?? throw new InvalidOperationException("nothing solid");
        return ModelWriter.Write(mesh, art, options, baseName);
    }

    public static byte[] Glb(string[] rows, Func<int, int, (int Front, int Back)>? depth = null) =>
        Assert.Single(Write(rows, depth, new RiseOptions { Format = ModelFormat.Glb })).Bytes;
}
