using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The shelf's geometry, asserted from a laid-out visual tree.
/// </summary>
/// <remarks>
/// The band being ALWAYS the same height is not a detail of this design — it is the design. It is
/// what lets the Kitchen live on Landing without opening or closing a workspace reflowing the screen,
/// and it is the visual argument that the Kitchen is something the app carries rather than something
/// a CookBook has. A claim that load-bearing is worth measuring rather than reading off the markup.
/// </remarks>
public class KitchenShelfLayoutTests
{
    private const double WindowWidth = 1180;

    private static LandingViewModel Landing(IKitchenSession? kitchen)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var notify = new FakeNotYetWired();
        return new LandingViewModel(nav, dialogs, notify, new FilePickerService(),
            new RecentsService(StateStore.InMemory()), new CookBookSession(),
            _ => null!, _ => null!, (_, _, _) => null!, kitchen);
    }

    private static (Window window, Views.LandingView view) Render(LandingViewModel vm)
    {
        var view = new Views.LandingView { DataContext = vm };
        var window = new Window
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = view,
            Width = WindowWidth,
            Height = 720,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static Border Band(Visual view) => view.GetVisualDescendants()
        .OfType<Border>().First(b => b.Classes.Contains("kshelf"));

    private static List<Button> Cards(Visual view) => view.GetVisualDescendants()
        .OfType<Button>().Where(b => b.Classes.Contains("kcard")).ToList();

    /// <summary>Fills the shelf with cards without touching a disk: the layout does not care where a
    /// card came from, only that there is one.</summary>
    private static void Fill(LandingViewModel vm, string? kitchenName, int books)
    {
        var cards = Enumerable.Range(1, books)
            .Select(i => new KitchenCard($"C:/k/b{i}.cbk", $"Book {i}", "1 recipe · 8×8",
                KitchenItemKind.CookBook))
            .ToList();
        vm.KitchenShelf.Load(kitchenName, cards);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_band_is_one_height_in_all_three_states()
    {
        var vm = Landing(new KitchenSession());
        var (window, view) = Render(vm);
        try
        {
            var band = Band(view);
            var heights = new List<(string state, double h)>();

            Fill(vm, null, 0);                       // no Kitchen open
            Assert.True(vm.KitchenShelf.ShowNoKitchen);
            heights.Add(("no kitchen", band.Bounds.Height));

            Fill(vm, "Studio", 0);                   // open, empty
            Assert.True(vm.KitchenShelf.ShowEmptyKitchen);
            heights.Add(("empty", band.Bounds.Height));

            Fill(vm, "Studio", 12);                  // open, several pages
            Assert.True(vm.KitchenShelf.HasCards);
            heights.Add(("full", band.Bounds.Height));

            Assert.True(heights[0].h > 0);
            Assert.All(heights, x => Assert.Equal(heights[0].h, x.h));
        }
        finally { window.Close(); }
    }

    /// <summary>Everything ABOVE the band must stay where it is when a workspace opens. Measured on
    /// the band's own top edge, which is where any reflow would show up first.</summary>
    [AvaloniaFact]
    public void Opening_a_workspace_moves_nothing_above_the_band()
    {
        var vm = Landing(new KitchenSession());
        var (window, view) = Render(vm);
        try
        {
            var band = Band(view);

            Fill(vm, null, 0);
            var closed = band.Bounds;

            Fill(vm, "Studio", 12);

            Assert.Equal(closed, band.Bounds);
        }
        finally { window.Close(); }
    }

    /// <summary>The row is one shape whatever is on it: a short final page keeps its slots, so the
    /// cards stay one width instead of re-spacing as you page.</summary>
    [AvaloniaFact]
    public void The_cards_keep_their_width_on_a_short_final_page()
    {
        var vm = Landing(new KitchenSession());
        var (window, view) = Render(vm);
        try
        {
            // One more than a page holds, so the last page is short by construction whatever the
            // measured page size turns out to be at this width.
            Fill(vm, "Studio", vm.KitchenShelf.PageSize + 1);

            var first = Cards(view).Select(c => c.Bounds.Width).ToList();
            Assert.NotEmpty(first);

            vm.KitchenShelf.Page(1);
            Dispatcher.UIThread.RunJobs();

            var last = Cards(view).Select(c => c.Bounds.Width).ToList();
            Assert.Equal(first.Count, last.Count);
            Assert.Equal(first, last);
        }
        finally { window.Close(); }
    }

    /// <summary>The chevrons keep their boxes at both ends and lose only their ink, so nothing beside
    /// them shifts when you reach the first or last page.</summary>
    [AvaloniaFact]
    public void The_pager_chevrons_never_move()
    {
        var vm = Landing(new KitchenSession());
        var (window, view) = Render(vm);
        try
        {
            Fill(vm, "Studio", vm.KitchenShelf.PageSize * 3);
            var chevrons = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("kchev")).ToList();
            Assert.Equal(2, chevrons.Count);

            var atStart = chevrons.Select(c => c.Bounds).ToList();
            Assert.False(vm.KitchenShelf.CanPrev);          // the disabled end

            while (vm.KitchenShelf.CanNext) vm.KitchenShelf.Page(1);
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.KitchenShelf.CanNext);          // the other disabled end
            Assert.Equal(atStart, chevrons.Select(c => c.Bounds).ToList());
        }
        finally { window.Close(); }
    }

    /// <summary>The view measures its own row and tells the shelf how many cards fit — so the page
    /// size has to be a real number derived from the width, not the ViewModel's default.</summary>
    [AvaloniaFact]
    public void The_view_sets_a_page_size_from_the_rendered_width()
    {
        var vm = Landing(new KitchenSession());
        var (window, view) = Render(vm);
        try
        {
            Fill(vm, "Studio", 12);

            // 1180 wide, less the band's 38px padding either side, at ~176+9 per card.
            Assert.InRange(vm.KitchenShelf.PageSize, 4, 8);
            Assert.Equal(vm.KitchenShelf.PageSize, Cards(view).Count);
        }
        finally { window.Close(); }
    }
}
