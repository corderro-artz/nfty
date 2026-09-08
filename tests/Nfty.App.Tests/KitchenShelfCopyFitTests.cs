using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Kitchen shelf's zero-state copy fits the band it is pinned inside, at every width.
/// </summary>
/// <remarks>
/// <para>The band is 52px and all three shelf states share it, which is what makes opening a Kitchen
/// change nothing about the screen's geometry. The copy did NOT fit: <c>.zp</c> is authored for a
/// 12px centered paragraph and states a 19.2 line box to match, and the shelf overrode the font to
/// 10.5 while inheriting that leading — 1.86x. One line was fine; the moment the sentence wrapped to
/// two, which is any window under about 1150 of page width, the block wanted 54px of 52 and was
/// arranged flush against both dashed edges. It reads as a panel with its bottom cut off, and it is
/// the state a first-time user meets, since nobody has a Kitchen open yet.</para>
///
/// <para>Measured against the DASHED RECTANGLE rather than the row, because the rectangle is what a
/// reader sees the text overflow: a Panel is happy to arrange a child at whatever it was given.</para>
/// </remarks>
public class KitchenShelfCopyFitTests
{
    /// <summary>The page width at the smallest window, and two wider ones.</summary>
    public static TheoryData<double> Widths => new()
    {
        (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
        1200,
        1400,
    };

    [AvaloniaTheory]
    [MemberData(nameof(Widths))]
    public void The_zero_state_copy_fits_inside_its_dashed_band(double width)
    {
        var view = new Views.LandingView
        {
            DataContext = new LandingViewModel(new FakeNav(), new FakeDialogs(),
                new FilePickerService(), new RecentsService(StateStore.InMemory()),
                new CookBookSession(), _ => null!, _ => null!, (_, _, _) => null!, null),
        };
        var window = new Window
        {
            Content = view,
            Width = width,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var shelf = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("kshelf"));
            var dash = shelf.GetVisualDescendants().OfType<Rectangle>()
                .First(r => r.Classes.Contains("dashed"));
            var band = dash.GetVisualAncestors().OfType<Panel>().First();
            var copy = band.GetVisualDescendants().OfType<StackPanel>().First();

            // What it WANTS, measured unconstrained, against the band it is given. Reading the
            // arranged bounds instead would pass every time: a clipped child still reports the size
            // it was handed, which is the whole reason this defect survived to a screenshot.
            copy.Measure(new Size(band.Bounds.Width, double.PositiveInfinity));

            Assert.True(copy.DesiredSize.Height <= dash.Bounds.Height,
                $"the zero-state copy wants {copy.DesiredSize.Height:F1}px in a "
                + $"{dash.Bounds.Height:F1}px band at {width:F0} of page width");

            // And it must not merely fit - it has to sit off both dashed edges, or it reads as
            // touching the border even when nothing is cut.
            Assert.True(dash.Bounds.Height - copy.DesiredSize.Height >= 4,
                $"only {dash.Bounds.Height - copy.DesiredSize.Height:F1}px of air around the copy");
        }
        finally { window.Close(); }
    }
}
