using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The desktop head's viewer window: not owned by the shell, remembers its size per layout, opens
/// somewhere that still exists, and leaves the application running until the LAST window closes.
/// </summary>
public class ViewerWindowTests
{
    private sealed class Picker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static DesktopViewerHost Host(IViewerWindowState state, Func<Window?> main) =>
        new(new Picker(), new NoopClipboardService(), state, new ImageBridge(), main);

    private static IViewerWindowState MemoryState() =>
        new ViewerWindowStateService(StateStore.InMemory());

    [Fact]
    public void App_states_shutdown_on_last_window_close()
    {
        Assert.Equal(ShutdownMode.OnLastWindowClose, Nfty.Desktop.App.Shutdown);

        // The constant is only a promise if the app assigns it: read the one line that does.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Nfty.Desktop", "App.axaml.cs"));
        Assert.Contains("desktop.ShutdownMode = Shutdown;", source);
    }

    [AvaloniaFact]
    public void The_viewer_window_is_not_owned_by_the_main_window()
    {
        var main = new Window { Width = 300, Height = 200 };
        main.Show();
        var host = Host(MemoryState(), () => main);
        ViewerWindow? opened = null;
        host.Opened += w => opened = w;
        using var art = ViewerViewTests.Art();
        try
        {
            host.Open(new ViewerViewTests.TrackedSubject("owned?", art, null));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(opened);
            Assert.Null(opened!.Owner);

            // Closing the shell leaves the viewer open: an owned window would have gone with it.
            main.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.True(opened.IsVisible);
        }
        finally { opened?.Close(); }
    }

    [AvaloniaFact]
    public void Closing_a_viewer_writes_the_size_for_its_layout()
    {
        var state = MemoryState();
        var host = Host(state, () => null);
        var windows = new System.Collections.Generic.List<ViewerWindow>();
        host.Opened += windows.Add;
        using var art = ViewerViewTests.Art();

        // A 2D-only subject writes the 2D entry and leaves the 3D one alone.
        host.Open(new ViewerViewTests.TrackedSubject("flat", art, null));
        var flat = windows[0];
        flat.Width = 777; flat.Height = 555;
        Dispatcher.UIThread.RunJobs();
        flat.Close();
        Assert.Equal(777, state.Current.TwoD.Width);
        Assert.Equal(555, state.Current.TwoD.Height);
        Assert.Equal(ViewerWindowState.DefaultThreeD, state.Current.ThreeD);

        // A subject with a model writes the 3D entry.
        host.Open(new ViewerViewTests.TrackedSubject("solid", art, ViewerViewTests.Model()));
        var solid = windows[1];
        solid.Width = 1000; solid.Height = 640;
        Dispatcher.UIThread.RunJobs();
        solid.Close();
        Assert.Equal(1000, state.Current.ThreeD.Width);
        Assert.Equal(640, state.Current.ThreeD.Height);
        Assert.Equal(777, state.Current.TwoD.Width);
    }

    [AvaloniaFact]
    public void The_next_viewer_opens_at_the_remembered_size_for_its_layout()
    {
        var state = MemoryState();
        state.Save(state.Current with { TwoD = new ViewerWindowEntry(820, 610, null, null, false) });
        var host = Host(state, () => null);
        ViewerWindow? opened = null;
        host.Opened += w => opened = w;
        using var art = ViewerViewTests.Art();
        host.Open(new ViewerViewTests.TrackedSubject("flat", art, null));
        try
        {
            Assert.Equal(820, opened!.Width);
            Assert.Equal(610, opened.Height);
        }
        finally { opened?.Close(); }
    }

    [AvaloniaFact]
    public void Closing_a_viewer_disposes_the_subject_it_was_handed()
    {
        var host = Host(MemoryState(), () => null);
        ViewerWindow? opened = null;
        host.Opened += w => opened = w;
        using var art = ViewerViewTests.Art();
        var subject = new ViewerViewTests.TrackedSubject("mine now", art, null);
        host.Open(subject);
        Assert.False(subject.Disposed);
        opened!.Close();
        Assert.True(subject.Disposed);
    }

    [AvaloniaFact]
    public void Escape_with_no_dialog_open_leaves_the_viewer_open()
    {
        var host = Host(MemoryState(), () => null);
        ViewerWindow? opened = null;
        host.Opened += w => opened = w;
        using var art = ViewerViewTests.Art();
        host.Open(new ViewerViewTests.TrackedSubject("stay", art, null));
        Dispatcher.UIThread.RunJobs();
        try
        {
            opened!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(opened!.IsVisible);
        }
        finally { opened?.Close(); }
    }

    [AvaloniaFact]
    public void Escape_closes_the_windows_own_dialog_and_ctrl_w_closes_the_window()
    {
        var host = Host(MemoryState(), () => null);
        ViewerWindow? opened = null;
        host.Opened += w => opened = w;
        using var art = ViewerViewTests.Art();
        host.Open(new ViewerViewTests.TrackedSubject("dlg", art, null));
        Dispatcher.UIThread.RunJobs();

        var dialogs = opened!.Dialogs;
        _ = dialogs.ShowAsync<object>(new ErrorDialogViewModel(dialogs, "Could not save", "disk full"));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(dialogs.Active);

        opened!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(dialogs.Active);
        Assert.True(opened.IsVisible);

        opened.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.False(opened.IsVisible);
    }

    [AvaloniaFact]
    public void A_remembered_position_off_every_screen_opens_centered()
    {
        var host = Host(MemoryState(), () => null);
        var state = MemoryState();
        state.Save(state.Current with { TwoD = new ViewerWindowEntry(800, 600, -30000, -30000, false) });
        host = Host(state, () => null);
        ViewerWindow? opened = null;
        host.Opened += w => opened = w;
        using var art = ViewerViewTests.Art();
        host.Open(new ViewerViewTests.TrackedSubject("lost", art, null));
        try { Assert.Equal(WindowStartupLocation.CenterScreen, opened!.WindowStartupLocation); }
        finally { opened?.Close(); }
    }

