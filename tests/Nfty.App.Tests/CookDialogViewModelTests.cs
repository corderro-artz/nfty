using System.IO;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class CookDialogViewModelTests
{
    private sealed class FolderPicker : IFilePickerService
    {
        private readonly string? _folder;
        public FolderPicker(string? folder) => _folder = folder;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult(_folder);
    }
    private sealed class RecordingRevealer : IFolderRevealer
    { public string? Revealed; public void Reveal(string p) => Revealed = p; }

    // A tiny valid 2-recipe book with enough unique space (reuse ExplorerViewModelTests.TwoRecipeBook()).
    private static LoadedCookBook Book() => ExplorerViewModelTests.TwoRecipeBook();

    [AvaloniaFact]
    public async Task Cook_writes_a_set_to_the_chosen_folder()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var vm = new CookDialogViewModel(Book(), new FolderPicker(dir), new RecordingRevealer(), new FakeDialogs());
        vm.Count = 2; vm.Seed = "seed1"; vm.Pack = false;
        await vm.CookCommand.ExecuteAsync(null);
        Assert.True(vm.IsDone);
        Assert.True(File.Exists(Path.Combine(dir, "set.json")));   // Core wrote the set
        Assert.Contains("2", vm.ResultText);
    }

    [AvaloniaFact]
    public async Task Pack_produces_a_sibling_set_archive()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var vm = new CookDialogViewModel(Book(), new FolderPicker(dir), new RecordingRevealer(), new FakeDialogs());
        vm.Count = 2; vm.Seed = "seed1"; vm.Pack = true;
        await vm.CookCommand.ExecuteAsync(null);
        Assert.True(File.Exists(dir + ".set"));
    }

    [AvaloniaFact]
    public async Task Cancelled_pick_does_nothing()
    {
        var vm = new CookDialogViewModel(Book(), new FolderPicker(null), new RecordingRevealer(), new FakeDialogs());
        vm.Count = 2; vm.Seed = "s";
        await vm.CookCommand.ExecuteAsync(null);
        Assert.False(vm.IsDone);
        Assert.False(vm.IsRunning);
    }

    [AvaloniaFact]
    public async Task Too_large_count_surfaces_an_error_dialog()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var dialogs = new FakeDialogs();
        var vm = new CookDialogViewModel(Book(), new FolderPicker(dir), new RecordingRevealer(), dialogs);
        vm.Count = 100000; vm.Seed = "s"; vm.Pack = false;   // exceeds the fixture's unique space
        await vm.CookCommand.ExecuteAsync(null);
        Assert.False(vm.IsDone);
        Assert.False(vm.IsRunning);
        Assert.IsType<ErrorDialogViewModel>(dialogs.Active);   // error surfaced, no crash
    }
}
