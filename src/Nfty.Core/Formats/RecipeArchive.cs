using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>Reads and writes <c>.rcp</c> archives — a manifest plus one nested <c>.igt</c> per
/// ingredient. The <c>ZipArchive</c> overloads exist so a CookBook can nest one without a file.</summary>
public static class RecipeArchive
{
    /// <summary>Writes a Recipe into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The recipe's manifest.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    public static void Write(ZipArchive zip, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients)
    {
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var ing in ingredients)
            ArchiveIo.WriteNested(zip, $"ingredients/{ing.Manifest.Id}.igt",
                inner => IngredientArchive.Write(inner, ing.Manifest, ing.VariantImages));
    }

    /// <summary>Reads a Recipe from an already-open archive, decoding every variant PNG.</summary>
    /// <param name="zip">The open archive.</param>
    /// <returns>The loaded recipe; the caller owns it.</returns>
    public static LoadedRecipe Read(ZipArchive zip)
    {
        var manifest = ArchiveIo.ReadManifest<RecipeManifest>(zip);
        var ingredients = new List<LoadedIngredient>();
        try
        {
            foreach (var name in ArchiveIo.EntryNamesUnder(zip, "ingredients/").OrderBy(n => n, StringComparer.Ordinal))
                ingredients.Add(ArchiveIo.ReadNested(zip, name, IngredientArchive.Read));
        }
        catch
        {
            // Ingredients already decoded (each owning its own variant images) before a later
            // one threw have no other owner yet — dispose them before the exception propagates.
            foreach (var ing in ingredients) ing.Dispose();
            throw;
        }
        return new LoadedRecipe { Manifest = manifest, Ingredients = ingredients };
    }

    /// <summary>Writes a Recipe to a file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The recipe's manifest.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    public static void Write(string path, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, ingredients);
    }

    /// <summary>Reads a Recipe from a file, decoding every variant PNG.</summary>
    /// <param name="path">Archive path.</param>
    /// <returns>The loaded recipe; the caller owns it and must dispose it.</returns>
    public static LoadedRecipe Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }

    /// <summary>Writes a Recipe into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The recipe's manifest.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(ZipArchive zip, RecipeManifest manifest,
        IReadOnlyList<LoadedIngredient> ingredients, CancellationToken ct = default)
    {
        await ArchiveIo.WriteManifestAsync(zip, manifest, ct);
        foreach (var ing in ingredients)
            await ArchiveIo.WriteNestedAsync(zip, $"ingredients/{ing.Manifest.Id}.igt",
                inner => IngredientArchive.WriteAsync(inner, ing.Manifest, ing.VariantImages, ct), ct);
    }

    /// <summary>Reads a Recipe from an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="ct">Cancels the read; anything already decoded is disposed first.</param>
    /// <returns>The loaded recipe; the caller owns it.</returns>
    public static async Task<LoadedRecipe> ReadAsync(ZipArchive zip, CancellationToken ct = default)
    {
        var manifest = await ArchiveIo.ReadManifestAsync<RecipeManifest>(zip, ct);
        var ingredients = new List<LoadedIngredient>();
        try
        {
            foreach (var name in ArchiveIo.EntryNamesUnder(zip, "ingredients/").OrderBy(n => n, StringComparer.Ordinal))
                ingredients.Add(await ArchiveIo.ReadNestedAsync(zip, name, IngredientArchive.ReadAsync, ct));
        }
        catch
        {
            foreach (var ing in ingredients) ing.Dispose();
            throw;
        }
        return new LoadedRecipe { Manifest = manifest, Ingredients = ingredients };
    }

    /// <summary>Writes a Recipe to a file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The recipe's manifest.</param>
    /// <param name="ingredients">Its ingredients, nested as <c>.igt</c> entries.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(string path, RecipeManifest manifest,
        IReadOnlyList<LoadedIngredient> ingredients, CancellationToken ct = default)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await WriteAsync(zip, manifest, ingredients, ct);
    }

    /// <summary>Reads a Recipe from a file.</summary>
    /// <param name="path">Archive path.</param>
    /// <param name="ct">Cancels the read; anything already decoded is disposed first.</param>
    /// <returns>The loaded recipe; the caller owns it.</returns>
    public static async Task<LoadedRecipe> ReadAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        return await ReadAsync(zip, ct);
    }
}
