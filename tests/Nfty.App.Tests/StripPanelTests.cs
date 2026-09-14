using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Controls;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// <see cref="StripPanel"/>'s two rules, measured on synthetic children rather than on a screen.
/// </summary>
/// <remarks>
/// The editor's own strips are asserted by <see cref="ToolstripLayoutTests"/> and
/// <see cref="PaletteStripLayoutTests"/>, at the widths the app actually gives them. This file is
/// the layer under those: it states the break rule in isolation, with widths chosen so a greedy
/// panel and a balanced one give visibly different answers — which is what makes the rule
/// deleted-line-proof rather than merely true today.
/// </remarks>
public class StripPanelTests
{
    private static Control Cell(double width, double height = 20, bool elastic = false,
        double max = double.PositiveInfinity)
    {
        // MinWidth, not Width: a child with a stated Width is pinned to it however wide a slot it
        // is arranged into, so an elastic one would never appear to grow and the fixture would be
        // testing itself. The strips' own elastic children state a floor for the same reason.
        var c = new Border { MinWidth = width, Height = height };
        if (elastic) StripPanel.SetElastic(c, true);
        if (!double.IsInfinity(max)) c.MaxWidth = max;
        return c;
    }

    private static StripPanel Laid(double width, params Control[] kids)
    {
        var panel = new StripPanel { Spacing = 10, LineSpacing = 4 };
        foreach (var k in kids) panel.Children.Add(k);
        var window = new Window { Content = panel, Width = width + 200, Height = 400 };
        // A Panel stretches, so the host is what fixes the width the breaks are computed against.
        panel.Width = width;
        panel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        panel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return panel;
    }

    /// <summary>Rows in reading order, each as the list of children on it.</summary>
    private static List<List<Control>> Rows(StripPanel panel) => StripLines.Of(panel);

    [AvaloniaFact]
    public void Everything_that_fits_stays_on_one_line()
    {
        var panel = Laid(400, Cell(100), Cell(100), Cell(100));
        Assert.Single(Rows(panel));
    }

    /// <summary>
    /// THE BREAK IS BALANCED, NOT GREEDY — the defect the whole class exists for.
    /// </summary>
    /// <remarks>
    /// 200 + 200 + 60 in a 470px row: greedy fills the first line to 410 and strands the 60px group
    /// alone on a line with 410px of empty row beside it, which is exactly what the palette strip
    /// did at the window the app opens at. Balanced puts one group per line and leaves two
    /// comparable gaps. Probe it by replacing the cost with the greedy walk: this fails and
    /// <c>Everything_that_fits_stays_on_one_line</c> does not.
    /// </remarks>
    [AvaloniaFact]
    public void A_break_is_placed_where_it_leaves_the_least_visible_gap()
    {
        var rows = Rows(Laid(470, Cell(200), Cell(200), Cell(60)));

        Assert.Equal(2, rows.Count);
        Assert.Single(rows[0]);                        // greedy would have put two here
        Assert.Equal(2, rows[1].Count);
    }

    /// <summary>
    /// Slack a line's elastic child can absorb is not a gap, so a line that HAS one is free to be
    /// filled greedily. Without that distinction the balancer would move groups around to even out
    /// space that is never visible.
    /// </summary>
    [AvaloniaFact]
    public void Slack_an_elastic_child_absorbs_does_not_count_as_a_gap()
    {
        var stretchy = Cell(60, elastic: true);
        var rows = Rows(Laid(470, Cell(200), Cell(200), stretchy));

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Count);                 // the 410px line is flush, not ragged
        Assert.Single(rows[1]);
        Assert.Equal(470, stretchy.Bounds.Width, 1);    // it took the whole line
    }

    [AvaloniaFact]
    public void An_elastic_child_grows_no_further_than_its_own_MaxWidth()
    {
        var stretchy = Cell(60, elastic: true, max: 120);
        Laid(470, Cell(100), stretchy);
        Assert.Equal(120, stretchy.Bounds.Width, 1);
    }

    /// <summary>
    /// Only the FIRST elastic child on a line grows. A strip that marks two does something a reader
    /// can predict off the markup rather than splitting the difference.
    /// </summary>
    [AvaloniaFact]
    public void Only_the_first_elastic_child_on_a_line_takes_the_slack()
    {
        var first = Cell(60, elastic: true);
        var second = Cell(60, elastic: true);
        Laid(400, first, second);

        Assert.Equal(330, first.Bounds.Width, 1);
        Assert.Equal(60, second.Bounds.Width, 1);
    }

    /// <summary>
    /// A group wider than the line gets the whole line. It overhangs rather than being dropped —
    /// which is the failure a Grid star column has here, and the reason the palette's saved run was
    /// once arranged at zero width and simply was not drawn.
    /// </summary>
    [AvaloniaFact]
    public void A_group_wider_than_the_line_still_gets_a_line_of_its_own()
    {
        var wide = Cell(300);
        var rows = Rows(Laid(200, Cell(100), wide));

        Assert.Equal(2, rows.Count);
        Assert.Equal(300, wide.Bounds.Width, 1);
    }

    /// <summary>The panel asks for exactly the lines it laid out, so a fixed-height row cannot cut
    /// one off — the strips opt out of <c>.pane-hrow</c>'s 41px for this reason.</summary>
    [AvaloniaFact]
    public void The_panel_asks_for_the_height_of_every_line_it_wraps_onto()
    {
        var panel = Laid(200, Cell(100, 20), Cell(150, 30));
        Assert.Equal(2, Rows(panel).Count);
        Assert.Equal(20 + 4 + 30, panel.Bounds.Height, 1);
    }
}

/// <summary>
/// Reads the LINES a <see cref="StripPanel"/> actually arranged, off the frame.
/// </summary>
/// <remarks>
/// Shared by the three files that assert about a strip. Grouping by a rounded Y centre is the
/// obvious way and it is wrong: the panel centres each child on its line, so a 33px tray and a 24px
/// swatch run on the same line have centres half a pixel apart and round to different keys — which
/// reported the mode tray as alone on a line and failed a passing layout. Two children are on one
/// line when their vertical extents OVERLAP, which is what "on one line" means.
/// </remarks>
internal static class StripLines
{
    internal static List<List<Control>> Of(StripPanel panel)
    {
        var lines = new List<List<Control>>();
        double bottom = double.NegativeInfinity;

        foreach (var c in panel.Children.OfType<Control>().OrderBy(c => c.Bounds.Y).ThenBy(c => c.Bounds.X))
        {
            if (lines.Count == 0 || c.Bounds.Y >= bottom - 0.5)
            {
                lines.Add(new List<Control>());
                bottom = c.Bounds.Bottom;
            }
            else bottom = Math.Max(bottom, c.Bounds.Bottom);
            lines[^1].Add(c);
        }

        foreach (var line in lines) line.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
        return lines;
    }

    /// <summary>How far along the row the last control on a line reaches.</summary>
    internal static double Used(List<Control> line) => line.Max(c => c.Bounds.X + c.Bounds.Width);
}
