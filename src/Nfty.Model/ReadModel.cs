namespace Nfty.Model;

/// <summary>A model read back from a file: the solid and the encoded PNG its UVs address. The
/// constructor retains the supplied mutable byte array. No decoded image is held or needs disposal;
/// callers own any image they decode from these bytes.</summary>
/// <param name="Mesh">The solid.</param>
/// <param name="TexturePng">The texture's PNG bytes exactly as the file held them.</param>
public sealed record ReadModel(Mesh Mesh, byte[] TexturePng);
