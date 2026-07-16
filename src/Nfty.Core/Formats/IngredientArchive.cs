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
        var images = manifest.Variants.ToDictionary(
            v => v.Id, v => ArchiveIo.ReadImage(zip, $"variants/{v.Id}.png"));
        return new LoadedIngredient { Manifest = manifest, VariantImages = images };
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
        var images = new Dictionary<string, Image<Rgba32>>(manifest.Variants.Count);
        foreach (var v in manifest.Variants)
            images[v.Id] = await ArchiveIo.ReadImageAsync(zip, $"variants/{v.Id}.png", ct);
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
