namespace Nfty.Model;

/// <summary>
/// A model file uses something <see cref="GltfReader"/> does not read. The reader understands
/// exactly the shape <see cref="ModelWriter"/> writes, so anything else is refused by naming the
/// feature, never guessed at.
/// </summary>
public sealed class UnsupportedModelException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="feature">The unsupported feature, worded for a reader (e.g. "more than one mesh").</param>
    public UnsupportedModelException(string feature)
        : base($"This model cannot be read: it uses {feature}, and nfty only reads models nfty wrote.")
    {
        Feature = feature;
    }

    /// <summary>The feature that was refused.</summary>
    public string Feature { get; }
}
