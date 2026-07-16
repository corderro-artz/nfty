using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class RecipeArchive
{
    public static void Write(ZipArchive zip, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients)
    {
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var ing in ingredients)
            ArchiveIo.WriteNested(zip, $"ingredients/{ing.Manifest.Id}.igt",
                inner => IngredientArchive.Write(inner, ing.Manifest, ing.VariantImages));
    }

    public static LoadedRecipe Read(ZipArchive zip)
    {
        var manifest = ArchiveIo.ReadManifest<RecipeManifest>(zip);
        var ingredients = ArchiveIo.EntryNamesUnder(zip, "ingredients/")
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => ArchiveIo.ReadNested(zip, n, IngredientArchive.Read))
            .ToList();
        return new LoadedRecipe { Manifest = manifest, Ingredients = ingredients };
    }

    public static void Write(string path, RecipeManifest manifest, IReadOnlyList<LoadedIngredient> ingredients)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, ingredients);
    }

    public static LoadedRecipe Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }

    public static async Task WriteAsync(ZipArchive zip, RecipeManifest manifest,
        IReadOnlyList<LoadedIngredient> ingredients, CancellationToken ct = default)
    {
        await ArchiveIo.WriteManifestAsync(zip, manifest, ct);
        foreach (var ing in ingredients)
            await ArchiveIo.WriteNestedAsync(zip, $"ingredients/{ing.Manifest.Id}.igt",
                inner => IngredientArchive.WriteAsync(inner, ing.Manifest, ing.VariantImages, ct), ct);
    }

    public static async Task<LoadedRecipe> ReadAsync(ZipArchive zip, CancellationToken ct = default)
    {
        var manifest = await ArchiveIo.ReadManifestAsync<RecipeManifest>(zip, ct);
        var ingredients = new List<LoadedIngredient>();
        foreach (var name in ArchiveIo.EntryNamesUnder(zip, "ingredients/").OrderBy(n => n, StringComparer.Ordinal))
            ingredients.Add(await ArchiveIo.ReadNestedAsync(zip, name, IngredientArchive.ReadAsync, ct));
        return new LoadedRecipe { Manifest = manifest, Ingredients = ingredients };
    }

    public static async Task WriteAsync(string path, RecipeManifest manifest,
        IReadOnlyList<LoadedIngredient> ingredients, CancellationToken ct = default)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await WriteAsync(zip, manifest, ingredients, ct);
    }

    public static async Task<LoadedRecipe> ReadAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        return await ReadAsync(zip, ct);
    }
}
