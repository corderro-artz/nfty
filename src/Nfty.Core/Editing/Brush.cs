namespace Nfty.Core.Editing;

/// <summary>Brush settings: stamp diameter in pixels and the grayscale value it paints.</summary>
public readonly record struct Brush(int Size, byte Value);
