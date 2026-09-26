namespace Nfty.Model;

/// <summary>One file of a written model: its name (no folder — the caller chooses where it goes) and
/// its complete bytes.</summary>
/// <param name="FileName">The file's name, e.g. <c>sprite.glb</c> or <c>sprite.bin</c>.</param>
/// <param name="Bytes">The file's contents.</param>
public sealed record ModelFile(string FileName, byte[] Bytes);
