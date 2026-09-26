using System.Globalization;

namespace Nfty.Model;

/// <summary>
/// A picture with a partly transparent pixel cannot be risen. It is refused by name rather than
/// thresholded: silently rounding would give a model whose silhouette nobody drew — at the scale of
/// one item, the "half the models are malformed" outcome the Rise spec forbids.
/// </summary>
public sealed class PartialAlphaException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="itemName">What was being risen, as a person would name it.</param>
    /// <param name="x">Column of the first partial pixel, row-major.</param>
    /// <param name="y">Row of the first partial pixel.</param>
    /// <param name="alpha">That pixel's alpha.</param>
    /// <param name="count">How many partial pixels the picture has.</param>
    public PartialAlphaException(string itemName, int x, int y, byte alpha, int count)
        : base(Describe(itemName, x, y, alpha, count))
    {
        ItemName = itemName;
        X = x;
        Y = y;
        Alpha = alpha;
        Count = count;
    }

    /// <summary>What was being risen.</summary>
    public string ItemName { get; }

    /// <summary>Column of the first partial pixel.</summary>
    public int X { get; }

    /// <summary>Row of the first partial pixel.</summary>
    public int Y { get; }

    /// <summary>That pixel's alpha, 1..254.</summary>
    public byte Alpha { get; }

    /// <summary>How many partial pixels the picture has.</summary>
    public int Count { get; }

    private static string Describe(string itemName, int x, int y, byte alpha, int count) =>
        (count == 1
            ? $"{itemName} cannot be risen: pixel ({x}, {y}) is partly transparent (alpha {alpha})."
            : $"{itemName} cannot be risen: {count.ToString("N0", CultureInfo.InvariantCulture)} pixels are "
              + $"partly transparent, the first at ({x}, {y}) with alpha {alpha}.")
        + " A voxel is either there or not, so every pixel must be fully opaque or fully clear;"
        + " nfty will not round one for you.";
}
