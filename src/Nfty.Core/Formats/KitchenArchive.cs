using System.IO.Compression;
using Nfty.Core.Model;

namespace Nfty.Core.Formats;

/// <summary>
/// Reads and writes the <c>.ktn</c> workspace file. Same shape as the other three archives — a ZIP
/// with a <c>manifest.json</c> — even though a Kitchen holds no images. Consistency buys the shared
/// <see cref="ArchiveIo"/> path, the schema gate, and one more case in <see cref="Archives.KindOf"/>
/// rather than one more concept; and a future Kitchen-level asset has somewhere to go.
/// </summary>
public static class KitchenArchive
{
    public static void Write(ZipArchive zip, KitchenManifest manifest) =>
        ArchiveIo.WriteManifest(zip, manifest);

    public static KitchenManifest Read(ZipArchive zip) =>
        ArchiveIo.ReadManifest<KitchenManifest>(zip);

    public static void Write(string path, KitchenManifest manifest)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest);
    }

    public static KitchenManifest Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }

    public static async Task WriteAsync(string path, KitchenManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        await ArchiveIo.WriteManifestAsync(zip, manifest, cancellationToken);
    }

    public static async Task<KitchenManifest> ReadAsync(string path,
        CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        return await ArchiveIo.ReadManifestAsync<KitchenManifest>(zip, cancellationToken);
    }
}
