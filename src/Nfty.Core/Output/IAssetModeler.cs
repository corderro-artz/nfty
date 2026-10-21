using Nfty.Core.Generation;

namespace Nfty.Core.Output;

/// <summary>One file a modeler produced for an asset.</summary>
/// <param name="FileName">A bare file name, written to <c>models/</c>. A name with a directory
/// part is refused.</param>
/// <param name="Bytes">The file's contents.</param>
public sealed record SetFile(string FileName, byte[] Bytes);

/// <summary>
/// Turns one finished asset into 3D model files. Lives in Core so <see cref="SetWriter"/> needn't
/// know any model library; the CLI supplies the implementation.
/// </summary>
public interface IAssetModeler
{
    /// <summary>The options the models are made with, recorded in <c>set.json</c>. Its
    /// <see cref="SetModels.Skipped"/> is ignored here; the writer fills it in.</summary>
    SetModels Options { get; }

    /// <summary>Files for one asset, named from <paramref name="baseName"/> (<c>"0007"</c> becomes
    /// <c>"0007.glb"</c>), or null when nothing in it is fully opaque. Called concurrently for
    /// different assets, so it must be a pure function of its input.</summary>
    /// <param name="asset">The finished asset.</param>
    /// <param name="baseName">The asset's zero-padded number.</param>
    /// <returns>The files, or null when the asset cannot be modeled.</returns>
    IReadOnlyList<SetFile>? Model(GeneratedAsset asset, string baseName);
}
