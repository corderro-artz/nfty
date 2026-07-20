using System.IO.Compression;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

public static class IngredientArchive
{
    public static void Write(ZipArchive zip, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages)
    {
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var v in manifest.Variants)
            ArchiveIo.WriteImage(zip, $"variants/{v.Id}.png", variantImages[v.Id]);
    }

    public static LoadedIngredient Read(ZipArchive zip)
    {
        var manifest = ArchiveIo.ReadManifest<IngredientManifest>(zip);
        EnsureUniqueVariantIds(manifest);
        var images = new Dictionary<string, Image<Rgba32>>(manifest.Variants.Count);
        try
        {
            foreach (var v in manifest.Variants)
                images[v.Id] = ArchiveIo.ReadImage(zip, $"variants/{v.Id}.png");
        }
        catch
        {
            // A later variant's PNG can be missing/corrupt after earlier ones decoded fine.
            // Those decoded images have no other owner yet, so strand-free means disposing
            // them here before the original exception propagates.
            foreach (var img in images.Values) img.Dispose();
            throw;
        }
        return new LoadedIngredient { Manifest = manifest, VariantImages = images };
    }

    /// <summary>
    /// Variant ids must be unique within an ingredient: they key the variant-image dictionary,
    /// and a hand-edited (or otherwise malformed) archive could carry duplicates. Catch that here,
    /// before either <see cref="Read(ZipArchive)"/> or <see cref="ReadAsync(ZipArchive, CancellationToken)"/>
    /// builds that dictionary, so both fail the same way with a message that names the file's
    /// actual problem instead of a raw "same key" exception from the framework.
    /// </summary>
    private static void EnsureUniqueVariantIds(IngredientManifest manifest)
    {
        var seen = new HashSet<string>();
        foreach (var v in manifest.Variants)
            if (!seen.Add(v.Id))
                throw new InvalidDataException(
                    $"Ingredient '{manifest.Id}' has duplicate variant id '{v.Id}'; every variant must have a unique id.");
    }

    public static void Write(string path, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, variantImages);
    }

    public static LoadedIngredient Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }

    public static async Task WriteAsync(ZipArchive zip, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages, CancellationToken ct = default)
    {
        await ArchiveIo.WriteManifestAsync(zip, manifest, ct);
        foreach (var v in manifest.Variants)
            await ArchiveIo.WriteImageAsync(zip, $"variants/{v.Id}.png", variantImages[v.Id], ct);
    }

    public static async Task<LoadedIngredient> ReadAsync(ZipArchive zip, CancellationToken ct = default)
    {
        var manifest = await ArchiveIo.ReadManifestAsync<IngredientManifest>(zip, ct);
        EnsureUniqueVariantIds(manifest);
        var images = new Dictionary<string, Image<Rgba32>>(manifest.Variants.Count);
        try
        {
            foreach (var v in manifest.Variants)
                images[v.Id] = await ArchiveIo.ReadImageAsync(zip, $"variants/{v.Id}.png", ct);
        }
        catch
        {
            foreach (var img in images.Values) img.Dispose();
            throw;
        }
        return new LoadedIngredient { Manifest = manifest, VariantImages = images };
    }

    public static async Task WriteAsync(string path, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages, CancellationToken ct = default)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await WriteAsync(zip, manifest, variantImages, ct);
    }

    public static async Task<LoadedIngredient> ReadAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        return await ReadAsync(zip, ct);
    }
}
