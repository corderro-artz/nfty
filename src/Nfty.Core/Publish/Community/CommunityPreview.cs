using Nfty.Core.Imaging;
using Nfty.Core.Output;
using SixLabors.ImageSharp;

namespace Nfty.Core.Publish.Community;

/// <summary>
/// The preview sheet a shared Set carries: its first assets stitched into one PNG that holds its own
/// <c>nfty-grid</c> chunk, never more than <see cref="ShareRules.MaxPreviewSide"/> pixels a side.
/// </summary>
/// <remarks>
/// It is the ordinary <see cref="SpriteSheet"/> over a prefix of the Set, so a preview is addressable
/// exactly as a full sheet is: cell <c>n</c> is asset <c>n + 1</c>. Nothing here resamples.
/// </remarks>
public static class CommunityPreview
{
    /// <summary>The most assets a preview shows, however small the art.</summary>
    private const int MaxCells = 64;

    /// <summary>How many assets the preview holds.</summary>
    /// <param name="count">How many assets the Set has.</param>
    /// <param name="cellW">The canvas width.</param>
    /// <param name="cellH">The canvas height.</param>
    /// <returns>The largest n up to 64 and up to <paramref name="count"/> whose grid stays within the side limit; 0 when not even one cell fits or there is nothing to show.</returns>
    public static int CellCount(int count, int cellW, int cellH)
    {
        if (count <= 0 || cellW <= 0 || cellH <= 0) return 0;
        for (int n = Math.Min(MaxCells, count); n >= 1; n--)
        {
            var layout = SpriteSheet.Fit(n, cellW, cellH);
            if (layout.Width <= ShareRules.MaxPreviewSide && layout.Height <= ShareRules.MaxPreviewSide)
                return n;
        }
        return 0;
    }

    /// <summary>Writes the preview sheet of a Set.</summary>
    /// <param name="items">The Set's assets.</param>
    /// <param name="destination">The <c>.png</c> to write.</param>
    /// <param name="ct">Stops between assets.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException">There is nothing to show, or the canvas is too large for even one cell.</exception>
    /// <remarks>Takes the first cells in Set (number) order. The cell is the Set's canvas, read from the first asset's header.</remarks>
    public static async Task WriteAsync(IReadOnlyList<SetItem> items, string destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var ordered = items.OrderBy(i => i.Number).ToList();
        if (ordered.Count == 0)
            throw new ArgumentException("A preview needs at least one asset.", nameof(items));

        var info = ImageFormats.Identify(ordered[0].ImagePath);
        int n = CellCount(ordered.Count, info.Width, info.Height);
        if (n == 0)
            throw new ArgumentException("The canvas is too large for a preview sheet.", nameof(items));

        var layout = SpriteSheet.Fit(n, info.Width, info.Height);
        await SpriteSheet.WriteAsync(ordered.Take(n).ToList(), layout, destination, null, ct)
            .ConfigureAwait(false);
    }

    /// <summary>Checks a preview file against the Set's canvas.</summary>
    /// <param name="pngPath">The preview.</param>
    /// <param name="canvasW">The Set's canvas width.</param>
    /// <param name="canvasH">The Set's canvas height.</param>
    /// <returns>No problems, or the single <see cref="ShareRules.PreviewBad"/> sentence. Never throws.</returns>
    public static IReadOnlyList<string> Check(string pngPath, int canvasW, int canvasH)
    {
        IReadOnlyList<string> bad = new[] { ShareRules.PreviewBad };
        try
        {
            if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath)) return bad;
            if (new FileInfo(pngPath).Length > ShareRules.MaxPreviewBytes) return bad;

            var info = ImageFormats.Identify(pngPath);
            if (info.Width > ShareRules.MaxPreviewSide || info.Height > ShareRules.MaxPreviewSide) return bad;

            var grid = SheetGrid.Read(pngPath);
            if (grid is null || grid.CellWidth != canvasW || grid.CellHeight != canvasH) return bad;

            return Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or UnknownImageFormatException or InvalidImageContentException
                                       or SixLabors.ImageSharp.UnknownImageFormatException)
        {
            return bad;
        }
    }
}
