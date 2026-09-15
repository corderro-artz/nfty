using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// THE RAIL SHOWS THE ASSET IT IS DESCRIBING.
/// </summary>
/// <remarks>
/// <para>A grid of a hundred tiles is SCANNED rather than read, and the rail already followed the
/// pointer — but it answered with an id, a hash and a table, so working out what you were actually
/// looking at meant going back to the tile you had just left. The preview binds to
/// <c>Shown</c>, which is already "the one under the pointer, or the one that is kept", so hover,
/// right-click-to-keep and click-through-to-the-inspector all move it for free rather than each
/// needing their own wiring.</para>
///
/// <para>It binds straight through to <c>Shown.Thumbnail</c> rather than mirroring it onto a
/// property here. A row's image arrives off the thread pool and raises its own change; a snapshot
/// taken when <c>Shown</c> moved would show the placeholder forever on any tile whose decode had not
/// landed yet.</para>
/// </remarks>
public class SetRailPreviewTests
{
    /// <summary>A tiny cooked Set on disk, the way every other Set-browser test builds one.</summary>
    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    private static void PumpUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static Image Preview(Visual view) => view.GetVisualDescendants()
        .OfType<Border>().First(b => b.Classes.Contains("railshot"))
        .GetVisualDescendants().OfType<Image>().First();

    [AvaloniaFact]
    public void The_preview_follows_the_hover_and_falls_back_to_the_selection()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        try
        {
            // Opens on the first asset, which is what the rail describes with nothing hovered.
            Assert.Same(vm.Items[0], vm.Shown);

            vm.HoveredItem = vm.Items[1];
            Assert.Same(vm.Items[1], vm.Shown);
            Assert.True(vm.IsShowingHover);

            // Moving off the tiles hands the rail back to the kept asset rather than blanking it.
            vm.HoveredItem = null;
            Assert.Same(vm.Items[0], vm.Shown);
            Assert.False(vm.IsShowingHover);

            // And right-click-to-keep moves both the selection and what the rail shows.
            vm.SelectedItem = vm.Items[1];
            Assert.Same(vm.Items[1], vm.Shown);
        }
        finally { vm.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// THE IMAGE IS REALLY DRAWN, and it is the shown asset's own. Asserting on
    /// <c>Shown</c> alone would pass on a rail that never got a preview at all.
    /// </summary>
    [AvaloniaFact]
    public void The_rail_draws_the_shown_assets_own_image()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var preview = Preview(view);

            PumpUntil(() => preview.Source is not null);
            Assert.NotNull(preview.Source);
            Assert.Same(vm.Items[0].Thumbnail, preview.Source);

            vm.HoveredItem = vm.Items[1];
            Dispatcher.UIThread.RunJobs();
            PumpUntil(() => ReferenceEquals(preview.Source, vm.Items[1].Thumbnail));

            Assert.Same(vm.Items[1].Thumbnail, preview.Source);
        }
        finally { vm.Dispose(); window.Close(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// The identity block is ONE HEIGHT for every asset. The rail changes as the pointer crosses the
    /// grid, which is the worst possible place for a reflow — the "hovering / selected" line beside
    /// the preview is reserved for the same reason, and a preview that sized itself to its art would
    /// have undone both.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_is_the_same_size_whatever_it_is_showing()
    {
        var loaded = CookedSet(out var dir);
        var vm = new SetBrowserViewModel(loaded);
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var shot = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("railshot"));
            var first = shot.Bounds;
            Assert.True(first.Width > 0 && first.Height > 0);

            foreach (var item in vm.Items)
            {
                vm.HoveredItem = item;
                Dispatcher.UIThread.RunJobs();
                PumpUntil(() => item.Thumbnail is not null);
                Assert.Equal(first, shot.Bounds);
            }
        }
        finally { vm.Dispose(); window.Close(); Directory.Delete(dir, recursive: true); }
    }
}
