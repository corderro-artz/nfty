using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Menu schema 2: <see cref="MenuWriter"/> carries each variant's <see cref="DepthMap"/> through to
/// the <c>.mnu</c>, and the book's 2D-only (<see cref="Nfty.Core.Imaging.Opacity"/>) flag.
/// </summary>
public class MenuDepthTests
{
    /// <summary>
    /// One recipe, two Custom layers: "withDepth" carries a painted, non-flat depth map on its one
    /// opaque variant; "noDepth" carries none. <paramref name="partialAlphaPixel"/> controls whether
    /// "noDepth"'s pixel is fully opaque (255) or partially transparent (128), so the same fixture
    /// shape drives both the depth round-trip test and the partialAlpha flag test.
    /// </summary>
    private static LoadedCookBook Book(bool partialAlphaPixel)
    {
        byte alpha = partialAlphaPixel ? (byte)128 : (byte)255;

        var withDepthImage = new Image<Rgba32>(2, 2, new Rgba32(200, 30, 30, 255));
        var noDepthImage = new Image<Rgba32>(2, 2, new Rgba32(30, 30, 200, alpha));

        // Fully opaque source pixel, so the writer's zero-over-transparency rule leaves this map
        // untouched — the point of this fixture is the round-trip, not the zeroing rule (Task 3
        // already covers that for .igt, and MenuWriter reuses the same helper).
        var depthMap = DepthMap.Flat(2, 2).With(0, 0, front: 10, back: 20);

        var withDepth = new LoadedIngredient
        {
            Manifest = new IngredientManifest("withDepth", "WithDepth", LayerKind.Custom, null,
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = withDepthImage },
            Depths = new Dictionary<string, DepthMap> { ["v"] = depthMap },
        };
        var noDepth = new LoadedIngredient
        {
            Manifest = new IngredientManifest("noDepth", "NoDepth", LayerKind.Custom, null,
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = noDepthImage },
        };

        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
                new Collection("Book", "d", "BK"), new Dictionary<string, double> { ["rec"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("rec", "Rec", new[] { "withDepth", "noDepth" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { withDepth, noDepth },
                },
            },
        };
    }

    [Theory]
    [InlineData(MenuArtMode.Atlas)]
    [InlineData(MenuArtMode.Loose)]
    public void A_variants_depth_map_round_trips_through_the_menu(MenuArtMode art)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var book = Book(partialAlphaPixel: false);
            string mnu = Path.Combine(dir, "game.mnu");
            MenuWriter.Write(book, mnu, new MenuWriteOptions { Art = art });

            using var menu = MenuArchive.Open(mnu);
            string withKey = MenuBook.CellKey("rec", "withDepth", "v");
            string noKey = MenuBook.CellKey("rec", "noDepth", "v");

            var map = menu.Depth(withKey);
            Assert.NotNull(map);
            Assert.Equal(2, map!.Width);
            Assert.Equal(2, map.Height);
            Assert.Equal(10, map.Front(0, 0));
            Assert.Equal(20, map.Back(0, 0));

            // A variant that was never painted gets no depth/ entry at all - null, not a flat map,
            // is what MenuArchive.Depth reports for it.
            Assert.Null(menu.Depth(noKey));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_menus_partialAlpha_flag_reflects_the_books_pixels()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var dirty = Book(partialAlphaPixel: true);
            string mnuDirty = Path.Combine(dir, "dirty.mnu");
            MenuWriter.Write(dirty, mnuDirty, new MenuWriteOptions());
            using var menuDirty = MenuArchive.Open(mnuDirty);
            Assert.True(menuDirty.Book.PartialAlpha);

            using var clean = Book(partialAlphaPixel: false);
            string mnuClean = Path.Combine(dir, "clean.mnu");
            MenuWriter.Write(clean, mnuClean, new MenuWriteOptions());
            using var menuClean = MenuArchive.Open(mnuClean);
            Assert.False(menuClean.Book.PartialAlpha);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
