namespace Nfty.Core.Model;

/// <summary>
/// How a layer's rolled variant image is turned into pixels.
/// </summary>
public enum LayerKind
{
    /// <summary>A grayscale value-map colorized with exactly one fixed color, deterministically.
    /// Applying the color consumes no RNG and adds no color uniqueness. Selecting the variant
    /// still consumes RNG, and distinct variants can add cross-asset uniqueness.</summary>
    Static,

    /// <summary>A grayscale value-map whose hue and saturation are ROLLED per asset from the
    /// layer's colorization. This is what makes the output space larger than the source art.</summary>
    Dynamic,

    /// <summary>A full-color RGBA image composited AS-IS and never colorized. Its colorization
    /// must be null.</summary>
    Custom,
}
