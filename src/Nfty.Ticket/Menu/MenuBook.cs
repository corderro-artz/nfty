using Nfty.Core.Model;

namespace Nfty.Ticket;

/// <summary>Whether a Menu carries its art as one atlas or as a file per variant.</summary>
public enum MenuArtMode
{
    /// <summary>Every variant stitched into one PNG, addressed by the rects in the manifest.</summary>
    Atlas,

    /// <summary>One PNG per variant, nested under <c>art/</c> by recipe, ingredient and variant —
    /// <c>art/&lt;recipeId&gt;/&lt;ingredientId&gt;/&lt;variantId&gt;.png</c>, the same path
    /// <see cref="MenuBook.CellKey"/> spells with <c>/</c> in place of the file's own separator.</summary>
    Loose,
}

/// <summary>Where one variant's art sits inside the atlas.</summary>
/// <param name="Key">The cell key, from <see cref="MenuBook.CellKey"/>.</param>
/// <param name="X">Left edge in the atlas, in pixels.</param>
/// <param name="Y">Top edge in the atlas, in pixels.</param>
/// <param name="Width">Cell width.</param>
/// <param name="Height">Cell height.</param>
public record MenuCell(string Key, int X, int Y, int Width, int Height);

/// <summary>How a Menu carries its art.</summary>
/// <param name="Mode">Atlas or loose.</param>
/// <param name="AtlasWidth">The atlas PNG's width; zero in loose mode.</param>
/// <param name="AtlasHeight">The atlas PNG's height; zero in loose mode.</param>
/// <param name="Cells">One entry per distinct image. In loose mode the rects are the whole image,
/// so a reader can treat both modes with one code path.</param>
public record MenuArt(MenuArtMode Mode, int AtlasWidth, int AtlasHeight, IReadOnlyList<MenuCell> Cells);

/// <summary>One layer, as a Menu records it: everything needed to roll and to tint, and no pixels.</summary>
/// <param name="Id">The ingredient id.</param>
/// <param name="Name">The trait_type this layer publishes under.</param>
/// <param name="Kind">Dynamic, Static or Custom.</param>
/// <param name="Colorization">How the layer is colored; null for Custom.</param>
/// <param name="Variants">The weighted variants.</param>
public record MenuLayer(
    string Id, string Name, LayerKind Kind, Colorization? Colorization, IReadOnlyList<Variant> Variants);

/// <summary>One recipe, as a Menu records it.</summary>
/// <param name="Id">The recipe id.</param>
/// <param name="Name">Its display name, published as the <c>Type</c> trait.</param>
/// <param name="Weight">Its weight against the book's other recipes.</param>
/// <param name="LayerOrder">Bottom-to-top paint order — and the order the roll consumes the RNG in,
/// which is why it travels rather than being inferred from <paramref name="Layers"/>.</param>
/// <param name="Layers">Its layers, keyed by the ids in <paramref name="LayerOrder"/>.</param>
/// <param name="Rules">Its incompatibility rules.</param>
/// <param name="AbsentPercent">How often each layer is left out, keyed by ingredient id. A layer
/// absent from this map always appears.</param>
public record MenuRecipe(
    string Id,
    string Name,
    double Weight,
    IReadOnlyList<string> LayerOrder,
    IReadOnlyList<MenuLayer> Layers,
    IReadOnlyList<IncompatibilityRule> Rules,
    IReadOnlyDictionary<string, double> AbsentPercent)
{
    /// <summary>How often <paramref name="ingredientId"/> is left out of this recipe, 0..100.</summary>
    /// <param name="ingredientId">The layer to ask about.</param>
    /// <returns>The recorded percent, or zero when the recipe records none.</returns>
    public double AbsentPercentOf(string ingredientId) =>
        AbsentPercent.TryGetValue(ingredientId, out double p) ? p : 0;
}

/// <summary>
/// A CookBook flattened for runtime: every weight, rule and colorization, plus where the art is,
/// and not one decoded pixel.
/// </summary>
/// <param name="MenuSchemaVersion">The Menu schema this was written against.</param>
/// <param name="SourceCookBookSha256">The hash of the <c>.cbk</c> this came from, or null when it
/// was built from a book that never came from a file. Null means "cannot tell", never "differs".</param>
/// <param name="Canvas">The canvas every variant shares.</param>
/// <param name="CollectionName">What assets are published as — <c>"{name} #{n}"</c>.</param>
/// <param name="CollectionSymbol">The collection's symbol.</param>
/// <param name="Recipes">Its recipes.</param>
/// <param name="Art">Where the art is.</param>
/// <param name="PartialAlpha">Whether any variant image anywhere in the book carries partial alpha —
/// the same definition <c>Nfty.Core.Imaging.Opacity</c> stamps onto every archive manifest,
/// projected here so a runtime host can tell 2D-only art (safe to lift with depth maps alone) from
/// art with a transparency gradient Rise cannot resolve, with no <c>.cbk</c> present. Defaults to
/// <c>false</c> so schema 1 wire data — which predates this field — reads as "no partial alpha
/// recorded" rather than failing to deserialize; a Menu at schema 2 or above always carries a real,
/// stamped value.</param>
/// <remarks>
/// <see cref="MenuSchemaVersion"/> is named — and therefore written and read — as
/// <c>menuSchemaVersion</c> rather than the plain <c>schemaVersion</c> a member called
/// <c>SchemaVersion</c> would camelCase to. A Menu's schema line is separate from
/// <see cref="Nfty.Core.Model.Schema"/>, the CookBook manifest line, and a shared key name between
/// two independently evolving formats is how one gets bumped and silently changes what the other
/// means. Named this way deliberately rather than overridden with a
/// <c>System.Text.Json.Serialization.JsonPropertyName</c> attribute — no manifest record in this
/// project carries a JSON attribute; the wire policy is supplied centrally
/// (<c>Nfty.Core.Formats.Json.Options</c> on the write side, <c>MenuArchive.WireOptions</c> on the
/// read side), and a property whose own name already produces the right key needs no exception to
/// that rule.
/// </remarks>
public record MenuBook(
    int MenuSchemaVersion,
    string? SourceCookBookSha256,
    Dimensions Canvas,
    string CollectionName,
    string CollectionSymbol,
    IReadOnlyList<MenuRecipe> Recipes,
    MenuArt Art,
    bool PartialAlpha = false)
{
    /// <summary>
    /// The key one variant's art is stored under.
    /// </summary>
    /// <param name="recipeId">The recipe the layer belongs to.</param>
    /// <param name="ingredientId">The layer.</param>
    /// <param name="variantId">The variant.</param>
    /// <returns>The cell key.</returns>
    /// <remarks>
    /// <b>Scoped by RECIPE, and that is not redundancy.</b> A <c>.cbk</c> nests a separate
    /// <c>.igt</c> inside each <c>.rcp</c>, so two recipes may each carry an ingredient with the id
    /// <c>bg</c> holding completely different variants. A shallower key collides without a word and
    /// one recipe silently draws another's art.
    /// </remarks>
    public static string CellKey(string recipeId, string ingredientId, string variantId) =>
        $"{recipeId}/{ingredientId}/{variantId}";
}
