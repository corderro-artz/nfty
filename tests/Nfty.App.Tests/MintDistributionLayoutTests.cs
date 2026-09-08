using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The mint distribution bar spans the whole panel, not one column of it.
/// </summary>
/// <remarks>
/// <para>It began as a 310px track sized by a constant multiplier, which stopped a third of the way
/// across. Stretching it fixed the multiplier but not the width, because the block still lived
/// inside the LEFT column of the two-column body — so "full width" meant full width of the metrics
/// beside it, and the bar still ended halfway across a panel whose other two sections run edge to
/// edge. Both cuts looked correct in the markup: <c>HorizontalAlignment="Stretch"</c> was doing
/// exactly what it says, in a container half the size of the one that mattered.</para>
///
/// <para>So the assertion is about the PANEL, measured from a laid-out frame: the bar starts where
/// the metrics start and ends where the DNA space ends. Stated as a relation between the three
/// rather than as a pixel width, because the body is star-sized and the number changes with the
/// window.</para>
/// </remarks>
public class MintDistributionLayoutTests
{
    [AvaloniaFact]
    public void The_bar_runs_the_full_width_of_the_panel()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var view = new Views.CookBookDetailView
        {
            DataContext = new CookBookDetailViewModel(book, () => { }, () => { }),
        };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bar = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("distbar"));

        // Measured against the IDENTITY CARD, which is the widest thing on this screen and the one
        // that defines its edges. The old version compared the bar to a metrics tile and a DNA
        // column that no longer exist as separate columns — the card is a single stack now, so the
        // question is simply whether the bar runs the whole of it.
        var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("cbk-id"));

        double Left(Visual v) => v.TranslatePoint(new Point(0, 0), view)!.Value.X;
        double Right(Visual v) => v.TranslatePoint(new Point(v.Bounds.Width, 0), view)!.Value.X;

        Assert.True(Left(bar) <= Left(card) + 1,
            $"the bar starts at {Left(bar)}, right of the card at {Left(card)}");
        Assert.True(Right(bar) >= Right(card) - 1,
            $"the bar ends at {Right(bar)}, short of the card's edge at {Right(card)}");
    }
}
