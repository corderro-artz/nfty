using System.IO;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class RiseVoxelSizeTests
{
    [AvaloniaFact]
    public void Voxel_size_bounds_are_one_rule_everywhere()
    {
        string dir = Directory.CreateTempSubdirectory("nfty-voxel-").FullName;
        string path = Path.Combine(dir, "gem.png");
        using (var img = new Image<Rgba32>(8, 8, new Rgba32(200, 30, 30, 255))) img.Save(path);

        using var vm = new RiseViewModel(PictureRiseSource.Open(path, new ImageBridge()), new ImageBridge(),
            new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());

        vm.VoxelSize = 0.0005;
        Assert.Equal(ModelVoxelSize.Min, vm.VoxelSize);

        vm.VoxelSize = 0.1234;
        Assert.Equal(0.123, vm.VoxelSize);

        vm.VoxelSize = 5000;
        Assert.Equal(ModelVoxelSize.Max, vm.VoxelSize);
    }
}
