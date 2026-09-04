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
        var images = DecodeVariants(zip, manifest);
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
        return new LoadedIngredient
        {
            Manifest = manifest,
            VariantImages = await DecodeVariantsAsync(zip, manifest, ct),
        };
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

    /// <summary>
    /// Extracts every variant PNG in manifest order, then decodes them in parallel.
    /// </summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The ingredient's manifest, which fixes the order.</param>
    /// <returns>The decoded images by variant id, inserted in manifest order.</returns>
    /// <remarks>
    /// <para>Two phases, because the two halves have opposite threading rules. A ZipArchive's
    /// entries share one stream, so extraction is sequential and always will be. Decoding is pure
    /// and is the expensive half — a 1000px PNG costs an order of magnitude more to decode than to
    /// pull out of the zip — so that is the half that runs wide.</para>
    ///
    /// <para>The dictionary is filled afterwards, in <b>manifest order</b>, never from the parallel
    /// loop. Insertion order is observable when anything enumerates VariantImages, and an order that
    /// depended on which decode finished first would be an order that varied by machine.</para>
    /// </remarks>
    private static Dictionary<string, Image<Rgba32>> DecodeVariants(
        ZipArchive zip, IngredientManifest manifest)
    {
        var variants = manifest.Variants;
        var encoded = new byte[variants.Count][];
        for (var i = 0; i < variants.Count; i++)
            encoded[i] = ArchiveIo.ReadImageBytes(zip, $"variants/{variants[i].Id}.png");

        var decoded = new Image<Rgba32>[variants.Count];
        try
        {
            ParallelWork.For(variants.Count, CancellationToken.None,
                i => decoded[i] = ArchiveIo.DecodeImage(encoded[i]));
        }
        catch
        {
            // A corrupt PNG can throw after its neighbours decoded fine. Those have no other owner
            // yet, so strand-free means disposing them before the original exception propagates.
            foreach (var img in decoded) img?.Dispose();
            throw;
        }

        var images = new Dictionary<string, Image<Rgba32>>(variants.Count);
        for (var i = 0; i < variants.Count; i++) images[variants[i].Id] = decoded[i];
        return images;
    }

    /// <summary>Extracts every variant PNG in manifest order, then decodes them in parallel.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The ingredient's manifest, which fixes the order.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The decoded images by variant id, inserted in manifest order.</returns>
    /// <inheritdoc cref="DecodeVariants" path="/remarks"/>
    private static async Task<Dictionary<string, Image<Rgba32>>> DecodeVariantsAsync(
        ZipArchive zip, IngredientManifest manifest, CancellationToken ct)
    {
        var variants = manifest.Variants;
        var encoded = new byte[variants.Count][];
        for (var i = 0; i < variants.Count; i++)
            encoded[i] = await ArchiveIo.ReadImageBytesAsync(zip, $"variants/{variants[i].Id}.png", ct);

        var decoded = new Image<Rgba32>[variants.Count];
        try
        {
            ParallelWork.For(variants.Count, ct, i => decoded[i] = ArchiveIo.DecodeImage(encoded[i]));
        }
        catch
        {
            foreach (var img in decoded) img?.Dispose();
            throw;
        }

        var images = new Dictionary<string, Image<Rgba32>>(variants.Count);
        for (var i = 0; i < variants.Count; i++) images[variants[i].Id] = decoded[i];
        return images;
    }
}
