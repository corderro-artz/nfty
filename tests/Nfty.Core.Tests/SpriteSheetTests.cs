using System.IO;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// Stitching a cooked Set into one image. The assertions are on PIXELS at known cell corners,
/// because a sheet whose cells are laid out in the wrong order is still a perfectly valid PNG.
/// </summary>
public class SpriteSheetTests
{
    /// <summary>A book of N distinctly-coloured 4x4 custom variants, so a cell can be identified
    /// from the colour of one pixel.</summary>
    internal static LoadedCookBook Book(int variants, int canvas = 4)
    {
        var images = new Dictionary<string, Image<Rgba32>>();
        var list = new List<Variant>();
        for (int i = 0; i < variants; i++)
        {
            list.Add(new Variant($"v{i}", $"V{i}", 1));
            images[$"v{i}"] = new Image<Rgba32>(canvas, canvas, new Rgba32((byte)(10 + i * 20), 40, 60, 255));
        }

        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null, list),
            VariantImages = images,
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(canvas, canvas),
                new Collection("Sheets", "d", "SH"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            ],
        };
    }

    private static string Cook(int count, int variants = 6) => SpriteSheetFixture.Cook(count, variants);

    [Fact]
    public void A_SHEET_PLACES_ASSETS_LEFT_TO_RIGHT_THEN_TOP_TO_BOTTOM()
    {
        // THE ASSERTION THE WHOLE FEATURE IS. Cell n must be asset n+1 in the Set's own numbering -
        // that is what makes a sheet addressable, and a sheet laid out down-then-across is still a
        // valid PNG that every other test here would pass.
        string dir = Cook(6);
        try
        {
            using var set = SetReader.Read(dir);
            string png = Path.Combine(dir, "sheet.png");
            var layout = new SpriteSheetLayout(3, 2, 4, 4);

            SpriteSheet.Write(set.Items, layout, png);

            using var sheet = Image.Load<Rgba32>(png);
            Assert.Equal(12, sheet.Width);
            Assert.Equal(8, sheet.Height);

            for (int i = 0; i < set.Items.Count; i++)
            {
                using var asset = Image.Load<Rgba32>(set.Items[i].ImagePath);
                int x = i % 3 * 4, y = i / 3 * 4;
                Assert.Equal(asset[0, 0], sheet[x, y]);
                Assert.Equal(asset[3, 3], sheet[x + 3, y + 3]);
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_short_last_row_leaves_transparent_cells_rather_than_shrinking_the_grid()
    {
        // That is what a spritesheet looks like, and the alternative renumbers frames: a grid that
        // closed the gap would put asset 5 where an engine indexing rows expects asset 7.
        string dir = Cook(5);
        try
        {
            using var set = SetReader.Read(dir);
            string png = Path.Combine(dir, "sheet.png");

            SpriteSheet.Write(set.Items, new SpriteSheetLayout(3, 2, 4, 4), png);

            using var sheet = Image.Load<Rgba32>(png);
            Assert.Equal(0, sheet[10, 6].A);        // the sixth cell, never filled
            Assert.NotEqual(0, sheet[6, 6].A);      // the fifth, which was
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_missing_asset_leaves_a_hole_rather_than_failing_the_sheet()
    {
        // The Set browser already takes this position - show the damage rather than refuse to open -
        // and a hole is information: a transparent cell among drawn ones is unmistakable, where a
        // sheet that closed the gap would renumber every frame after it.
        string dir = Cook(4);
        try
        {
            using var set = SetReader.Read(dir);
            File.Delete(set.Items[1].ImagePath);
            string png = Path.Combine(dir, "sheet.png");

            SpriteSheet.Write(set.Items, new SpriteSheetLayout(2, 2, 4, 4), png);

            using var sheet = Image.Load<Rgba32>(png);
            Assert.Equal(0, sheet[5, 1].A);         // cell 1, whose file is gone
            Assert.NotEqual(0, sheet[1, 1].A);      // cell 0, still there
            Assert.NotEqual(0, sheet[1, 5].A);      // cell 2, drawn after the failure
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Fit_is_the_squarest_grid_that_holds_the_collection()
    {
        Assert.Equal(new SpriteSheetLayout(3, 3, 4, 4), SpriteSheet.Fit(9, 4, 4));
        Assert.Equal(new SpriteSheetLayout(4, 4, 4, 4), SpriteSheet.Fit(16, 4, 4));
        Assert.Equal(new SpriteSheetLayout(3, 2, 4, 4), SpriteSheet.Fit(5, 4, 4));
        Assert.Equal(new SpriteSheetLayout(1, 1, 4, 4), SpriteSheet.Fit(1, 4, 4));

        // Square by CELL COUNT, not by pixels: a 4:1 canvas must not produce a grid four cells wide.
        Assert.Equal(4, SpriteSheet.Fit(16, 64, 16).Columns);
    }

    [Fact]
    public void A_GRID_TOO_SMALL_FOR_THE_SET_IS_REFUSED_WITH_THE_NUMBERS()
    {
        // A refusal an author can act on. It names what the grid holds and what the Set has, so the
        // fix is arithmetic rather than guesswork.
        string? problem = SpriteSheet.ProblemWith(new SpriteSheetLayout(2, 2, 4, 4), 9);

        Assert.NotNull(problem);
        Assert.Contains("holds 4", problem, StringComparison.Ordinal);
        Assert.Contains("this Set has 9", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_SHEET_PAST_THE_PIXEL_CEILING_IS_REFUSED_BEFORE_ANYTHING_IS_ALLOCATED()
    {
        // A sheet is one contiguous raster, so the ceiling is real rather than defensive: 500 assets
        // at 1000px in a 23x23 grid is 529 megapixels and about 2 GB of RGBA before the encoder sees
        // it. Refusing with the numbers beats an OutOfMemoryException half a minute in.
        var huge = new SpriteSheetLayout(100, 100, 1000, 1000);

        Assert.NotNull(SpriteSheet.ProblemWith(huge, 10_000));
        Assert.Contains("limit", SpriteSheet.ProblemWith(huge, 10_000)!, StringComparison.Ordinal);

        // And the writer throws on the same condition, so a caller that skipped the check still
        // cannot produce a broken sheet.
        Assert.Throws<ArgumentException>(() =>
            SpriteSheet.Write(Array.Empty<SetItem>(), huge, "unused.png"));
    }

    [Fact]
    public void A_long_thin_strip_is_refused_on_its_SIDE_rather_than_its_area()
    {
        // Small in area and still not an image anything can open, which is why the two limits are
        // checked separately.
        var strip = new SpriteSheetLayout(20_000, 1, 4, 4);

        string? problem = SpriteSheet.ProblemWith(strip, 20_000);

        Assert.NotNull(problem);
        Assert.Contains("on a side", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_is_reported_once_per_asset_and_reaches_the_end()
    {
        string dir = Cook(6);
        try
        {
            using var set = SetReader.Read(dir);
            var seen = new List<SpriteSheetProgress>();

            SpriteSheet.Write(set.Items, new SpriteSheetLayout(3, 2, 4, 4),
                Path.Combine(dir, "sheet.png"),
                new Progress<SpriteSheetProgress>(seen.Add));

            // Progress<T> posts through the synchronization context, so a report can still be in
            // flight; what must hold is that the last one seen is the finished one.
            Assert.Contains(seen, p => p.Placed == 6 && p.Fraction == 1);
            Assert.All(seen, p => Assert.Equal(6, p.Total));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_export_that_asks_for_a_sheet_carries_one_and_says_so_first()
    {
        // The plan is what the screen promises, so a sheet that appeared in the output without
        // appearing in the list is the defect this seam exists to prevent.
        string dir = Cook(4);
        string outDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var options = new ExportOptions
            {
                SpriteSheet = true,
                SpriteSheetColumns = 2,
                SpriteSheetRows = 2,
                Shape = ExportShape.Folder,
            };

            var plan = SetExporter.Plan(dir, options);
            Assert.Equal(new SpriteSheetLayout(2, 2, 4, 4), plan.SpriteSheet);
            Assert.Contains(plan.Parts(), p => p.StartsWith("spritesheet.png", StringComparison.Ordinal));

            var result = SetExporter.Export(dir, outDir, options);
            string sheet = Path.Combine(result.Path, "spritesheet.png");

            Assert.True(File.Exists(sheet));
            using var image = Image.Load<Rgba32>(sheet);
            Assert.Equal(8, image.Width);
            Assert.Equal(8, image.Height);

            // In the RESULT it is an ordinary entry with a real size, which is what lets the total
            // be honest once the file exists.
            Assert.Contains(result.Plan.Entries, e => e.Name == "spritesheet.png" && e.Bytes > 0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void An_export_without_the_option_is_byte_for_byte_what_it_always_was()
    {
        // The axis is additive: a book that does not ask for a sheet must not gain one, and nothing
        // about the plan it already produced may change.
        string dir = Cook(4);
        string outDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var plan = SetExporter.Plan(dir, new ExportOptions { Shape = ExportShape.Folder });

            Assert.Null(plan.SpriteSheet);
            Assert.DoesNotContain(plan.Parts(), p => p.Contains("spritesheet", StringComparison.Ordinal));

            var result = SetExporter.Export(dir, outDir, new ExportOptions { Shape = ExportShape.Folder });
            Assert.False(File.Exists(Path.Combine(result.Path, "spritesheet.png")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task An_export_reports_progress_that_ends_at_the_end()
    {
        string dir = Cook(4);
        string outDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var seen = new List<ExportProgress>();
            var options = new ExportOptions { SpriteSheet = true, Shape = ExportShape.Folder };

            await SetExporter.ExportAsync(dir, outDir, options,
                progress: new Progress<ExportProgress>(seen.Add));

            Assert.NotEmpty(seen);
            Assert.All(seen, p => Assert.InRange(p.Fraction, 0, 1));
            Assert.Contains(seen, p => p.Fraction == 1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }
}
