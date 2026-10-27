using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// V-3: the validity list folds on a whole row. Fourteen problems at the smallest window left row
/// fourteen sliced by the footer, with the fold landing mid-sentence.
/// </summary>
/// <remarks>
/// Problem rows wrap, so they are not one height and "a multiple of the pitch" is the wrong
/// arithmetic: the property is that the scroller's viewport ends exactly on some row's bottom edge.
/// </remarks>
public class ValidityDialogLayoutTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(41)]
    public void Fourteen_problems_fold_on_a_row_boundary(double extraHeight)
    {
        var problems = Enumerable.Range(1, 14)
            .Select(n => $"Recipe 'cat' layerOrder references unknown ingredient 'missing-layer-{n}'"
                         + (n % 3 == 0 ? " and keeps going for long enough that this sentence wraps onto a second line." : "."))
            .ToList();
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var view = new Views.ValidityDialogView
        { DataContext = new ValidityDialogViewModel(new FakeDialogs(), book, problems) };
        var area = RisePages.MinArea;
        var window = new Window { Content = view, Width = area.Width, Height = area.Height + extraHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var scroll = view.FindControl<ScrollViewer>("ProblemScroll")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 0.5, "the fixture no longer overflows");
            var panel = ((ItemsControl)scroll.Content!).ItemsPanelRoot!;
            var edges = panel.Children.Select(c => c.Bounds.Bottom).ToList();
            Assert.True(edges.Any(e => Math.Abs(e - scroll.Viewport.Height) < 0.75),
                $"the list shows {scroll.Viewport.Height:F1}px and no row ends there "
                + $"(row edges: {string.Join(", ", edges.Select(e => e.ToString("F0")))}) - the fold cuts one in half");
        }
        finally { window.Close(); }
    }
}
