using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Set browser's detail rail follows the pointer, and the right button keeps an asset without
/// opening it.
/// </summary>
/// <remarks>
/// <para>A grid of hundreds of tiles is SCANNED rather than read, and the only way to find out what
/// one was meant clicking it — which opens the inspector directly over the panel that answers the
/// question. So the rail appeared never to update until the modal had been opened and closed again:
/// it was updating, under the modal, where nobody could see it.</para>
///
/// <para>And there was no way to keep an asset in the rail. Left-click means "show me this bigger",
/// which is the right default on a grid of pictures; comparing one asset's rarity against another
/// had no gesture at all.</para>
///
/// <para>Driven from the WINDOW, because all of this is pointer routing: a test that called
/// <c>Hover</c> would pass while the app did nothing.</para>
/// </remarks>
public class SetBrowserHoverTests
{
    private sealed class NoDialogs : IDialogService
    {
        public int Shown { get; private set; }
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed { add { } remove { } }
        public System.Threading.Tasks.Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Shown++;
            Active = d;
            return System.Threading.Tasks.Task.FromResult<TResult?>(default);
        }
        public void Close(object? result) { }
    }

    private static (Window Window, SetBrowserViewModel Vm, Views.SetBrowserView View, string Dir) Render()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using (var cooked = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(6, "hover")))
            SetWriter.Write(cooked, dir, pack: false);

        var vm = new SetBrowserViewModel(SetReader.Read(dir), new FilePickerService(), new NoDialogs(),
            new StatusService(), new NoopFolderRevealer());
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view, dir);
    }

    /// <summary>The realized tile buttons, in the order they were laid out.</summary>
    private static Button[] Tiles(Visual view) => view.GetVisualDescendants().OfType<Button>()
        .Where(b => b.Classes.Contains("tcell") && b.Bounds.Width > 0)
        .ToArray();

    private static Point Center(Window window, Visual v) =>
        v.TranslatePoint(new Point(v.Bounds.Width / 2, v.Bounds.Height / 2), window)!.Value;

    private static void Cleanup(Window window, SetBrowserViewModel vm, string dir)
    {
        window.Close();
        vm.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    [AvaloniaFact]
    public void Hovering_a_tile_points_the_rail_at_it()
    {
        var (window, vm, view, dir) = Render();
        try
        {
            var tiles = Tiles(view);
            Assert.True(tiles.Length >= 2, $"only {tiles.Length} tiles were realized");

            var first = vm.ShownNumber;
            var other = (SetItemRow)tiles.Last().DataContext!;

            window.MouseMove(Center(window, tiles.Last()));
            Dispatcher.UIThread.RunJobs();

            Assert.Same(other, vm.Shown);
            Assert.Equal($"#{other.Number:D4}", vm.ShownNumber);
            Assert.NotEqual(first, vm.ShownNumber);
            Assert.Equal(other.Item.Dna, vm.ShownDna);
            Assert.True(vm.IsShowingHover);
            Assert.True(other.IsHovered);

            // Hovering is not selecting: what Save and the inspector act on has not moved.
            Assert.NotSame(other, vm.SelectedItem);
        }
        finally { Cleanup(window, vm, dir); }
    }

    /// <summary>Moving off the grid puts the rail back on the selection rather than leaving it
    /// describing an asset the pointer left behind.</summary>
    [AvaloniaFact]
    public void Moving_off_the_tiles_returns_the_rail_to_the_selection()
    {
        var (window, vm, view, dir) = Render();
        try
        {
            var tiles = Tiles(view);
            window.MouseMove(Center(window, tiles.Last()));
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsShowingHover);

            window.MouseMove(new Point(5, 5));         // the page, not a tile
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.IsShowingHover);
            Assert.Same(vm.SelectedItem, vm.Shown);
            Assert.DoesNotContain(vm.Items, i => i.IsHovered);
        }
        finally { Cleanup(window, vm, dir); }
    }

    /// <summary>
    /// The right button keeps an asset and opens nothing.
    /// </summary>
    /// <remarks>
    /// The press is taken on the TUNNEL and handled, because the tile is a Button: letting it reach
    /// one would arm a click the release then delivers to the click handler, which opens the very
    /// modal this gesture exists to avoid. Asserting "no dialog was shown" is what makes that
    /// assertion about the feature rather than about the property.
    /// </remarks>
    [AvaloniaFact]
    public void Right_clicking_a_tile_selects_it_without_opening_the_inspector()
    {
        var (window, vm, view, dir) = Render();
        var dialogs = (NoDialogs)typeof(SetBrowserViewModel)
            .GetField("_dialogs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm)!;
        try
        {
            var tiles = Tiles(view);
            var target = (SetItemRow)tiles.Last().DataContext!;
            Assert.NotSame(target, vm.SelectedItem);

            var at = Center(window, tiles.Last());
            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(target, vm.SelectedItem);
            Assert.True(target.IsSelected);
            Assert.Equal(0, dialogs.Shown);
        }
        finally { Cleanup(window, vm, dir); }
    }

    /// <summary>Left-click still opens the inspector — the right button is an addition, not a
    /// replacement.</summary>
    [AvaloniaFact]
    public void Left_clicking_a_tile_still_opens_the_inspector()
    {
        var (window, vm, view, dir) = Render();
        var dialogs = (NoDialogs)typeof(SetBrowserViewModel)
            .GetField("_dialogs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm)!;
        try
        {
            var tiles = Tiles(view);
            var target = (SetItemRow)tiles.Last().DataContext!;
            var at = Center(window, tiles.Last());

            window.MouseMove(at);
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(target, vm.SelectedItem);
            Assert.Equal(1, dialogs.Shown);
        }
        finally { Cleanup(window, vm, dir); }
    }

    /// <summary>
    /// Exactly one tile is hovered at a time, and exactly one is selected.
    /// </summary>
    /// <remarks>
    /// Both flags are pushed row by row rather than recomputed by every row, so the invariant is
    /// worth pinning rather than assuming — the same reason the selection's own "exactly one" test
    /// exists. Hover moves on every pointer move, so an unpaired flag would accumulate across a
    /// grid of hundreds.
    /// </remarks>
    [AvaloniaFact]
    public void Exactly_one_tile_is_hovered_and_one_selected()
    {
        var (window, vm, view, dir) = Render();
        try
        {
            foreach (var tile in Tiles(view))
            {
                window.MouseMove(Center(window, tile));
                Dispatcher.UIThread.RunJobs();
                Assert.Single(vm.Items, i => i.IsHovered);
                Assert.Single(vm.Items, i => i.IsSelected);
            }
        }
        finally { Cleanup(window, vm, dir); }
    }

    /// <summary>
    /// Save acts on what the rail SHOWS, which is the same asset either way.
    /// </summary>
    /// <remarks>
    /// Reaching the Save button means leaving the tiles, so the hover is already gone by the time it
    /// can be pressed — but a rail describing one asset over a button that saves another is exactly
    /// the kind of disagreement this app treats as a defect, so they read the same property.
    /// </remarks>
    [AvaloniaFact]
    public void The_rail_and_save_never_describe_different_assets()
    {
        var (window, vm, view, dir) = Render();
        try
        {
            var tiles = Tiles(view);
            window.MouseMove(Center(window, tiles.Last()));
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsShowingHover);

            var save = view.GetVisualDescendants().OfType<Button>()
                .First(b => b.Classes.Contains("savebtn"));
            window.MouseMove(Center(window, save));
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.IsShowingHover);
            Assert.Same(vm.SelectedItem, vm.Shown);
        }
        finally { Cleanup(window, vm, dir); }
    }
}
