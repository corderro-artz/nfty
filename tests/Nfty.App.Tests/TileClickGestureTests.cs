using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Set browser's tile click, exercised as a gesture.
///
/// <para>The tiles are the app's one deliberate use of a bubbled <c>Button.ClickEvent</c> handler
/// rather than a per-item command — the grid is an ItemsControl of rows of tiles, so a binding would
/// have to hop two DataContexts up through a template. That is a reasonable trade, but it puts the
/// wiring in code-behind where <c>WiringCoverageTests</c> cannot follow it and
/// <c>DeadCodeAuditTests</c> explicitly exempts it. Nothing raised a real click on a tile until this
/// file, so the handler's only evidence was a person driving the app.</para>
///
/// <para>Written immediately after the same exposure turned out to be a live bug on the Recipe
/// pane's chance field, where a handler tested <c>e.Source</c> by type and the source it actually
/// received was the control inside the template. Assume nothing about a routed event's source that a
/// gesture has not demonstrated.</para>
/// </summary>
public class TileClickGestureTests
{
    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    [AvaloniaFact]
    public void Clicking_a_tile_opens_the_inspector_on_that_asset()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // A tile is the Button whose DataContext is a row — the same shape the handler matches
            // on, found here from the tree rather than assumed from the markup.
            var tiles = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.DataContext is SetItemRow)
                .ToList();
            Assert.True(tiles.Count >= 2, $"expected two tiles to click between, found {tiles.Count}");

            // The SECOND tile. The browser opens with the first item already selected, so clicking
            // that one would pass whether the handler ran or not.
            var tile = tiles[1];
            var row = (SetItemRow)tile.DataContext!;
            Assert.NotSame(row, vm.SelectedItem);

            tile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent) { Source = tile });
            Dispatcher.UIThread.RunJobs();

            Assert.Same(row, vm.SelectedItem);
        }
        finally
        {
            window.Close();
            loaded.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Every other Button in the view bubbles to the same handler, and none of them may be mistaken
    /// for a tile. The rail's own buttons carry the ViewModel as their DataContext, not a row, which
    /// is the whole of what tells them apart — so it is asserted rather than left as a comment.
    /// </summary>
    [AvaloniaFact]
    public void A_button_that_is_not_a_tile_does_not_select_anything()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var other = view.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.DataContext is not SetItemRow);
            Assert.NotNull(other);

            var before = vm.SelectedItem;
            other!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent) { Source = other });
            Dispatcher.UIThread.RunJobs();

            Assert.Same(before, vm.SelectedItem);
        }
        finally
        {
            window.Close();
            loaded.Dispose();
            Directory.Delete(dir, recursive: true);
        }
    }
}
