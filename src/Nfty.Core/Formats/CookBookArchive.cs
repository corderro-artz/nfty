using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>Reads and writes <c>.cbk</c> archives — a manifest plus one nested <c>.rcp</c> per recipe.</summary>
public static class CookBookArchive
{
    /// <summary>Writes a CookBook.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The book's manifest.</param>
    /// <param name="recipes">Recipes to nest inside it.</param>
    public static void Write(string path, CookBookManifest manifest, IReadOnlyList<LoadedRecipe> recipes)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var r in recipes)
            ArchiveIo.WriteNested(zip, $"recipes/{r.Manifest.Id}.rcp",
                inner => RecipeArchive.Write(inner, r.Manifest, r.Ingredients));
    }

    /// <summary>Reads a CookBook, eagerly decoding every variant image inside it.</summary>
    /// <param name="path">Archive path.</param>
    /// <returns>The loaded book. The caller owns it and must dispose it — that frees every
    /// decoded image in the tree.</returns>
    /// <exception cref="InvalidDataException">The archive or a manifest inside it is unreadable.</exception>
    /// <exception cref="UnsupportedSchemaVersionException">It declares a newer schema than this build reads.</exception>
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

    /// <summary>Writes a CookBook.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The book's manifest.</param>
    /// <param name="recipes">Recipes to nest inside it.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when the archive is written.</returns>
    public static async Task WriteAsync(string path, CookBookManifest manifest,
        IReadOnlyList<LoadedRecipe> recipes, CancellationToken ct = default)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await ArchiveIo.WriteManifestAsync(zip, manifest, ct);
        foreach (var r in recipes)
            await ArchiveIo.WriteNestedAsync(zip, $"recipes/{r.Manifest.Id}.rcp",
                inner => RecipeArchive.WriteAsync(inner, r.Manifest, r.Ingredients, ct), ct);
    }

    /// <summary>Reads a CookBook, eagerly decoding every variant image inside it.</summary>
    /// <param name="path">Archive path.</param>
    /// <param name="ct">Cancels the read. Anything already decoded is disposed before the
    /// cancellation propagates.</param>
    /// <returns>The loaded book; the caller owns it.</returns>
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
