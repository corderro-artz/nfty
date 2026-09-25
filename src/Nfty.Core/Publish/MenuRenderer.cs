using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Core.Publish;

/// <summary>
/// Renders a rolled <see cref="AssetTicket"/> back into pixels from the <c>.mnu</c> it was rolled
/// against — the read-side counterpart to <see cref="MenuWriter"/>. <c>Nfty.Ticket</c> is
/// deliberately dependency-free and decodes no pixels, so the half of "roll a ticket, then render
/// it" that touches ImageSharp lives here rather than there, the same split <c>MenuWriter</c>'s own
/// remarks describe for writing.
/// </summary>
/// <remarks>
/// This is the ONE render loop: <c>serve</c> and <c>MenuFidelityTests</c> both call it rather than
/// each carrying their own copy, because two renderers is how "the client cooks the same dish the
/// server rolled" stops being true.
/// </remarks>
public static class MenuRenderer
{
    /// <summary>
    /// Renders a ticket: plates it into draws (<see cref="Plating.Plate"/>), crops each layer's art
    /// out of the Menu's atlas (or takes it whole, in loose mode), colorizes Dynamic/Static layers
    /// and leaves Custom ones as-is, then composites.
    /// </summary>
    /// <param name="menu">The Menu the ticket was rolled against.</param>
    /// <param name="ticket">The roll to render.</param>
    /// <returns>The composited asset. The caller owns and disposes it.</returns>
    /// <exception cref="KeyNotFoundException">See <see cref="Plating.Plate"/>.</exception>
    /// <exception cref="FormatException">See <see cref="Plating.Plate"/>.</exception>
    public static Image<Rgba32> Render(MenuArchive menu, AssetTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(ticket);

        var draws = Plating.Plate(menu, ticket);
        var layers = new List<Image<Rgba32>>(draws.Count);

        // In atlas mode every draw's ImageKey resolves to the SAME atlas.png bytes (ArtBytes
        // hands back the whole atlas regardless of which cell was asked for) — decoding it once
        // and cropping from that one decode, rather than once per layer, is what keeps a render at
        // the atlas size ceiling from repeating a ~1 GB decode per layer. Loose mode has no such
        // sharing: each cell is its own file, so each layer decodes its own image.
        Image<Rgba32>? atlas = null;
        try
        {
            foreach (var draw in draws)
            {
                Image<Rgba32> whole;
                bool ownsWhole;
                if (menu.Book.Art.Mode == MenuArtMode.Atlas)
                {
                    atlas ??= Image.Load<Rgba32>(menu.ArtBytes(draw.ImageKey));
                    whole = atlas;
                    ownsWhole = false;
                }
                else
                {
                    whole = Image.Load<Rgba32>(menu.ArtBytes(draw.ImageKey));
                    ownsWhole = true;
                }

                try
                {
                    var cell = draw.Source;
                    using var cropped = whole.Clone(
                        c => c.Crop(new Rectangle(cell.X, cell.Y, cell.Width, cell.Height)));

                    layers.Add(draw.Kind == LayerKind.Custom
                        ? cropped.Clone()
                        : Colorizer.Apply(cropped, draw.Hue, draw.Sat, draw.Model));
                }
                finally
                {
                    if (ownsWhole) whole.Dispose();
                }
            }
            return Compositor.Composite(menu.Book.Canvas, layers);
        }
        finally
        {
            atlas?.Dispose();
            foreach (var l in layers) l.Dispose();
        }
    }
}
