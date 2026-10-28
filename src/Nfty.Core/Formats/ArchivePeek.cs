using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>
/// Reads an archive's own manifest without decoding anything inside it.
/// </summary>
/// <remarks>
/// <para>This is what makes a workspace <em>listing</em> possible. <see cref="KitchenContents"/>
/// deliberately holds paths rather than <c>Loaded*</c> graphs, because
/// <see cref="CookBookArchive.Read"/> eagerly decodes every variant PNG in the tree — materialising a
/// whole folder just to name what is in it would pull the entire workspace into memory. But a listing
/// that can only show file names is a poor listing, and everything worth showing is already in the
/// outer manifest: a CookBook's <c>recipeWeights</c> gives its recipe count and its <c>canvas</c>
/// gives its size, with no nested read and no image touched at all.</para>
///
/// <para>So: open the zip, read <c>manifest.json</c>, close. Same
/// <see cref="ArchiveIo.ReadManifest{T}"/> every other reader goes through, so the schema gate
/// applies here too and a future-version archive is refused rather than half-understood.</para>
/// </remarks>
public static class ArchivePeek
{
    /// <summary>Reads a CookBook's manifest alone.</summary>
    /// <param name="path">Path to a <c>.cbk</c>.</param>
    /// <returns>Its manifest.</returns>
    /// <exception cref="InvalidDataException">The archive or its manifest is unreadable.</exception>
    /// <exception cref="UnsupportedSchemaVersionException">It declares a newer schema than this build reads.</exception>
    public static CookBookManifest CookBook(string path) => Peek<CookBookManifest>(path);

    /// <summary>Reads a Recipe's manifest alone.</summary>
    /// <param name="path">Path to a <c>.rcp</c>.</param>
    /// <returns>Its manifest.</returns>
    /// <exception cref="InvalidDataException">The archive or its manifest is unreadable.</exception>
    /// <exception cref="UnsupportedSchemaVersionException">It declares a newer schema than this build reads.</exception>
    public static RecipeManifest Recipe(string path) => Peek<RecipeManifest>(path);

    /// <summary>Reads an Ingredient's manifest alone.</summary>
    /// <param name="path">Path to an <c>.igt</c>.</param>
    /// <returns>Its manifest.</returns>
    /// <exception cref="InvalidDataException">The archive or its manifest is unreadable.</exception>
    /// <exception cref="UnsupportedSchemaVersionException">It declares a newer schema than this build reads.</exception>
    public static IngredientManifest Ingredient(string path) => Peek<IngredientManifest>(path);

    /// <summary>
    /// Reads a CookBook and every manifest nested inside it — recipes and their ingredients —
    /// without decoding a single variant image.
    /// </summary>
    /// <remarks>
    /// The whole argument for this type, applied one level deeper: see <see cref="PeekedCookBook"/>
    /// for what the shape is for and what it deliberately does not carry. Same
    /// <see cref="ArchiveIo.ReadManifest{T}"/> gate at every level, so a nested archive declaring a
    /// newer schema is refused here exactly as it would be by a full read.
    /// </remarks>
    /// <param name="path">Path to a <c>.cbk</c>.</param>
    /// <returns>The book's manifests, recipes ordinally by id.</returns>
    /// <exception cref="InvalidDataException">The archive or a manifest inside it is unreadable.</exception>
    /// <exception cref="UnsupportedSchemaVersionException">It, or something inside it, declares a
    /// newer schema than this build reads.</exception>
    public static PeekedCookBook CookBookTree(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = ArchiveIo.ReadManifest<CookBookManifest>(zip);
        var recipes = new List<PeekedRecipe>();
        foreach (var name in ArchiveIo.EntryNamesUnder(zip, "recipes/").OrderBy(n => n, StringComparer.Ordinal))
            recipes.Add(ArchiveIo.ReadNested(zip, name, PeekRecipe));
        return new PeekedCookBook(manifest, recipes);
    }

    /// <summary>
    /// Reads a Recipe and every Ingredient manifest nested inside it, without decoding an image.
    /// </summary>
    /// <param name="path">Path to a <c>.rcp</c>.</param>
    /// <returns>The recipe's manifests.</returns>
    /// <exception cref="InvalidDataException">The archive or a manifest inside it is unreadable.</exception>
    /// <exception cref="UnsupportedSchemaVersionException">It, or something inside it, declares a
    /// newer schema than this build reads.</exception>
    public static PeekedRecipe RecipeTree(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return PeekRecipe(zip);
    }

    /// <summary>
    /// One variant's stored PNG bytes, read out of a <c>.igt</c>, a <c>.rcp</c> or a <c>.cbk</c>
    /// without opening any other variant: only the nested archives on the way to it are read.
    /// </summary>
    /// <param name="path">The archive.</param>
    /// <param name="recipeId">The recipe holding the ingredient; required for a <c>.cbk</c>, null otherwise.</param>
    /// <param name="ingredientId">The ingredient holding the variant; required for a <c>.cbk</c> or <c>.rcp</c>, null for a <c>.igt</c>.</param>
    /// <param name="variantId">The variant.</param>
    /// <returns>The encoded PNG exactly as stored: a Dynamic or Static variant is its gray value-map.</returns>
    /// <exception cref="InvalidDataException">The archive, or an entry on the way, is missing or unreadable.</exception>
    public static byte[] VariantPng(string path, string? recipeId, string? ingredientId, string variantId)
    {
        using var zip = ZipFile.OpenRead(path);
        string entry = $"variants/{variantId}.png";
        return recipeId is not null
            ? ArchiveIo.ReadNested(zip, $"recipes/{recipeId}.rcp",
                rcp => ArchiveIo.ReadNested(rcp, $"ingredients/{ingredientId}.igt",
                    igt => ArchiveIo.ReadImageBytes(igt, entry)))
            : ingredientId is not null
                ? ArchiveIo.ReadNested(zip, $"ingredients/{ingredientId}.igt",
                    igt => ArchiveIo.ReadImageBytes(igt, entry))
                : ArchiveIo.ReadImageBytes(zip, entry);
    }

    /// <summary>One nested <c>.rcp</c>, as manifests.</summary>
    private static PeekedRecipe PeekRecipe(ZipArchive zip)
    {
        var manifest = ArchiveIo.ReadManifest<RecipeManifest>(zip);
        var ingredients = new List<IngredientManifest>();
        foreach (var name in ArchiveIo.EntryNamesUnder(zip, "ingredients/").OrderBy(n => n, StringComparer.Ordinal))
            ingredients.Add(ArchiveIo.ReadNested(zip, name, ArchiveIo.ReadManifest<IngredientManifest>));
        return new PeekedRecipe(manifest, ingredients);
    }

    private static T Peek<T>(string path) where T : ISchemaVersioned
    {
        using var zip = ZipFile.OpenRead(path);
        return ArchiveIo.ReadManifest<T>(zip);
    }
}
