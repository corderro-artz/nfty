using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class RiseDimensionsAuditTests
{
    [AvaloniaTheory]
    [InlineData(1, 0.001, "model 0.001 × 0.001 × 0.001")]
    [InlineData(8, 0.123, "model 0.984 × 0.984 × 0.123")]
    public void The_dimension_readout_keeps_supported_voxel_precision(int side, double voxelSize, string expected)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(directory.FullName, "art.png");
            using (var art = new Image<Rgba32>(side, side, new Rgba32(1, 2, 3, 255))) art.SaveAsPng(path);
            using var vm = new RiseViewModel(PictureRiseSource.Open(path, new ImageBridge()), new ImageBridge(),
                new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());
            RisePages.Settle(vm);
            vm.VoxelSize = voxelSize;
            Assert.Equal(expected, vm.ModelDimensionsText);
        }
        finally { directory.Delete(recursive: true); }
    }
}
