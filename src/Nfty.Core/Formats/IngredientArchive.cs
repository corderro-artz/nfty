using System.IO.Compression;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Formats;

/// <summary>Reads and writes <c>.igt</c> archives — a manifest plus one PNG per variant, plus an
/// optional depth map per variant. The <c>ZipArchive</c> overloads exist so a Recipe can nest one
/// without going through a file.</summary>
public static class IngredientArchive
{
    /// <summary>Stamps <see cref="IngredientManifest.PartialAlpha"/> from the actual pixels rather
    /// than trusting whatever the caller (a writer) or the file (a reader) said. Shared by every
    /// sync/async write and read path so they cannot drift on the definition.</summary>
    private static IngredientManifest Stamped(IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages) =>
        manifest with { PartialAlpha = Opacity.HasPartialAlpha(variantImages.Values) };

    /// <summary>The bytes to write for one variant's depth map, or <c>null</c> when nothing should
    /// be written at all — either the map was never painted (flat), or painting over a since-erased
    /// pixel left it flat once the write zeroes depth over transparency.</summary>
    /// <param name="ingredientId">The owning ingredient's id, for the size-mismatch message.</param>
    /// <param name="variantId">The variant's id, for the size-mismatch message.</param>
    /// <param name="image">The variant's decoded image — the map must share its dimensions.</param>
    /// <param name="map">The depth map as authored, before transparency is zeroed.</param>
    /// <exception cref="ArgumentException"><paramref name="map"/>'s dimensions differ from
    /// <paramref name="image"/>'s — a caller bug, not a file problem.</exception>
    /// <remarks>
    /// <b>Internal, not private</b> — <c>MenuWriter</c> (<c>Nfty.Core.Publish</c>) writes the same
    /// <c>depth/</c> entries into a <c>.mnu</c> and must apply the exact same zero-over-transparency
    /// rule; a map already zeroed by an <c>.igt</c> write is not something a Menu write can rely on
    /// (the Menu is projected straight from the in-memory <see cref="LoadedIngredient.Depths"/>, not
    /// from a round trip through a written <c>.igt</c>), so the rule has to run again either way.
    /// Shared here rather than copy-pasted so the two writers cannot drift on the definition.
    /// </remarks>
    internal static byte[]? DepthBytesToWrite(string ingredientId, string variantId,
        Image<Rgba32> image, DepthMap map)
    {
        if (map.Width != image.Width || map.Height != image.Height)
            throw new ArgumentException(
                $"Depth map for ingredient '{ingredientId}' variant '{variantId}' is "
                + $"{map.Width}x{map.Height}; its image is {image.Width}x{image.Height}.");

        var bytes = map.ToBytes();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (Opacity.IsSolid(row[x])) continue;
                    int i = (y * map.Width + x) * 2;
                    bytes[i] = 0;
                    bytes[i + 1] = 0;
                }
            }
        });

        var zeroed = DepthMap.FromBytes(map.Width, map.Height, bytes);
        return zeroed.IsFlat ? null : zeroed.ToBytes();
    }

    /// <summary>Writes an Ingredient into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The ingredient's manifest. Its <c>PartialAlpha</c> is ignored — the
    /// written value is recomputed from <paramref name="variantImages"/>.</param>
    /// <param name="variantImages">One image per variant id.</param>
    /// <param name="depths">Depth maps by variant id. A variant with no entry, or whose entry is
    /// flat once zeroed over transparency, gets no <c>depth/</c> entry at all. An entry whose key
    /// is not one of this manifest's variants is ignored.</param>
    public static void Write(ZipArchive zip, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages,
        IReadOnlyDictionary<string, DepthMap> depths)
    {
        ArchiveIo.WriteManifest(zip, Stamped(manifest, variantImages));
        foreach (var v in manifest.Variants)
        {
            var image = variantImages[v.Id];
            ArchiveIo.WriteImage(zip, $"variants/{v.Id}.png", image);
            if (depths.TryGetValue(v.Id, out var map))
            {
                var bytes = DepthBytesToWrite(manifest.Id, v.Id, image, map);
                if (bytes is not null)
                    ArchiveIo.WriteBytes(zip, $"depth/{v.Id}.dep", bytes);
            }
        }
    }

    /// <summary>For each variant, the wrong-length message <see cref="ReadDepths"/> and its async
    /// twin both throw — shared so they cannot drift on wording, and reused by
    /// <see cref="ArchiveIo.TryReadBytes"/>'s early, pre-read refusal so a mismatch is reported in
    /// the exact same words whether it is caught before or after the entry is inflated.</summary>
    private static InvalidDataException WrongLength(
        string ingredientId, string variantId, long len, Image<Rgba32> image) =>
        new($"Ingredient '{ingredientId}' variant '{variantId}' has a {len}-byte depth map; "
            + $"its {image.Width}x{image.Height} image needs {image.Width * image.Height * 2}.");

    /// <summary>Reads every variant's depth map, when it has one.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The ingredient's manifest.</param>
    /// <param name="images">The already-decoded variant images, to check each map's size against.</param>
    /// <exception cref="InvalidDataException">A stored depth entry's length disagrees with its
    /// variant image's dimensions.</exception>
    private static Dictionary<string, DepthMap> ReadDepths(
        ZipArchive zip, IngredientManifest manifest, IReadOnlyDictionary<string, Image<Rgba32>> images)
    {
        var depths = new Dictionary<string, DepthMap>();
        foreach (var v in manifest.Variants)
        {
            var image = images[v.Id];
            int expected = image.Width * image.Height * 2;
            // The size check happens twice on purpose: ArchiveIo refuses early, against the entry's
            // stored (uncompressed) length, before a hostile or malformed entry is inflated at all;
            // this one stays as the authority, checking the bytes that were actually read.
            var bytes = ArchiveIo.TryReadBytes(zip, $"depth/{v.Id}.dep", expected,
                len => WrongLength(manifest.Id, v.Id, len, image));
            if (bytes is null) continue;
            if (bytes.Length != expected)
                throw WrongLength(manifest.Id, v.Id, bytes.Length, image);
            depths[v.Id] = DepthMap.FromBytes(image.Width, image.Height, bytes);
        }
        return depths;
    }

    /// <summary>Reads every variant's depth map, when it has one.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The ingredient's manifest.</param>
    /// <param name="images">The already-decoded variant images, to check each map's size against.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <exception cref="InvalidDataException">A stored depth entry's length disagrees with its
    /// variant image's dimensions.</exception>
    private static async Task<Dictionary<string, DepthMap>> ReadDepthsAsync(
        ZipArchive zip, IngredientManifest manifest, IReadOnlyDictionary<string, Image<Rgba32>> images,
        CancellationToken ct)
    {
        var depths = new Dictionary<string, DepthMap>();
        foreach (var v in manifest.Variants)
        {
            var image = images[v.Id];
            int expected = image.Width * image.Height * 2;
            var bytes = await ArchiveIo.TryReadBytesAsync(zip, $"depth/{v.Id}.dep", ct, expected,
                len => WrongLength(manifest.Id, v.Id, len, image));
            if (bytes is null) continue;
            if (bytes.Length != expected)
                throw WrongLength(manifest.Id, v.Id, bytes.Length, image);
            depths[v.Id] = DepthMap.FromBytes(image.Width, image.Height, bytes);
        }
        return depths;
    }

    /// <summary>Reads an Ingredient from an already-open archive, decoding every variant PNG.</summary>
    /// <param name="zip">The open archive.</param>
    /// <returns>The loaded ingredient; the caller owns it. Its manifest's <c>PartialAlpha</c> is
    /// recomputed from the decoded images rather than trusting the stored value.</returns>
    /// <exception cref="InvalidDataException">A stored depth entry's length disagrees with its
    /// variant image's dimensions.</exception>
    public static LoadedIngredient Read(ZipArchive zip)
    {
        var manifest = ArchiveIo.ReadManifest<IngredientManifest>(zip);
        EnsureUniqueVariantIds(manifest);
        var images = DecodeVariants(zip, manifest);
        Dictionary<string, DepthMap> depths;
        try
        {
            depths = ReadDepths(zip, manifest, images);
        }
        catch
        {
            foreach (var img in images.Values) img.Dispose();
            throw;
        }
        return new LoadedIngredient { Manifest = Stamped(manifest, images), VariantImages = images, Depths = depths };
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
    /// <param name="depths">Depth maps by variant id.</param>
    public static void Write(string path, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages,
        IReadOnlyDictionary<string, DepthMap> depths)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest, variantImages, depths);
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
    /// <param name="depths">Depth maps by variant id. See the sync overload for the write rules.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(ZipArchive zip, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages,
        IReadOnlyDictionary<string, DepthMap> depths, CancellationToken ct = default)
    {
        await ArchiveIo.WriteManifestAsync(zip, Stamped(manifest, variantImages), ct);
        foreach (var v in manifest.Variants)
        {
            var image = variantImages[v.Id];
            await ArchiveIo.WriteImageAsync(zip, $"variants/{v.Id}.png", image, ct);
            if (depths.TryGetValue(v.Id, out var map))
            {
                var bytes = DepthBytesToWrite(manifest.Id, v.Id, image, map);
                if (bytes is not null)
                    await ArchiveIo.WriteBytesAsync(zip, $"depth/{v.Id}.dep", bytes, ct);
            }
        }
    }

    /// <summary>Reads an Ingredient from an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="ct">Cancels the read; anything already decoded is disposed first.</param>
    /// <returns>The loaded ingredient; the caller owns it. Its manifest's <c>PartialAlpha</c> is
    /// recomputed from the decoded images rather than trusting the stored value.</returns>
    /// <exception cref="InvalidDataException">A stored depth entry's length disagrees with its
    /// variant image's dimensions.</exception>
    public static async Task<LoadedIngredient> ReadAsync(ZipArchive zip, CancellationToken ct = default)
    {
        var manifest = await ArchiveIo.ReadManifestAsync<IngredientManifest>(zip, ct);
        EnsureUniqueVariantIds(manifest);
        var images = await DecodeVariantsAsync(zip, manifest, ct);
        Dictionary<string, DepthMap> depths;
        try
        {
            depths = await ReadDepthsAsync(zip, manifest, images, ct);
        }
        catch
        {
            foreach (var img in images.Values) img.Dispose();
            throw;
        }
        return new LoadedIngredient
        {
            Manifest = Stamped(manifest, images),
            VariantImages = images,
            Depths = depths,
        };
    }

    /// <summary>Writes an Ingredient to a file.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The ingredient's manifest.</param>
    /// <param name="variantImages">One image per variant id.</param>
    /// <param name="depths">Depth maps by variant id.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(string path, IngredientManifest manifest,
        IReadOnlyDictionary<string, Image<Rgba32>> variantImages,
        IReadOnlyDictionary<string, DepthMap> depths, CancellationToken ct = default)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        await WriteAsync(zip, manifest, variantImages, depths, ct);
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
