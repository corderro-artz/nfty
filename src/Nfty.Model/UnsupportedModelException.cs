namespace Nfty.Model;

/// <summary>
/// A model's content is malformed, truncated, structurally invalid or uses a feature outside
/// <see cref="GltfReader"/>'s supported <see cref="ModelWriter"/> subset. The reason is named,
/// never guessed at. Ordinary file I/O and permission failures are not represented by this type.
/// </summary>
public sealed class UnsupportedModelException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="feature">The unsupported feature or malformed content, worded for a reader
    /// (e.g. "more than one mesh" or "a truncated GLB chunk").</param>
    public UnsupportedModelException(string feature)
        : base($"This model cannot be read: it uses {feature}, and nfty only reads models nfty wrote.")
    {
        Feature = feature;
    }

    /// <summary>The unsupported feature or malformed content that was refused.</summary>
    public string Feature { get; }
}
