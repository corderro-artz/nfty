using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nfty.Core.Model;

namespace Nfty.Ticket.Tests;

public class PlatingTests
{
    /// <summary>
    /// The same wire policy <c>MenuArchive</c>'s private <c>WireOptions</c> parses with — camelCase
    /// properties, enums by name — built independently here rather than reused, because there is
    /// nothing to reuse: the type is private to that file on purpose (see its own remarks), and a
    /// test proving what a REAL <c>.mnu</c> round-trip does has to go through the actual wire format,
    /// not a shortcut into the object graph.
    /// </summary>
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static MenuArchive OpenMenu(MenuBook book)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open(), Encoding.UTF8))
            w.Write(JsonSerializer.Serialize(book, WireOptions));
        return MenuArchive.Open(new MemoryStream(ms.ToArray()));
    }

    private static Colorization Range(ColorModel model) =>
        new(model, 10, 10, [new ColorEntry(1, new ColorRange(0, 360, 0, 100), null)]);

    /// <summary>One recipe, one layer, one cell — just enough for <see cref="Plating.Plate"/> to
    /// resolve a single draw.</summary>
    private static MenuArchive OneLayerMenu(string recipeId, string ingredientId, LayerKind kind,
        Colorization? colorization)
    {
        var layer = new MenuLayer(ingredientId, ingredientId, kind, colorization,
            [new Variant("a", "A", 1)]);
        var recipe = new MenuRecipe(recipeId, recipeId, 1, [ingredientId], [layer], [],
            new Dictionary<string, double>());
        string key = MenuBook.CellKey(recipeId, ingredientId, "a");
        var art = new MenuArt(MenuArtMode.Loose, 0, 0, [new MenuCell(key, 0, 0, 2, 2)]);
        var book = new MenuBook(1, null, new Dimensions(2, 2), "C", "SYM", [recipe], art);
        return OpenMenu(book);
    }

    // ------------------------------------------------------------------ R11: Menu is authoritative

    /// <summary>
    /// R11's first half: <c>Plate</c> must read <c>Kind</c> from the MENU, never from the ticket.
    /// The Menu says Dynamic; the (hand-built, "tampered") ticket says Custom. <c>Dna.Compute</c>
    /// never hashes <c>Kind</c>, so a wire altered only here still carries a provably-correct DNA —
    /// nothing about the ticket's own consistency check would catch this, which is exactly why the
    /// rule has to be enforced in <c>Plate</c> itself and not merely explained in a comment beside it.
    /// </summary>
    [Fact]
    public void Plate_takes_kind_from_the_menu_even_when_the_ticket_disagrees()
    {
        using var menu = OneLayerMenu("cat", "bg", LayerKind.Dynamic, Range(ColorModel.Hsv));

        // A ticket a legitimate roll could never produce: Kind says Custom where the Menu says
        // Dynamic. Hue/Sat/Model are populated exactly as a real Dynamic roll would leave them, so
        // the only tampered field is Kind.
        var ticket = new AssetTicket("irrelevant-for-this-test", "cat",
            [new TicketLayer("bg", "a", 180, 0.5, ColorModel.Hsv, LayerKind.Custom, 10, 10)]);

        var draw = Assert.Single(Plating.Plate(menu, ticket));

        Assert.Equal(LayerKind.Dynamic, draw.Kind);   // the Menu's answer, not the ticket's Custom
    }

    /// <summary>
    /// R11's second half, told apart from the first: <c>Kind</c> and <c>Model</c> are set from
    /// structurally independent expressions in <c>Plate</c> (<c>definition.Kind</c> and
    /// <c>definition.Colorization?.Model ?? ColorModel.Hsv</c>), so a half-fix that reads
    /// <c>Kind</c> from the Menu but still reads <c>Model</c> from the ticket would pass the test
    /// above and still mis-colorize: <c>Colorizer.Apply</c> branches <c>HsvToRgb</c> against
    /// <c>HslToRgb</c> on <c>Model</c> alone, so identical (H,S) numbers produce different RGB — a
    /// difference invisible to <c>Dna.Compute</c> (it hashes neither Kind nor Model) and visible only
    /// in the rendered pixels. Here the Kind matches (both Dynamic) so only Model is tampered.
    /// </summary>
    [Fact]
    public void Plate_takes_color_model_from_the_menu_even_when_the_ticket_disagrees()
    {
        using var menu = OneLayerMenu("cat", "bg", LayerKind.Dynamic, Range(ColorModel.Hsv));

        var ticket = new AssetTicket("irrelevant-for-this-test", "cat",
            [new TicketLayer("bg", "a", 180, 0.5, ColorModel.Hsl, LayerKind.Dynamic, 10, 10)]);

        var draw = Assert.Single(Plating.Plate(menu, ticket));

        Assert.Equal(ColorModel.Hsv, draw.Model);   // the Menu's answer, not the ticket's Hsl
    }

    // ------------------------------------------------------------------ quantize: Menu is authoritative

    /// <summary>
    /// The quantize a ticket carries is NOT authoritative either. <c>Dna.Compute</c> hashes the
    /// BUCKET index, so a hue moved to another value inside a re-quantized bucket keeps the DNA:
    /// hue 100 at a 10-degree step is bucket 10, and so is hue 205 at a 20-degree step. That forged
    /// wire parses — its DNA is genuinely the DNA its contents produce — and without a check against
    /// the Menu it plates a hue 105 degrees away from the one that was rolled.
    /// </summary>
    [Fact]
    public void Plate_refuses_a_ticket_whose_quantize_differs_from_the_menu()
    {
        using var menu = OneLayerMenu("cat", "bg", LayerKind.Dynamic, Range(ColorModel.Hsv));
        TicketLayer[] rolled = [new("bg", "a", 100, 0.5, ColorModel.Hsv, LayerKind.Dynamic, 10, 10)];
        var genuine = new AssetTicket(AssetTicket.ComputeDna("cat", rolled), "cat", rolled);

        string forgedWire = genuine.ToWire().Replace(
            "bg:a@100,0.5,Hsv,10,10,", "bg:a@205,0.5,Hsv,20,10,", StringComparison.Ordinal);
        Assert.NotEqual(genuine.ToWire(), forgedWire);
        var forged = AssetTicket.Parse(forgedWire);          // same DNA, so the wire check passes
        Assert.Equal(genuine.Dna, forged.Dna);

        var ex = Assert.Throws<FormatException>(() => Plating.Plate(menu, forged));
        Assert.Contains("bg", ex.Message, StringComparison.Ordinal);

        // The genuine ticket still plates, at the hue that was rolled.
        var draw = Assert.Single(Plating.Plate(menu, AssetTicket.Parse(genuine.ToWire())));
        Assert.Equal(100, draw.Hue);
    }

    // ------------------------------------------------------------------ MenuArchive.Layer naming

    [Fact]
    public void Layer_names_an_unknown_recipe()
    {
        using var menu = OneLayerMenu("cat", "bg", LayerKind.Custom, null);

        var ex = Assert.Throws<KeyNotFoundException>(() => menu.Layer("dog", "bg"));
        Assert.Contains("dog", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Layer_names_an_unknown_ingredient_within_a_known_recipe()
    {
        using var menu = OneLayerMenu("cat", "bg", LayerKind.Custom, null);

        var ex = Assert.Throws<KeyNotFoundException>(() => menu.Layer("cat", "hat"));
        Assert.Contains("cat", ex.Message, StringComparison.Ordinal);
        Assert.Contains("hat", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Before this fix, an unknown recipe id reached the caller as an unnamed
    /// <see cref="InvalidOperationException"/> out of <c>Enumerable.Single</c> — a shape
    /// <see cref="Plate_takes_kind_from_the_menu_even_when_the_ticket_disagrees"/> and its sibling
    /// above do not exercise, since both use a recipe id the Menu actually has. This pins the
    /// specific failure mode <c>Plate</c>'s docstring now promises: a stale recipe id on an otherwise
    /// well-formed ticket surfaces as a named <see cref="KeyNotFoundException"/>, not a generic one.
    /// </summary>
    [Fact]
    public void Plate_names_a_recipe_the_menu_does_not_have()
    {
        using var menu = OneLayerMenu("cat", "bg", LayerKind.Custom, null);
        var ticket = new AssetTicket("irrelevant-for-this-test", "dog",
            [new TicketLayer("bg", "a", null, null, null, LayerKind.Custom, 0, 0)]);

        var ex = Assert.Throws<KeyNotFoundException>(() => Plating.Plate(menu, ticket));
        Assert.Contains("dog", ex.Message, StringComparison.Ordinal);
    }
}
