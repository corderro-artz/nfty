using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Buttons standing in one row are one size.
/// </summary>
/// <remarks>
/// <c>Button.accent</c> set no <c>FontSize</c>, so it inherited Fluent's 14 while every
/// <c>Button.tbtn</c> beside it was 12.5 — which made "Add recipe" 37px tall against Delete and
/// Import at 35. Two pixels, but the three sit in one row, and a row where one control is a size
/// larger reads as a mistake. Both classes now also center their content: <c>tbtn</c> was
/// left-aligned, which is invisible while a button hugs its label and wrong the moment one is
/// stretched.
/// </remarks>
public class ToolbarButtonParityTests
{
    private static (Window window, ExplorerViewModel vm, Views.ExplorerView view) Render()
    {
        var nav = new FakeNav(); var dialogs = new FakeDialogs(); var session = new CookBookSession();
        var vm = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
            new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    [AvaloniaFact]
    public void The_toolbar_buttons_are_all_one_height_and_one_size()
    {
        var (window, vm, view) = Render();
        try
        {
            var row = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Bounds.Height > 20 && b.Classes.Any(c => c is "accent" or "tbtn"))
                .Where(b => b.GetVisualDescendants().OfType<TextBlock>().Any())
                .GroupBy(b => ((Visual)b).TranslatePoint(default, view)!.Value.Y)
                .OrderBy(g => g.Key)
                .First()
                .ToList();

            Assert.True(row.Count >= 3, $"expected the toolbar's three buttons, found {row.Count}");
            foreach (var b in row)
            {
                Assert.Equal(row[0].Bounds.Height, b.Bounds.Height, 1);
                Assert.Equal(row[0].FontSize, b.FontSize);
            }
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>Both button classes center their label, so a stretched one does not slide to an edge.</summary>
    [AvaloniaFact]
    public void Both_button_classes_center_their_content()
    {
        var (window, vm, view) = Render();
        try
        {
            var buttons = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Any(c => c is "accent" or "tbtn"))
                .ToList();

            Assert.NotEmpty(buttons);
            Assert.All(buttons, b => Assert.Equal(HorizontalAlignment.Center, b.HorizontalContentAlignment));
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
