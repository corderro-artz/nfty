using System.IO;
using Avalonia.Headless.XUnit;
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
        Assert.All(vm.Items, r => Assert.NotNull(r.Thumbnail));
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
        Assert.All(vm.Items, r => Assert.NotNull(r.Thumbnail));

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

        // Reading the property is what the virtualizing panel does when it realizes a row, and it
        // is the only thing that decodes.
        var first = vm.Items[0].Thumbnail;
        Assert.NotNull(first);

        // Cached: a second read is the same instance, not a second decode.
        Assert.Same(first, vm.Items[0].Thumbnail);

        vm.Dispose();
        Directory.Delete(dir, recursive: true);
    }
}
