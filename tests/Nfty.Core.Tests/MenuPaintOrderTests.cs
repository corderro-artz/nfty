using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

public class MenuPaintOrderTests
{
    [Theory]
    [InlineData(MenuArtMode.Atlas)]
    [InlineData(MenuArtMode.Loose)]
    public void Reordering_ticket_wire_layers_keeps_canonical_pixels_and_DNA(MenuArtMode mode)
    {
        static LoadedIngredient Layer(string id, Rgba32 pixel) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, [new Variant("a", "A", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(1, 1, pixel) },
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("book", "Book", new Dimensions(1, 1),
                new Collection("C", "D", "S"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = [new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", ["zebra", "apple"], []),
                Ingredients = [Layer("zebra", new Rgba32(255, 0, 0)), Layer("apple", new Rgba32(0, 0, 255))],
            }],
        };
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string path = Path.Combine(dir, "paint.mnu");
            MenuWriter.Write(book, path, new MenuWriteOptions { Art = mode });
            using var menu = MenuArchive.Open(path);
            TicketLayer[] layers = [new("zebra", "a", null, null, null, LayerKind.Custom, 0, 0),
                new("apple", "a", null, null, null, LayerKind.Custom, 0, 0)];
            var ticket = new AssetTicket(AssetTicket.ComputeDna("cat", layers), "cat", layers);
            string[] wire = ticket.ToWire().Split('|');
            (wire[2], wire[3]) = (wire[3], wire[2]);
            var reordered = AssetTicket.Parse(string.Join('|', wire));

            Assert.Equal(ticket.Dna, reordered.Dna);
            using var original = MenuRenderer.Render(menu, ticket);
            using var rendered = MenuRenderer.Render(menu, reordered);
            Assert.Equal(new Rgba32(0, 0, 255), original[0, 0]);
            Assert.Equal(new Rgba32(0, 0, 255), rendered[0, 0]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