    [Fact]
    public void Placement_clamps_onto_a_screen_that_exists_and_gives_up_on_one_that_does_not()
    {
        var left = new PixelRect(0, 0, 1920, 1040);
        var right = new PixelRect(1920, 0, 1920, 1040);
        var entry = (double x, double y) => new ViewerWindowEntry(800, 600, x, y, false);

        // On a screen: kept.
        Assert.Equal(new PixelPoint(100, 100), ViewerWindowPlacement.Resolve(entry(100, 100), [left, right]));
        // Hanging off the bottom-right of the second screen: shifted back inside it.
        Assert.Equal(new PixelPoint(3840 - 800, 1040 - 600), ViewerWindowPlacement.Resolve(entry(3700, 900), [left, right]));
        // The second monitor was unplugged: that position is on no screen any more.
        Assert.Null(ViewerWindowPlacement.Resolve(entry(2500, 100), [left]));
        // Nothing was ever remembered.
        Assert.Null(ViewerWindowPlacement.Resolve(new ViewerWindowEntry(800, 600, null, null, false), [left]));
    }

    [Fact]
    public void A_corrupt_window_state_reads_as_defaults()
    {
        foreach (var junk in new[] { "{ not json", "[]", "{\"twoD\": 5}", "" })
        {
            var store = StateStore.InMemory();
            store.Write(ViewerWindowStateService.FileName, junk);
            Assert.Equal(ViewerWindowState.Default, new ViewerWindowStateService(store).Current);
        }

        // A window under the minimum cannot be opened, so it is not trusted.
        var tiny = StateStore.InMemory();
        tiny.Write(ViewerWindowStateService.FileName,
            "{\"twoD\":{\"width\":10,\"height\":10,\"maximized\":false},\"threeD\":{\"width\":1500,\"height\":900,\"maximized\":true}}");
        var read = new ViewerWindowStateService(tiny).Current;
        Assert.Equal(ViewerWindowState.DefaultTwoD, read.TwoD);
        Assert.Equal(new ViewerWindowEntry(1500, 900, null, null, true), read.ThreeD);
    }

    [Fact]
    public void The_window_state_round_trips_through_the_store_with_the_backdrop()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var first = new ViewerWindowStateService(StateStore.At(dir.FullName));
            var wanted = new ViewerWindowState(
                new ViewerWindowEntry(910, 705, 40, 50, false),
                new ViewerWindowEntry(1300, 800, null, null, true),
                nameof(ViewerBackdrop.Custom), "#112233");
            first.Save(wanted);

            var reopened = new ViewerWindowStateService(StateStore.At(dir.FullName));
            Assert.Equal(wanted, reopened.Current);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_state_file_from_before_the_backdrop_existed_reads_with_the_default_backdrop()
    {
        var store = StateStore.InMemory();
        store.Write(ViewerWindowStateService.FileName,
            "{\"twoD\":{\"width\":900,\"height\":700,\"maximized\":false},\"threeD\":{\"width\":1280,\"height\":760,\"maximized\":false}}");
        var read = new ViewerWindowStateService(store).Current;
        Assert.Equal("Checker", read.Backdrop);
        Assert.Equal("#808080", read.BackdropColor);
    }

    [AvaloniaFact]
    public void The_backdrop_the_reader_chose_is_remembered_and_applied_to_the_next_viewer()
    {
        var state = MemoryState();
        var host = Host(state, () => null);
        var windows = new System.Collections.Generic.List<ViewerWindow>();
        host.Opened += windows.Add;
        using var art = ViewerViewTests.Art();

        host.Open(new ViewerViewTests.TrackedSubject("first", art, null));
        windows[0].ViewModel.TwoD.Backdrop = ViewerBackdrop.Custom;
        windows[0].ViewModel.TwoD.BackdropColor = "#abcdef";
        windows[0].Close();
        Assert.Equal("Custom", state.Current.Backdrop);
        Assert.Equal("#abcdef", state.Current.BackdropColor);

        host.Open(new ViewerViewTests.TrackedSubject("second", art, null));
        try
        {
            Assert.Equal(ViewerBackdrop.Custom, windows[1].ViewModel.TwoD.Backdrop);
            Assert.Equal("#abcdef", windows[1].ViewModel.TwoD.BackdropColor);
        }
        finally { windows[1].Close(); }
    }

    [AvaloniaFact]
    public void The_outline_display_the_reader_chose_is_remembered_and_applied_to_the_next_viewer()
    {
        var state = MemoryState();
        var host = Host(state, () => null);
        var windows = new System.Collections.Generic.List<ViewerWindow>();
        host.Opened += windows.Add;
        using var art = ViewerViewTests.Art();
        var mesh = Nfty.Model.Riser.TryBuild(art, null, new Nfty.Model.RiseOptions(), "t")!;
        using var atlas = Nfty.Model.FaceAtlas.Build(art);
        var model = new Nfty.App.Viewer.ViewerModel(mesh, atlas, null);

        host.Open(new ViewerViewTests.TrackedSubject("first", art, model));
        Assert.False(windows[0].ViewModel.Preview.Wireframe);
        windows[0].ViewModel.Preview.Wireframe = true;
        windows[0].Close();
        Assert.True(state.Current.Wireframe);

        host.Open(new ViewerViewTests.TrackedSubject("second", art, model));
        try { Assert.True(windows[1].ViewModel.Preview.Wireframe); }
        finally { windows[1].Close(); }
    }
}
