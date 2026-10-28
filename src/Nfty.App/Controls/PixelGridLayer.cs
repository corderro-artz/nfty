using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nfty.App.Services;

namespace Nfty.App.Controls;

/// <summary>
/// Draws the boundaries between the art's own pixels as hairlines, once a pixel is big enough on
/// screen for a line to mean something.
/// </summary>
/// <remarks>
/// <para>One control with one <see cref="Render"/>: it walks only the lines that cross its own
/// bounds, so a 1000px picture zoomed to 64 draws a screenful of lines and not a million.</para>
/// <para>It maps through the same <see cref="CanvasFrame"/> the pointer and the lattice backdrop use
/// (the view hands it <c>PixelCanvasHost.Frame()</c>), so a line is never a fraction of a pixel off
/// the art it divides. It never hit-tests: the surface under it owns the pointer.</para>
/// </remarks>
public sealed class PixelGridLayer : Control
{
    /// <summary>A pixel narrower than this on screen draws no line: a grid that fine is a smear.</summary>
    public const double MinCellPx = 6;

    /// <summary>Backs <see cref="Frame"/>.</summary>
    public static readonly StyledProperty<CanvasFrame?> FrameProperty =
        AvaloniaProperty.Register<PixelGridLayer, CanvasFrame?>(nameof(Frame));

    /// <summary>Backs <see cref="PixelWidth"/>.</summary>
    public static readonly StyledProperty<int> PixelWidthProperty =
        AvaloniaProperty.Register<PixelGridLayer, int>(nameof(PixelWidth));

    /// <summary>Backs <see cref="PixelHeight"/>.</summary>
    public static readonly StyledProperty<int> PixelHeightProperty =
        AvaloniaProperty.Register<PixelGridLayer, int>(nameof(PixelHeight));

    static PixelGridLayer()
    {
        AffectsRender<PixelGridLayer>(FrameProperty, PixelWidthProperty, PixelHeightProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<PixelGridLayer>(false);
    }

    /// <summary>Where the art is drawn, in this control's coordinates; null while there is none.</summary>
    public CanvasFrame? Frame
    {
        get => GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    /// <summary>The art's width in pixels.</summary>
    public int PixelWidth
    {
        get => GetValue(PixelWidthProperty);
        set => SetValue(PixelWidthProperty, value);
    }

    /// <summary>The art's height in pixels.</summary>
    public int PixelHeight
    {
        get => GetValue(PixelHeightProperty);
        set => SetValue(PixelHeightProperty, value);
    }

    /// <summary>Draws the lines.</summary>
    /// <param name="context">The drawing context.</param>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Frame is not { } f || PixelWidth <= 0 || PixelHeight <= 0 || f.Scale < MinCellPx) return;
        if (this.TryFindResource("LineStrongBrush", ActualThemeVariant, out var res) is not true
            || res is not ISolidColorBrush ink) return;

        var pen = new Pen(new SolidColorBrush(ink.Color, 0.55), 1);
        double right = f.OffX + PixelWidth * f.Scale, bottom = f.OffY + PixelHeight * f.Scale;
        double top = Math.Max(f.OffY, 0), left = Math.Max(f.OffX, 0);
        double lineBottom = Math.Min(bottom, Bounds.Height), lineRight = Math.Min(right, Bounds.Width);
        if (lineBottom <= top || lineRight <= left) return;

        // Only the columns and rows that cross the control: first and last index by arithmetic.
        int c0 = Math.Max(0, (int)Math.Floor((left - f.OffX) / f.Scale));
        int c1 = Math.Min(PixelWidth, (int)Math.Ceiling((lineRight - f.OffX) / f.Scale));
        for (int c = c0; c <= c1; c++)
        {
            // +0.5 puts the 1px hairline on a device pixel rather than straddling two.
            double x = Math.Round(f.OffX + c * f.Scale) + 0.5;
            context.DrawLine(pen, new Point(x, top), new Point(x, lineBottom));
        }
        int r0 = Math.Max(0, (int)Math.Floor((top - f.OffY) / f.Scale));
        int r1 = Math.Min(PixelHeight, (int)Math.Ceiling((lineBottom - f.OffY) / f.Scale));
        for (int r = r0; r <= r1; r++)
        {
            double y = Math.Round(f.OffY + r * f.Scale) + 0.5;
            context.DrawLine(pen, new Point(left, y), new Point(lineRight, y));
        }
    }
}
