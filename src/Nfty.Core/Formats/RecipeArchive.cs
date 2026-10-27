using System.IO.Compression;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>Reads and writes <c>.rcp</c> archives — a manifest plus one nested <c>.igt</c> per
/// ingredient. The <c>ZipArchive</c> overloads exist so a CookBook can nest one without a file.</summary>
public static class RecipeArchive
{
    /// <summary>Stamps <see cref="RecipeManifest.PartialAlpha"/> from the ingredients' own pixels
    /// rather than trusting whatever the caller (a writer) or the file (a reader) said. Shared by
    /// every sync/async write and read path so they cannot drift on the definition.</summary>
    private static RecipeManifest Stamped(RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients) =>
        manifest with { PartialAlpha = ingredients.Any(Opacity.HasPartialAlpha) };

    /// <summary>Writes a Recipe into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The recipe's manifest. Its <c>PartialAlpha</c> is ignored — the
    /// written value is recomputed from <paramref name="ingredients"/>' images.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    /// <param name="parallelism">How wide image encoding or decoding may run; null means <see cref="Parallelism.Default"/>. Never changes the bytes.</param>
    public static void Write(ZipArchive zip, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients,
        Parallelism? parallelism = null)
    {
        ArchiveIo.WriteManifest(zip, Stamped(manifest, ingredients));
        foreach (var ing in ingredients)
            ArchiveIo.WriteNested(zip, $"ingredients/{ing.Manifest.Id}.igt",
                inner => IngredientArchive.Write(inner, ing.Manifest, ing.VariantImages, ing.Depths, parallelism));
    }

    /// <summary>Reads a Recipe from an already-open archive, decoding every variant PNG.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="parallelism">How wide image encoding or decoding may run; null means <see cref="Parallelism.Default"/>. Never changes the bytes.</param>
    /// <returns>The loaded recipe; the caller owns it. Its manifest's <c>PartialAlpha</c> is
    /// recomputed from its loaded ingredients rather than trusting the stored value.</returns>
    public static LoadedRecipe Read(ZipArchive zip, Parallelism? parallelism = null)
    {
        var manifest = ArchiveIo.ReadManifest<RecipeManifest>(zip);
        var ingredients = new List<LoadedIngredient>();
        try
        {
            foreach (var name in ArchiveIo.EntryNamesUnder(zip, "ingredients/").OrderBy(n => n, StringComparer.Ordinal))
                ingredients.Add(ArchiveIo.ReadNested(zip, name, inner => IngredientArchive.Read(inner, parallelism)));
        }
        catch
        {
            // Ingredients already decoded (each owning its own variant images) before a later
            // one threw have no other owner yet — dispose them before the exception propagates.
            foreach (var ing in ingredients) ing.Dispose();
            throw;
        }
        return new LoadedRecipe { Manifest = Stamped(manifest, ingredients), Ingredients = ingredients };
    }

    /// <summary>Writes a Recipe to a file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The recipe's manifest.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    /// <param name="parallelism">How wide image encoding or decoding may run; null means <see cref="Parallelism.Default"/>. Never changes the bytes.</param>
    public static void Write(string path, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients,
        Parallelism? parallelism = null)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, ingredients, parallelism);
    }

    /// <summary>Reads a Recipe from a file, decoding every variant PNG.</summary>
    /// <param name="path">Archive path.</param>
    /// <param name="parallelism">How wide image encoding or decoding may run; null means <see cref="Parallelism.Default"/>. Never changes the bytes.</param>
    /// <returns>The loaded recipe; the caller owns it and must dispose it.</returns>
    public static LoadedRecipe Read(string path, Parallelism? parallelism = null)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip, parallelism);
    }

    /// <summary>Writes a Recipe into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The recipe's manifest.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <param name="parallelism">How wide image encoding or decoding may run; null means <see cref="Parallelism.Default"/>. Never changes the bytes.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(ZipArchive zip, RecipeManifest manifest,
        IReadOnlyList<LoadedIngredient> ingredients, CancellationToken ct = default,
        Parallelism? parallelism = null)
    {
        await ArchiveIo.WriteManifestAsync(zip, Stamped(manifest, ingredients), ct).ConfigureAwait(false);
        foreach (var ing in ingredients)
            await ArchiveIo.WriteNestedAsync(zip, $"ingredients/{ing.Manifest.Id}.igt",
                inner => IngredientArchive.WriteAsync(inner, ing.Manifest, ing.VariantImages, ing.Depths, ct, parallelism), ct).ConfigureAwait(false);
    }

    /// <summary>Reads a Recipe from an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="ct">Cancels the read; anything already decoded is disposed first.</param>
    /// <param name="parallelism">How wide image encoding or decoding may run; null means <see cref="Parallelism.Default"/>. Never changes the bytes.</param>
    /// <returns>The loaded recipe; the caller owns it. Its manifest's <c>PartialAlpha</c> is
    /// recomputed from its loaded ingredients rather than trusting the stored value.</returns>
    public static async Task<LoadedRecipe> ReadAsync(ZipArchive zip, CancellationToken ct = default,
        Parallelism? parallelism = null)
    {
        var manifest = await ArchiveIo.ReadManifestAsync<RecipeManifest>(zip, ct).ConfigureAwait(false);
        var ingredients = new List<LoadedIngredient>();
        try
        {
            foreach (var name in ArchiveIo.EntryNamesUnder(zip, "ingredients/").OrderBy(n => n, StringComparer.Ordinal))
                ingredients.Add(await ArchiveIo.ReadNestedAsync(zip, name, (inner, token) => IngredientArchive.ReadAsync(inner, token, parallelism), ct).ConfigureAwait(false));
        }
        catch
        {
            foreach (var ing in ingredients) ing.Dispose();
            throw;
        }
        return new LoadedRecipe { Manifest = Stamped(manifest, ingredients), Ingredients = ingredients };
    }

    /// <summary>Writes a Recipe to a file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The recipe's manifest.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <param name="parallelism">How wide image encoding or decoding may run; null means <see cref="Parallelism.Default"/>. Never changes the bytes.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(string path, RecipeManifest manifest,
        IReadOnlyList<LoadedIngredient> ingredients, CancellationToken ct = default,
        Parallelism? parallelism = null)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await WriteAsync(zip, manifest, ingredients, ct, parallelism).ConfigureAwait(false);
    }

    /// <summary>Reads a Recipe from a file.</summary>
    /// <param name="path">Archive path.</param>
    /// <param name="ct">Cancels the read; anything already decoded is disposed first.</param>
    /// <param name="parallelism">How wide decoding may run; null means <see cref="Parallelism.Default"/>.</param>
    /// <returns>The loaded recipe; the caller owns it.</returns>
    public static async Task<LoadedRecipe> ReadAsync(string path, CancellationToken ct = default,
        Parallelism? parallelism = null)
    {
        using var zip = ZipFile.OpenRead(path);
        return await ReadAsync(zip, ct, parallelism).ConfigureAwait(false);
    }
}
