using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>A voxel-ready snap on a picture lives only in the session, so it is unsaved work: it lights
/// Save as .igt even with no depth painted, and leaving asks before dropping it.</summary>
public class RisePictureSnapSaveTests
{
    private sealed class Dialogs(LeaveChoice choice) : IDialogService
    {
        public LeaveDialogViewModel? Leave { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }

        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is VoxelReadyDialogViewModel) return Task.FromResult((TResult?)(object)true);
            if (dialog is LeaveDialogViewModel leave) { Leave = leave; return Task.FromResult((TResult?)(object)choice); }
            return Task.FromResult<TResult?>(default);
        }

        public void Close(object? result) { }
    }

    private static string GhostPng()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-snap-").FullName, "ghost.png");
        using var img = RiseFixtures.Picture(RiseFixtures.Ghost);
        img[4, 3] = new Rgba32(200, 40, 40, 127);
        img.Save(path);
        return path;
    }

    private static (RiseViewModel Vm, PictureRiseSource Src, FakeNav Nav, Dialogs Dialogs, string Igt) Open(LeaveChoice choice)
    {
        string igt = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-igt-").FullName, "ghost.igt");
        var src = PictureRiseSource.Open(GhostPng(), new ImageBridge(), () => Task.FromResult<string?>(igt));
        var nav = new FakeNav();
        var dialogs = new Dialogs(choice);
        var vm = new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), new StatusService());
        return (vm, src, nav, dialogs, igt);
    }

    [AvaloniaFact]
    public async Task Save_as_is_enabled_after_a_snap_with_no_depth_painted_and_writes_the_snapped_pixels()
    {
        var (vm, src, _, _, igt) = Open(LeaveChoice.Stay);
        using (src) using (vm)
        {
            Assert.False(vm.CanSave);
            await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

            Assert.False(vm.HasUnsavedDepth);
            Assert.True(vm.HasUnsavedWork);
            Assert.True(vm.CanSave);
            Assert.True(vm.SaveCommand.CanExecute(null));
            Assert.Contains("voxel-ready", vm.FooterText, StringComparison.Ordinal);

            await vm.SaveCommand.ExecuteAsync(null);

            using var written = IngredientArchive.Read(igt);
            var art = Assert.Single(written.VariantImages).Value;
            Assert.Equal(255, art[3, 3].A);
            Assert.Equal(0, art[4, 3].A);
            Assert.False(vm.HasUnsavedWork);
            Assert.False(vm.CanSave);
        }
    }

    [AvaloniaFact]
    public async Task Leaving_with_an_unsaved_snap_asks_and_Stay_keeps_the_page()
    {
        var (vm, src, nav, dialogs, _) = Open(LeaveChoice.Stay);
        using (src) using (vm)
        {
            await vm.MakeVoxelReadyCommand.ExecuteAsync(null);
            await vm.BackCommand.ExecuteAsync(null);

            Assert.NotNull(dialogs.Leave);
            Assert.Contains("voxel-ready", dialogs.Leave!.Message, StringComparison.Ordinal);
            Assert.True(dialogs.Leave.CanSave);
            Assert.Equal(0, nav.BackCount);
        }
    }

    [AvaloniaFact]
    public async Task Discard_leaves_and_Save_and_leave_writes_first()
    {
        var (vm, src, nav, _, igt) = Open(LeaveChoice.Discard);
        using (src) using (vm)
        {
            await vm.MakeVoxelReadyCommand.ExecuteAsync(null);
            await vm.BackCommand.ExecuteAsync(null);
            Assert.Equal(1, nav.BackCount);
            Assert.False(File.Exists(igt));
        }

        var (vm2, src2, nav2, _, igt2) = Open(LeaveChoice.SaveAndLeave);
        using (src2) using (vm2)
        {
            await vm2.MakeVoxelReadyCommand.ExecuteAsync(null);
            await vm2.BackCommand.ExecuteAsync(null);
            Assert.Equal(1, nav2.BackCount);
            Assert.True(File.Exists(igt2));
        }
    }

    [AvaloniaFact]
    public async Task An_undone_or_saved_snap_leaves_without_asking()
    {
        var (vm, src, nav, dialogs, _) = Open(LeaveChoice.Stay);
        using (src) using (vm)
        {
            await vm.MakeVoxelReadyCommand.ExecuteAsync(null);
            await vm.UndoVoxelReadyCommand.ExecuteAsync(null);
            Assert.False(vm.CanSave);
            await vm.BackCommand.ExecuteAsync(null);
            Assert.Null(dialogs.Leave);
            Assert.Equal(1, nav.BackCount);
        }
    }
}
