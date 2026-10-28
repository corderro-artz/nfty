namespace Nfty.Model;

/// <summary>A model read back from a file: the solid and the PNG its UVs address.</summary>
/// <param name="Mesh">The solid.</param>
/// <param name="TexturePng">The texture's PNG bytes exactly as the file held them.</param>
public sealed record ReadModel(Mesh Mesh, byte[] TexturePng);
