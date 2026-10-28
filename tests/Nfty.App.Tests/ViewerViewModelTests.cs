using System;
using System.IO;
using System.Threading.Tasks;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class ViewerViewModelTests
{
    private sealed class Picker(string? path) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult(path);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static Image<Rgba32> Art()
    {
        var img = new Image<Rgba32>(3, 2);
        img[0, 0] = new Rgba32(10, 20, 30, 255);
        img[2, 1] = new Rgba32(200, 100, 50, 255);
        return img;
    }

    private static ViewerModel Model() => new(
        new Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1, 0, 0, 1, 0, 0, 1], [0, 0, 1, 0, 0, 1], [0, 1, 2], 1, 1),
        new Image<Rgba32>(1, 1), null);

    private static ViewerViewModel Vm(ViewerSubject s, string? path = null, IClipboardService? clip = null) =>
        new(s, new Picker(path), clip ?? new NoopClipboardService(), new DialogService());

    [Fact]
    public void A_subject_without_a_model_opens_in_2d_and_dims_the_layout_control()
    {
        using var art = Art();
        using var s = new SnapshotSubject("x", [], art, null);
        var vm = Vm(s);
        Assert.Equal(ViewerLayout.TwoD, vm.Layout);
        Assert.False(vm.CanShowThreeD);
        vm.Layout = ViewerLayout.ThreeD;
        Assert.Equal(ViewerLayout.TwoD, vm.Layout);
    }

    [Fact]
    public void A_subject_with_a_model_opens_side_by_side()
    {
        using var art = Art();
        using var s = new SnapshotSubject("x", [], art, Model());
        var vm = Vm(s);
        Assert.Equal(ViewerLayout.Both, vm.Layout);
        Assert.True(vm.CanShowThreeD);
        vm.Layout = ViewerLayout.ThreeD;
        Assert.Equal(ViewerLayout.ThreeD, vm.Layout);
    }

    [Fact]
    public async Task A_sealed_subject_disables_save_and_copy_and_says_why()
    {
        using var art = Art();
        using var s = new SnapshotSubject("x", [], art, null) { AllowExport = false };
        var clip = new NoopClipboardService();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        var vm = Vm(s, path, clip);

        Assert.False(vm.CanExport);
        Assert.False(vm.SaveImageCommand.CanExecute(1));
        Assert.False(vm.CopyCommand.CanExecute(null));
        Assert.Equal("View-only: this file was sealed.", vm.ExportNote);
        // Even invoked around CanExecute, nothing is written.
        await vm.SaveImageCommand.ExecuteAsync(1);
        await vm.CopyCommand.ExecuteAsync(null);
        Assert.False(File.Exists(path));
        Assert.Null(clip.LastImage);
    }

    [Fact]
    public async Task Save_image_at_2x_writes_twice_the_art_size_with_whole_pixels()
    {
        using var art = Art();
        using var s = new SnapshotSubject("x", [], art, null);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            await Vm(s, path).SaveImageCommand.ExecuteAsync(2);
            using var saved = Image.Load<Rgba32>(path);
            Assert.Equal(6, saved.Width);
            Assert.Equal(4, saved.Height);
            Assert.Equal(art[2, 1], saved[5, 3]);
            Assert.Equal(art[2, 1], saved[4, 2]);
            Assert.Equal(art[0, 0], saved[1, 1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Copy_puts_the_art_png_on_the_clipboard()
    {
        using var art = Art();
        using var s = new SnapshotSubject("x", [], art, null);
        var clip = new NoopClipboardService();
        await Vm(s, null, clip).CopyCommand.ExecuteAsync(null);
        Assert.NotNull(clip.LastImage);
        using var back = Image.Load<Rgba32>(clip.LastImage!);
        Assert.Equal(art[2, 1], back[2, 1]);
    }

    [Fact]
    public void Fit_resets_zoom_and_pan()
    {
        var pane = new TwoDPaneViewModel { Zoom = 8, PanX = 30, PanY = -12 };
        pane.FitCommand.Execute(null);
        Assert.Equal(1, pane.Zoom);
        Assert.Equal(0, pane.PanX);
        Assert.Equal(0, pane.PanY);
    }

    [Fact]
    public void Zoom_is_clamped_between_1_and_64()
    {
        var pane = new TwoDPaneViewModel { Zoom = 0.2 };
        Assert.Equal(1, pane.Zoom);
        pane.Zoom = 500;
        Assert.Equal(64, pane.Zoom);
        pane.Zoom = double.NaN;
        Assert.Equal(1, pane.Zoom);
    }

    [Fact]
    public void One_to_one_inverts_the_fitted_scale()
    {
        var pane = new TwoDPaneViewModel();
        pane.SetSizes(640, 480, 320, 240); // fitted scale 0.5 device px per art px
        pane.Zoom = 5;
        pane.OneToOneCommand.Execute(null);
        Assert.Equal(2, pane.Zoom, 9);
        // Art smaller than the surface would need zoom below the floor; it clamps to fitted.
        pane.SetSizes(32, 16, 640, 480);
        pane.OneToOneCommand.Execute(null);
        Assert.Equal(1, pane.Zoom);
    }

    [Fact]
    public void The_custom_backdrop_keeps_its_color_beside_the_choice()
    {
        var pane = new TwoDPaneViewModel { Backdrop = ViewerBackdrop.Custom, BackdropColor = "#336699" };
        Assert.Equal(ViewerBackdrop.Custom, pane.Backdrop);
        Assert.Equal("#336699", pane.BackdropColor);
        pane.Backdrop = ViewerBackdrop.Dark;
        Assert.Equal("#336699", pane.BackdropColor);
    }

    [Fact]
    public void Snapshot_subject_owns_a_copy_not_the_callers_image()
    {
        var art = Art();
        using var s = new SnapshotSubject("x", [], art, Model());
        art.Dispose();
        Assert.Equal(new Rgba32(200, 100, 50, 255), s.Art![2, 1]);
        Assert.NotNull(s.Model!.Texture);
    }
}
