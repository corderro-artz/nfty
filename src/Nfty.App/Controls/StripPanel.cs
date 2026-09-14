using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Nfty.App.Controls;

/// <summary>
/// A toolbar row that wraps by GROUP and gives its slack to one child.
/// </summary>
/// <remarks>
/// <para><b>Why not <c>WrapPanel</c>.</b> A WrapPanel wraps between any two children, and the editor's
/// strips hand it thirteen loose controls — so the break landed wherever the arithmetic put it. At the
/// window the app OPENS at, the toolstrip wanted 557px of a 556px pane and broke after the swatch,
/// leaving the brush-size box alone on a second line with six hundred pixels of empty row beside it.
/// That is not a wrapped toolbar, it is a broken one, and it missed fitting by a single pixel.</para>
///
/// <para><b>Three rules, and they are what make a strip read as designed rather than as arithmetic.</b>
/// Children here are GROUPS — tools, history, ink — so a break always falls between groups and every
/// line is a whole idea. One child per line may be <see cref="ElasticProperty">elastic</see>: it
/// absorbs whatever is left over, up to its own <see cref="Layoutable.MaxWidth"/> — the value ramp
/// and the saved-swatch run are the controls here whose meaning improves with length, so a wide
/// window spends its slack on them instead of leaving a void at the end of the row. And the breaks
/// are BALANCED rather than greedy: see <c>Breaks</c> for why filling each line to the brim is what
/// makes a wrapped toolbar look broken.</para>
///
/// <para><b>It never scrolls and never clips.</b> A group wider than the line still gets the whole
/// line rather than being cut at the edge, which is the failure mode a Grid star column has here (see
/// the palette strip's own history: a star given nothing was arranged at zero width and the saved
/// swatches were simply not drawn).</para>
/// </remarks>
public class StripPanel : Panel
{
    /// <summary>Gap between children on one line.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<StripPanel, double>(nameof(Spacing), 8);

    /// <summary>Gap between lines when the row wraps.</summary>
    public static readonly StyledProperty<double> LineSpacingProperty =
        AvaloniaProperty.Register<StripPanel, double>(nameof(LineSpacing), 6);

    /// <summary>Marks the child that absorbs the slack left on its line.</summary>
    public static readonly AttachedProperty<bool> ElasticProperty =
        AvaloniaProperty.RegisterAttached<StripPanel, Control, bool>("Elastic");

    static StripPanel()
    {
        AffectsMeasure<StripPanel>(SpacingProperty, LineSpacingProperty);
        AffectsMeasure<StripPanel>(ElasticProperty);
    }

    /// <summary>Gap between children on one line.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gap between lines when the row wraps.</summary>
    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    /// <summary>Marks a child as the one that absorbs its line's slack.</summary>
    /// <param name="control">The child.</param>
    /// <param name="value">Whether it is elastic.</param>
    public static void SetElastic(Control control, bool value) => control.SetValue(ElasticProperty, value);

    /// <summary>Whether a child absorbs its line's slack.</summary>
    /// <param name="control">The child.</param>
    /// <returns>True when it is elastic.</returns>
    public static bool GetElastic(Control control) => control.GetValue(ElasticProperty);

    /// <summary>One wrapped line: a span of children and the size they came to.</summary>
    private readonly record struct Line(int First, int Count, double Width, double Height);

    /// <summary>
    /// How much of a line's leftover width is still VISIBLE as a gap after the elastic child on it
    /// has taken what it can.
    /// </summary>
    /// <remarks>
    /// This is the number a reader actually sees, and it is what the break points are chosen to
    /// minimize. Slack on a line whose elastic child can absorb it is not a hole in the toolbar —
    /// the ramp or the saved run simply gets longer — so counting raw slack would rate a line with
    /// an elastic child as ragged when it is flush.
    /// </remarks>
    private static double Residual(double width, double lineWidth, double elasticRoom)
    {
        double slack = width - lineWidth;
        if (slack <= 0) return 0;
        return double.IsInfinity(elasticRoom) ? 0 : Math.Max(0, slack - elasticRoom);
    }

    /// <summary>The growth the first elastic child in a span can take, or zero if the span has
    /// none.</summary>
    private static double RoomIn(IReadOnlyList<Control> kids, int first, int count)
    {
        for (int i = first; i < first + count; i++)
        {
            if (!GetElastic(kids[i])) continue;
            double max = kids[i].MaxWidth;
            return double.IsInfinity(max)
                ? double.PositiveInfinity
                : Math.Max(0, max - kids[i].DesiredSize.Width);
        }
        return 0;
    }

