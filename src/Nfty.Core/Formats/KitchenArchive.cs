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
    /// <summary>Writes the manifest into an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <param name="manifest">The workspace's identity.</param>
    public static void Write(ZipArchive zip, KitchenManifest manifest) =>
        ArchiveIo.WriteManifest(zip, manifest);

    /// <summary>Reads the manifest from an already-open archive.</summary>
    /// <param name="zip">The open archive.</param>
    /// <returns>The manifest.</returns>
    public static KitchenManifest Read(ZipArchive zip) =>
        ArchiveIo.ReadManifest<KitchenManifest>(zip);

    /// <summary>Writes a <c>.ktn</c>.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The workspace's identity.</param>
    public static void Write(string path, KitchenManifest manifest)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Write(zip, manifest);
    }

    /// <summary>Reads a <c>.ktn</c>.</summary>
    /// <param name="path">Archive path.</param>
    /// <returns>The manifest.</returns>
    public static KitchenManifest Read(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return Read(zip);
    }

    /// <summary>Writes a <c>.ktn</c>.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="manifest">The workspace's identity.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public static async Task WriteAsync(string path, KitchenManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        await ArchiveIo.WriteManifestAsync(zip, manifest, cancellationToken);
    }

    /// <summary>Reads a <c>.ktn</c>.</summary>
    /// <param name="path">Archive path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The manifest.</returns>
    public static async Task<KitchenManifest> ReadAsync(string path,
        CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        return await ArchiveIo.ReadManifestAsync<KitchenManifest>(zip, cancellationToken);
    }
}
