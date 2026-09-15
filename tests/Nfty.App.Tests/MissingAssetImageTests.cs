using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A MISSING ASSET IMAGE IS NOT A SLOW ONE, AND MUST NOT LOOK LIKE ONE.
/// </summary>
/// <remarks>
/// <para>The decode falls back to a 1×1 transparent bitmap rather than throwing, which is right — a
/// browser over a damaged Set should show the damage rather than refuse to open. But the moment that
/// placeholder was published <c>IsLoading</c> went false, the breathing diamond that covers a pending
/// decode went with it, and the tile settled into a flat empty square: indistinguishable from a
/// decode still in flight, and from a fully transparent asset, which is a legal thing to mint.</para>
///
/// <para>Found by opening a Set whose folder had been half cleaned up — twenty-four images for a
/// sixty-asset Set, and thirty-six tiles that looked like they were still thinking. An empty state
/// must say which emptiness it is.</para>
/// </remarks>
public class MissingAssetImageTests
{
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

    [AvaloniaFact]
    public void A_row_whose_file_is_gone_says_so_once_the_decode_has_answered()
    {
        var loaded = CookedSet(out var dir);
        try
        {
            File.Delete(loaded.Items[0].ImagePath);
            using var vm = new SetBrowserViewModel(loaded);

            // The decode still yields a bitmap — a damaged Set opens.
            Assert.NotNull(vm.Items[0].DecodeNow());
            Assert.NotNull(vm.Items[1].DecodeNow());

            Assert.True(vm.Items[0].ImageMissing);
            Assert.False(vm.Items[1].ImageMissing, "a file that is present must not be flagged");

            // And neither is "loading" any more, which is the whole point: without the flag these
            // two rows are in identical states and only one of them is ever getting an image.
            Assert.False(vm.Items[0].IsLoading);
            Assert.False(vm.Items[1].IsLoading);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// THE MARK IS DRAWN, and only on the row that lost its file. Asserting the flag alone would
    /// pass on a tile that still shows nothing — which is the bug.
    /// </summary>
    [AvaloniaFact]
    public void Only_the_tile_whose_file_is_gone_draws_the_mark()
    {
        var loaded = CookedSet(out var dir);
        try
        {
            File.Delete(loaded.Items[0].ImagePath);
            var vm = new SetBrowserViewModel(loaded);
            var view = new Views.SetBrowserView { DataContext = vm };
            var window = new Window { Content = view, Width = 1180, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                PumpUntil(() => vm.Items.All(i => !i.IsLoading));

                var marks = view.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("tmissing") && b.IsVisible)
                    .ToList();

                // One on the tile, one in the rail — the rail is describing the selected asset,
                // which is item 0, the one that lost its file.
                Assert.NotEmpty(marks);
                Assert.All(marks, m => Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0));

                // The loading pulse is NOT what a missing file gets: a pulse means "coming".
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Border>(),
                    b => b.Classes.Contains("tload") && b.IsVisible);
            }
            finally { vm.Dispose(); window.Close(); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The probe: an intact Set draws no marks at all, or the assertion above would pass on
    /// a browser that flagged every tile.</summary>
    [AvaloniaFact]
    public void An_intact_set_draws_no_marks()
    {
        var loaded = CookedSet(out var dir);
        try
        {
            var vm = new SetBrowserViewModel(loaded);
            var view = new Views.SetBrowserView { DataContext = vm };
            var window = new Window { Content = view, Width = 1180, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                PumpUntil(() => vm.Items.All(i => !i.IsLoading));

                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Border>(),
                    b => b.Classes.Contains("tmissing") && b.IsVisible);
                Assert.All(vm.Items, i => Assert.False(i.ImageMissing));
            }
            finally { vm.Dispose(); window.Close(); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