    /// <summary>
    /// Breaks the visible children into lines at the given width, choosing the breaks that leave the
    /// least visible gap.
    /// </summary>
    /// <remarks>
    /// <para><b>Greedy is what made a wrapped strip look broken.</b> Filling each line until the next
    /// group misses puts every hole at the END, and the hole is as big as whatever did not fit: at
    /// the window the app OPENS at, the palette strip's first three groups came to 523px of a 532px
    /// row and the fourth — the alpha axis and its lock — took a line of its own with four hundred
    /// pixels of empty row beside it. Two 180px gaps read as a toolbar with room in it; one 400px gap
    /// reads as a control that fell off.</para>
    ///
    /// <para>So the breaks are chosen to minimize the sum of SQUARED residual gap. Squared rather
    /// than linear because the complaint is not about total emptiness — which is fixed, being the
    /// row's width less its content — but about emptiness <i>concentrated in one place</i>, and only
    /// a convex penalty says so. Two lines beat three under this rule automatically: moving a group
    /// off a line only ever widens that line's gap, so an extra line never pays for itself and the
    /// panel still uses the fewest lines the width allows.</para>
    ///
    /// <para>Measure and arrange must agree exactly about where the breaks fall, so both call this
    /// rather than each walking the children themselves — two copies of a break rule is two rules.
    /// It is O(n^2) over GROUPS, of which a strip has a handful.</para>
    /// </remarks>
    private List<Line> Breaks(double width, IReadOnlyList<Control> kids)
    {
        int n = kids.Count;
        var lines = new List<Line>();
        if (n == 0) return lines;

        // Running width of children [0..i), so a span's width is one subtraction plus its gaps.
        var run = new double[n + 1];
        for (int i = 0; i < n; i++) run[i + 1] = run[i] + kids[i].DesiredSize.Width;
        double Span(int first, int count) => run[first + count] - run[first] + Spacing * (count - 1);

        // best[j] is the least cost of laying out the first j children, rows[j] how many lines that
        // took, and from[j] where the last of them starts.
        var best = new double[n + 1];
        var rows = new int[n + 1];
        var from = new int[n + 1];
        for (int j = 1; j <= n; j++) { best[j] = double.PositiveInfinity; rows[j] = int.MaxValue; }

        for (int j = 1; j <= n; j++)
        {
            for (int i = 0; i < j; i++)
            {
                // A single group wider than the line still gets the whole line - it is laid out and
                // overhangs rather than being silently dropped, which is the one failure mode a Grid
                // star column has here. Any longer span is infeasible, and spans only shorten as i
                // rises, so this skips rather than stops.
                double w = Span(i, j - i);
                if (w > width + 0.5 && j - i > 1) continue;

                double r = Residual(width, w, RoomIn(kids, i, j - i));
                double cost = best[i] + r * r;
                int lines2 = rows[i] == int.MaxValue ? 1 : rows[i] + 1;

                // FEWER LINES BREAKS A TIE, stated rather than left to the iteration order. Two
                // elastic groups both absorb their whole line, so one line and two lines can cost
                // exactly zero - and a panel that answered "two" there would wrap a strip that fits.
                if (cost < best[j] - 1e-9 || (cost < best[j] + 1e-9 && lines2 < rows[j]))
                {
                    best[j] = cost;
                    rows[j] = lines2;
                    from[j] = i;
                }
            }
        }

        // Walk the chosen breaks back out, then reverse into reading order.
        for (int j = n; j > 0; j = from[j])
        {
            int i = from[j];
            double h = 0;
            for (int k = i; k < j; k++) h = Math.Max(h, kids[k].DesiredSize.Height);
            lines.Add(new Line(i, j - i, Span(i, j - i), h));
        }
        lines.Reverse();
        return lines;
    }

    private List<Control> Visible()
    {
        var kids = new List<Control>(Children.Count);
        foreach (var c in Children) if (c.IsVisible) kids.Add(c);
        return kids;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var kids = Visible();
        foreach (var c in kids) c.Measure(Size.Infinity);

        double limit = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        var lines = Breaks(limit, kids);

        double w = 0, h = 0;
        foreach (var line in lines)
        {
            w = Math.Max(w, line.Width);
            h += line.Height;
        }
        if (lines.Count > 1) h += LineSpacing * (lines.Count - 1);
        return new Size(w, h);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var kids = Visible();
        var lines = Breaks(finalSize.Width, kids);

        double y = 0;
        foreach (var line in lines)
        {
            // The slack this line has left, and who gets it. Only one child per line is elastic; the
            // first one found wins, so a strip that marks two does something predictable rather than
            // splitting the difference in a way nobody can read off the markup.
            double slack = finalSize.Width - line.Width;
            int elastic = -1;
            if (slack > 0.5)
            {
                for (int i = line.First; i < line.First + line.Count; i++)
                    if (GetElastic(kids[i])) { elastic = i; break; }
            }

            // MaxWidth is the cap, and the same RoomIn the break rule costs against - so a strip
            // cannot be broken on one belief about how far a control grows and arranged on another.
            // A ramp is better longer, but not unboundedly: past a point the row is one enormous
            // slider beside a few small controls, which reads as a layout accident rather than as a
            // control that earned the space.
            double grow = elastic < 0 ? 0 : Math.Min(slack, RoomIn(kids, elastic, 1));

            double x = 0;
            for (int i = line.First; i < line.First + line.Count; i++)
            {
                var child = kids[i];
                if (i > line.First) x += Spacing;
                double w = child.DesiredSize.Width + (i == elastic ? grow : 0);
                double ch = Math.Min(child.DesiredSize.Height, line.Height);

                // Vertically centered on the line. The alpha cell states its own 40px height to keep
                // Fluent's cancelling slider margin true; centering leaves that calibration alone
                // whatever else shares the line.
                child.Arrange(new Rect(x, y + (line.Height - ch) / 2, w, ch));
                x += w;
            }

            y += line.Height + LineSpacing;
        }

        return finalSize;
    }
}
