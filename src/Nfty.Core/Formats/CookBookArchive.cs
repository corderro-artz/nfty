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
        return new LoadedCookBook { Manifest = manifest, Recipes = recipes };
    }
}
