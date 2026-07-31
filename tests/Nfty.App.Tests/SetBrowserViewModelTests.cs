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
}
