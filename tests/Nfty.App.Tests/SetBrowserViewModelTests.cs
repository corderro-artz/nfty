using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

public class SetBrowserViewModelTests
{
    // Reuse a helper that cooks a tiny set to a temp folder and reads it (mirror SetReaderTests' book).
    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    [AvaloniaFact]
    public void Exposes_items_with_thumbnails_and_header()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        Assert.Equal("VaporCats", vm.Name);
        Assert.Equal(2, vm.Count);
        Assert.Equal(2, vm.Items.Count);
        Assert.All(vm.Items, r => Assert.NotNull(r.DecodeNow()));
        vm.SelectedItem = vm.Items[0];
        Assert.False(string.IsNullOrEmpty(vm.SelectedDna));
        vm.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    [AvaloniaFact]
    public void Tolerates_missing_item_image()
    {
        var loaded = CookedSet(out var dir);
        // Delete one image file to simulate corruption/missing file.
        File.Delete(loaded.Items[0].ImagePath);

        // Must not throw despite missing image.
        var vm = new SetBrowserViewModel(loaded);

        Assert.Equal(2, vm.Items.Count);
        // A missing file must still yield a placeholder bitmap rather than throwing — the decode is
        // off the UI thread now, so an exception there would be a crash with no stack to attach to.
        Assert.All(vm.Items, r => Assert.NotNull(r.DecodeNow()));

        vm.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Opening a Set must not decode every thumbnail. It used to: the constructor decoded all of
    /// them on the UI thread — 627 ms for 900 assets, about seven seconds of frozen window for a
    /// 10,000-asset Set — and the ListBox's virtualization could do nothing about it, because
    /// virtualizing limits what is RENDERED, not work already finished before the first frame.
    /// </summary>
    [AvaloniaFact]
    public void Opening_a_set_decodes_no_thumbnails_until_a_row_is_realized()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);

        // Constructed and populated, with nothing decoded.
        Assert.Equal(2, vm.Items.Count);

        Assert.All(vm.Items, r => Assert.False(r.IsThumbnailDecoded));

        // Reading the property is what the virtualizing panel does when it realizes a row, and it is
        // the only thing that starts a decode. It returns null until that decode lands, which is
        // what the tile's placeholder covers.
        Assert.Null(vm.Items[0].Thumbnail);
        PumpUntil(() => vm.Items[0].IsThumbnailDecoded);     // let the decode land
        var first = vm.Items[0].Thumbnail;
        Assert.NotNull(first);
        Assert.False(vm.Items[1].IsThumbnailDecoded);        // and only the row that was read

        // Cached: a second read is the same instance, not a second decode.
        Assert.Same(first, vm.Items[0].Thumbnail);

        vm.Dispose();
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Runs the dispatcher until <paramref name="done"/> holds, or gives up.
    /// </summary>
    /// <param name="done">The condition to wait for.</param>
    /// <param name="timeoutMs">How long to wait before giving up.</param>
    /// <remarks>
    /// Thumbnails decode on the thread pool and publish through <c>Dispatcher.UIThread.Post</c>, so
    /// a single <c>RunJobs</c> can drain an empty queue before the decode has even finished. Pumping
    /// until the state actually changes is the honest way to test that path; the timeout is there so
    /// a broken decode fails as a failed assertion rather than a hung run.
    /// </remarks>
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
}
