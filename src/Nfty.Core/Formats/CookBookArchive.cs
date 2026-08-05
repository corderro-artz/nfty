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
        var recipes = new List<LoadedRecipe>();
        try
        {
            foreach (var name in ArchiveIo.EntryNamesUnder(zip, "recipes/").OrderBy(n => n, StringComparer.Ordinal))
                recipes.Add(ArchiveIo.ReadNested(zip, name, RecipeArchive.Read));

            // Inside the try, not after it. The hash reads the whole file and can fail — on I/O, or
            // on cancellation in the async twin — and at this point `recipes` owns every decoded
            // variant image in the book with no other owner to free them.
            return new LoadedCookBook
            {
                Manifest = manifest,
                Recipes = recipes,
                SourceSha256 = ArchiveIo.HashFile(path),
            };
        }
        catch
        {
            // Recipes already decoded (each owning its ingredients and their variant images)
            // before a later one threw have no other owner yet — dispose them before the
            // original exception propagates.
            foreach (var r in recipes) r.Dispose();
            throw;
        }
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
        try
        {
            CookBookManifest manifest;
            using (var zip = ZipFile.OpenRead(path))
            {
                manifest = await ArchiveIo.ReadManifestAsync<CookBookManifest>(zip, ct);
                foreach (var name in ArchiveIo.EntryNamesUnder(zip, "recipes/").OrderBy(n => n, StringComparer.Ordinal))
                    recipes.Add(await ArchiveIo.ReadNestedAsync(zip, name, RecipeArchive.ReadAsync, ct));
            }

            // The hash is inside the try because cancellation genuinely lands here: this method
            // takes a CancellationToken as a first-class input, a GUI passes one that fires when the
            // user navigates away, and HashFileAsync awaits the whole file. Leaving it outside
            // stranded every decoded image in the book.
            return new LoadedCookBook
            {
                Manifest = manifest,
                Recipes = recipes,
                SourceSha256 = await ArchiveIo.HashFileAsync(path, ct),
            };
        }
        catch
        {
            foreach (var r in recipes) r.Dispose();
            throw;
        }
    }
}
