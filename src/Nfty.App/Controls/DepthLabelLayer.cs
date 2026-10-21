using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nfty.App.Services;
using Nfty.Core.Editing;

namespace Nfty.App.Controls;

/// <summary>
/// Prints each solid pixel's total thickness (<c>1 + front + back</c>) in the middle of its cell, once
/// the canvas is zoomed far enough for a cell to hold a number.
/// </summary>
/// <remarks>
/// <para><b>One control, one <see cref="Render"/>.</b> A TextBlock per cell would put a 512px canvas at
/// 262,144 elements; this walks only the cells that intersect its own bounds, and only at all once a
/// cell is <see cref="MinCellPx"/> wide, so at the fitted zoom of any real canvas it draws nothing and
/// costs nothing.</para>
/// <para>The ink is chosen from the ART pixel under the number (dark ink on a light pixel, light on a
/// dark one), so it is the same in both themes: the numbers sit on the artwork, not on the page.</para>
/// <para>It never hit-tests: the surface under it owns the pointer.</para>
/// </remarks>
public sealed class DepthLabelLayer : Control
{
    /// <summary>A cell narrower than this draws no label: a number that small is not a number.</summary>
    public const double MinCellPx = 18;

    /// <summary>Backs <see cref="Surface"/>.</summary>
    public static readonly StyledProperty<DepthSurface?> SurfaceProperty =
        AvaloniaProperty.Register<DepthLabelLayer, DepthSurface?>(nameof(Surface));

    /// <summary>Backs <see cref="Frame"/>.</summary>
    public static readonly StyledProperty<CanvasFrame> FrameProperty =
        AvaloniaProperty.Register<DepthLabelLayer, CanvasFrame>(nameof(Frame));

    static DepthLabelLayer()
    {
        AffectsRender<DepthLabelLayer>(SurfaceProperty, FrameProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<DepthLabelLayer>(false);
    }

    /// <summary>The depth being painted.</summary>
    public DepthSurface? Surface
    {
        get => GetValue(SurfaceProperty);
        set => SetValue(SurfaceProperty, value);
    }

    /// <summary>Where the art is drawn, in this control's coordinates.</summary>
    public CanvasFrame Frame
    {
        get => GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    /// <summary>How many labels the last <see cref="Render"/> drew; a test reads it.</summary>
    internal int LastLabelCount { get; private set; }

    // The two inks, looked up once per render pass.
    private IBrush? _onLight;
    private IBrush? _onDark;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        LastLabelCount = 0;
        var surface = Surface;
        var frame = Frame;
        if (surface is null || frame.Scale < MinCellPx) return;

        _onLight = Ink("CellLabelOnLightBrush");
        _onDark = Ink("CellLabelOnDarkBrush");
        if (_onLight is null || _onDark is null) return;

        double cell = frame.Scale;
        var bounds = new Rect(Bounds.Size);
        // The cells that intersect the control, and only those: a deep zoom of a big canvas has a
        // few dozen on screen, never the whole picture.
        int x0 = Math.Max(0, (int)Math.Floor((bounds.Left - frame.OffX) / cell));
        int y0 = Math.Max(0, (int)Math.Floor((bounds.Top - frame.OffY) / cell));
        int x1 = Math.Min(surface.Width - 1, (int)Math.Floor((bounds.Right - frame.OffX) / cell));
        int y1 = Math.Min(surface.Height - 1, (int)Math.Floor((bounds.Bottom - frame.OffY) / cell));
        if (x1 < x0 || y1 < y0) return;

        var family = (FontFamily?)(this.TryFindResource("MonoFontFamily", ActualThemeVariant, out var f) ? f : null)
                     ?? FontFamily.Default;
        var typeface = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
        double size = 0.42 * cell;

        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                if (!surface.IsSolid(x, y)) continue;
                var depth = surface.Get(x, y);
                var art = surface.ArtAt(x, y);
                double luma = 0.3 * art.R + 0.59 * art.G + 0.11 * art.B;
                var text = new FormattedText(
                    (1 + depth.Front + depth.Back).ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size,
                    luma > 125 ? _onLight : _onDark);
                var at = new Point(
                    frame.OffX + x * cell + (cell - text.Width) / 2,
                    frame.OffY + y * cell + (cell - text.Height) / 2);
                context.DrawText(text, at);
                LastLabelCount++;
            }
        }
    }

    private IBrush? Ink(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var v) ? v as IBrush : null;
}
