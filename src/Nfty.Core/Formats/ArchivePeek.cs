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

    private static T Peek<T>(string path) where T : ISchemaVersioned
    {
        using var zip = ZipFile.OpenRead(path);
        return ArchiveIo.ReadManifest<T>(zip);
    }
}
