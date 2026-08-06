using System.IO.Compression;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

/// <summary>Reads and writes <c>.igt</c> archives — a manifest plus one PNG per variant. The
/// <c>ZipArchive</c> overloads exist so a Recipe can nest one without going through a file.</summary>
public static class IngredientArchive
{
    /// <summary>Writes an Ingredient into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The ingredient's manifest.</param>
    /// <param name="variantImages">One image per variant id.</param>
    public static void Write(ZipArchive zip, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages)
    {
        ArchiveIo.WriteManifest(zip, manifest);
        foreach (var v in manifest.Variants)
            ArchiveIo.WriteImage(zip, $"variants/{v.Id}.png", variantImages[v.Id]);
    }

    /// <summary>Reads an Ingredient from an already-open archive, decoding every variant PNG.</summary>
    /// <param name="zip">The open archive.</param>
    /// <returns>The loaded ingredient; the caller owns it.</returns>
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

    /// <summary>Writes an Ingredient to a file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The ingredient's manifest.</param>
    /// <param name="variantImages">One image per variant id.</param>
    public static void Write(string path, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, variantImages);
    }

    /// <summary>Reads an Ingredient from a file, decoding every variant PNG.</summary>
    /// <param name="path">Archive path.</param>
    /// <returns>The loaded ingredient; the caller owns it and must dispose it.</returns>
    public static LoadedIngredient Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }

    /// <summary>Writes an Ingredient into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The ingredient's manifest.</param>
    /// <param name="variantImages">One image per variant id.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(ZipArchive zip, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages, CancellationToken ct = default)
    {
        await ArchiveIo.WriteManifestAsync(zip, manifest, ct);
        foreach (var v in manifest.Variants)
            await ArchiveIo.WriteImageAsync(zip, $"variants/{v.Id}.png", variantImages[v.Id], ct);
    }

    /// <summary>Reads an Ingredient from an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="ct">Cancels the read; anything already decoded is disposed first.</param>
    /// <returns>The loaded ingredient; the caller owns it.</returns>
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

    /// <summary>Writes an Ingredient to a file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The ingredient's manifest.</param>
    /// <param name="variantImages">One image per variant id.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(string path, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages, CancellationToken ct = default)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await WriteAsync(zip, manifest, variantImages, ct);
    }

    /// <summary>Reads an Ingredient from a file.</summary>
    /// <param name="path">Archive path.</param>
    /// <param name="ct">Cancels the read; anything already decoded is disposed first.</param>
    /// <returns>The loaded ingredient; the caller owns it.</returns>
    public static async Task<LoadedIngredient> ReadAsync(string path, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(path);
        return await ReadAsync(zip, ct);
    }
}
