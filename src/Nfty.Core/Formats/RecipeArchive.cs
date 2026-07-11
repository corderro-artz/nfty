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
}
