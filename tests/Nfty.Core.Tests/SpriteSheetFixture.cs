using System.IO;
using Nfty.Core.Generation;
using Nfty.Core.Output;

namespace Nfty.Core.Tests;

/// <summary>
/// A cooked Set on disk, for the tests that need real PNGs to stitch or stamp.
/// </summary>
/// <remarks>
/// Shared by <see cref="SpriteSheetTests"/> and <see cref="NumberStampTests"/> because both need
/// the same thing — a handful of distinctly-coloured 4x4 assets, so a cell or a corner can be
/// identified from one pixel. Everything else in this project builds its fixtures in memory; these
/// two cannot, because the code under test reads images off disk by path.
/// </remarks>
internal static class SpriteSheetFixture
{
    /// <summary>Cooks a Set into a fresh temp directory and returns it.</summary>
    /// <param name="count">How many assets.</param>
    /// <param name="variants">How many distinct colours to roll between.</param>
    /// <param name="canvas">The canvas size. 4 is enough to identify a cell by one pixel and is
    /// what the sheet tests use; the watermark tests need a canvas a stamp actually fits on, which
    /// is a real constraint rather than a fixture detail — <c>NumberStamp.Draw</c> declines to draw
    /// half a number.</param>
    /// <returns>The folder, for the caller to delete.</returns>
    public static string Cook(int count, int variants = 6, int canvas = 4)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using var book = SpriteSheetTests.Book(variants, canvas);
        using var set = Generator.Generate(book, new GenerateOptions(count, "sheet-seed"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }
}
