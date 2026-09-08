using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Every PAGE fits the smallest window the app opens, or scrolls what does not fit. Nothing is
/// silently cut.
/// </summary>
/// <remarks>
/// <para>Driving the app at the minimum window found three screens cut at once, none of which any
/// test could see. Landing's action column wants 528px of a 346px row, so <b>three working commands
/// — Open a cooked .set, Open the demo CookBook and Open Kitchen… — were off the bottom with no
/// scrollbar</b>. The editor's toolstrip needed 581px in a 418px pane and painted its ramp, swatch
/// and brush-size field over the colorize rail next door. And the Recipe pane's layer table
/// arranged its one star column at ZERO width, so every layer name on the screen was missing.</para>
///
/// <para>Each was invisible for the same reason: <b>a control that overruns is still arranged, still
/// reports a sensible <c>Bounds</c>, and is simply not drawn.</b> The individual layout tests that
/// existed measured at 1180 — 200px wider than the page has ever been given — so they passed while
/// the running app clipped.</para>
///
/// <para>The assertion is one line and it is the whole rule: measure the page at exactly the area
/// the smallest window gives it, and require its DESIRED size to fit. A <see cref="ScrollViewer"/>
/// never desires more than the constraint it was handed, so a page that scrolls what it cannot show
/// passes and a page that quietly clips does not — which is the distinction that matters, since a
/// page may scroll and a modal may not (<see cref="ModalFitTests"/> owns the other half).</para>
/// </remarks>
public class MinimumWindowFitTests
{
    /// <summary>The page area at the smallest window, derived rather than typed.</summary>
    private static Size PageArea => new(
        (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
        (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale);

    [AvaloniaFact]
    public void No_page_is_cut_off_at_the_smallest_window()
    {
        var area = PageArea;
        var over = new List<string>();

        foreach (var (name, view, dispose) in Pages())
        {
            var window = new Window { Content = view, Width = area.Width, Height = area.Height };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Measured at the real area, so anything inside a scroller is already bounded by it and
            // only a genuine overrun shows up here.
            view.Measure(area);
            var want = view.DesiredSize;

            if (want.Width > area.Width + 0.5 || want.Height > area.Height + 0.5)
            {
                over.Add($"{name} wants {want.Width:F0}x{want.Height:F0} "
                    + $"in {area.Width:F0}x{area.Height:F0}");
            }

            window.Close();
            dispose?.Invoke();
        }

        Assert.True(over.Count == 0,
            "These pages are cut off at the smallest window the app opens: " + string.Join("; ", over));
    }

    /// <summary>
    /// Landing does not actually need its scroller at the smallest window.
    /// </summary>
    /// <remarks>
    /// The sweep above cannot see this, and that is the price of measuring desired size: a
    /// <see cref="ScrollViewer"/> never wants more than its constraint, so a page that scrolls
    /// passes it whether it scrolls by a thousand pixels or by thirteen. Landing overran by exactly
    /// thirteen, which is not a clip — everything was reachable — but it put a scrollbar down the
    /// side of a screen with nothing hidden, which reads as broken. The scroller stays as the safety
    /// net for a window smaller than the app allows; it is asserted here that it is only that.
    /// </remarks>
    [AvaloniaFact]
    public void Landing_needs_no_scrollbar_at_the_smallest_window()
    {
        var area = PageArea;
        var view = new Views.LandingView
        {
            DataContext = new LandingViewModel(new FakeNav(), new FakeDialogs(),
                new FilePickerService(), new RecentsService(Directory.CreateTempSubdirectory().FullName),
                new CookBookSession(), _ => null!, _ => null!, (_, _, _) => null!, null),
        };
        var window = new Window { Content = view, Width = area.Width, Height = area.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"Landing wants {scroller.Extent.Height:F1}px in a {scroller.Viewport.Height:F1}px "
                + "row, so it shows a scrollbar with nothing hidden behind it");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The layer table's name column survives the narrowest pane it is ever given.
    /// </summary>
    /// <remarks>
    /// The page-level sweep above cannot see this one: a Grid star column given nothing is arranged
    /// at zero and the Grid's own desired size is perfectly happy, so the table "fits" while every
    /// name in it is gone. The column has to be asserted by name.
    /// </remarks>
    [AvaloniaFact]
    public void The_recipe_layer_table_still_has_room_for_a_layer_name()
    {
        var area = PageArea;
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

        var view = new Views.ExplorerView { DataContext = explorer };
        var window = new Window { Content = view, Width = area.Width, Height = area.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var header = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Text == "LAYER");
            Assert.True(header.Bounds.Width >= 90,
                $"the LAYER column is {header.Bounds.Width:F0}px wide - the names are gone");
        }
        finally { window.Close(); }
    }

    private static IEnumerable<(string Name, Control View, Action? Dispose)> Pages()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();

        var recents = new RecentsService(Directory.CreateTempSubdirectory().FullName);
        recents.Add(new Models.RecentItem("VaporPets", "cookbook - 2 recipes", @"D:\art\VaporPets.cbk", false));
        yield return ("landing", new Views.LandingView
        {
            DataContext = new LandingViewModel(nav, dialogs, new FilePickerService(), recents,
                session, _ => null!, _ => null!, (_, _, _) => null!, null),
        }, null);

        foreach (string which in new[] { "cookbook", "recipe", "ingredient" })
        {
            var book = ExplorerViewModelTests.TwoRecipeBook();
            var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
                new CookBookSession(), new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs),
                new StatusService());
            explorer.SelectNodeCommand.Execute(which switch
            {
                "cookbook" => explorer.Root,
                "recipe" => explorer.Root.Children[0],
                _ => explorer.Root.Children[0].Children[0],
            });
            yield return ($"explorer/{which}", new Views.ExplorerView { DataContext = explorer },
                explorer.Dispose);
        }

        var (b, r, ing) = VisualCapture.DynamicIngredient();
        yield return ("ingredient-editor", new Views.IngredientEditorView
        {
            DataContext = new IngredientEditorViewModel(ing, r, b, new ImageBridge(), nav,
                new CookBookSession(), dialogs, new FilePickerService()),
        }, b.Dispose);
    }
}
