using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LandingRiseTests
{
    private sealed class StubPicker(string? path) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult(path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Landing(FakeNav nav, FakeDialogs dialogs, string? picked, StatusService status, bool withRise = true) =>
        new(nav, dialogs, new StubPicker(picked), new RecentsService(Directory.CreateTempSubdirectory().FullName),
            new CookBookSession(), _ => null!, _ => null!, (_, _, _) => null!, null, null,
            withRise
                ? src => new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), status)
                : null,
            status, new ImageBridge());

    private static string TempSheet()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-landing-rise-").FullName, "hero.png");
        using var img = new Image<Rgba32>(32, 16);
        for (int c = 0; c < 2; c++)
            for (int y = 4; y < 12; y++)
                for (int x = 4; x < 12; x++)
                    img[c * 16 + x, y] = new Rgba32(200, 40, 40, 255);
        img.Save(path);
        return path;
    }

    [AvaloniaFact]
    public async Task Landing_rise_a_picture_opens_the_rise_page()
    {
        var nav = new FakeNav();
        var status = new StatusService();
        var vm = Landing(nav, new FakeDialogs(), TempSheet(), status);

        Assert.True(vm.RisePictureCommand.CanExecute(null));
        await vm.RisePictureCommand.ExecuteAsync(null);
        await vm.PendingOpen!;

        var page = Assert.IsType<RiseViewModel>(nav.Current);
        using var _ = page;
        Assert.Equal(RiseSourceKind.Sheet, page.Source.Kind);
        Assert.Equal(2, page.Items.Count);
        // The page's opening line and what happened to the file, as one sentence on the status board.
        Assert.StartsWith(page.Source.OpeningLine, status.Last);
        Assert.EndsWith(((PictureRiseSource)page.Source).GridWriteNote!, status.Last);
    }

    [AvaloniaFact]
    public async Task A_canceled_picker_opens_nothing()
    {
        var nav = new FakeNav();
        var vm = Landing(nav, new FakeDialogs(), null, new StatusService());

        await vm.RisePictureCommand.ExecuteAsync(null);

        Assert.Null(vm.PendingOpen);
        Assert.Null(nav.Current);
    }

    [AvaloniaFact]
    public async Task A_file_that_is_not_a_picture_is_reported_and_opens_nothing()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-landing-rise-").FullName, "bad.png");
        File.WriteAllText(path, "nope");
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var vm = Landing(nav, dialogs, path, new StatusService());

        await vm.RisePictureCommand.ExecuteAsync(null);
        await vm.PendingOpen!;

        Assert.Null(nav.Current);
        var error = Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.Contains("bad.png", error.Message);
    }

    [AvaloniaFact]
    public async Task Without_a_rise_page_the_command_is_disabled_and_does_nothing()
    {
        var nav = new FakeNav();
        var vm = Landing(nav, new FakeDialogs(), TempSheet(), new StatusService(), withRise: false);

        Assert.False(vm.RisePictureCommand.CanExecute(null));
        await vm.RisePictureCommand.ExecuteAsync(null);     // a disabled button is a label

        Assert.Null(vm.PendingOpen);
        Assert.Null(nav.Current);
    }
}
