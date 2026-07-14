namespace Nfty.Core.Model;

/// <summary>
/// How a layer's rolled variant image is turned into pixels.
/// <list type="bullet">
/// <item><b>Dynamic</b> — grayscale value-map; hue/saturation are ROLLED per asset from the layer's colorization.</item>
/// <item><b>Static</b> — grayscale value-map colorized with EXACTLY ONE fixed color, deterministically (no RNG roll).</item>
/// <item><b>Custom</b> — full-color RGBA image composited AS-IS; never colorized (colorization must be null).</item>
/// </list>
/// </summary>
public enum LayerKind { Static, Dynamic, Custom }
