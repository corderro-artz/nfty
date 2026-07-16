using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

public static class CookBookArchive
{
    public static void Write(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var r in recipes)
            ArchiveIo.WriteNested(zip, $"recipes/{r.Manifest.Id}.rcp",
                inner => RecipeArchive.Write(inner, r.Manifest, r.Ingredients));
    }

    public static LoadedCookBook Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifest = ArchiveIo.ReadManifest<CookBookManifest>(zip);
        var recipes = ArchiveIo.EntryNamesUnder(zip, "recipes/")
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => ArchiveIo.ReadNested(zip, n, RecipeArchive.Read))
            .ToList();
        return new LoadedCookBook
        {
            Manifest = manifest,
            Recipes = recipes,
            SourceSha256 = ArchiveIo.HashFile(path),
        };
    }

    public static async Task WriteAsync(string path, CookBookManifest manifest,
        IReadOnlyList<LoadedRecipe> recipes, CancellationToken ct = default)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await ArchiveIo.WriteManifestAsync(zip, manifest, ct);
        foreach (var r in recipes)
            await ArchiveIo.WriteNestedAsync(zip, $"recipes/{r.Manifest.Id}.rcp",
                inner => RecipeArchive.WriteAsync(inner, r.Manifest, r.Ingredients, ct), ct);
    }

    public static async Task<LoadedCookBook> ReadAsync(string path, CancellationToken ct = default)
    {
        var recipes = new List<LoadedRecipe>();
        CookBookManifest manifest;

        using (var zip = ZipFile.OpenRead(path))
        {
            manifest = await ArchiveIo.ReadManifestAsync<CookBookManifest>(zip, ct);
            foreach (var name in ArchiveIo.EntryNamesUnder(zip, "recipes/").OrderBy(n => n, StringComparer.Ordinal))
                recipes.Add(await ArchiveIo.ReadNestedAsync(zip, name, RecipeArchive.ReadAsync, ct));
        }

        return new LoadedCookBook
        {
            Manifest = manifest,
            Recipes = recipes,
            SourceSha256 = await ArchiveIo.HashFileAsync(path, ct),
        };
    }
}
