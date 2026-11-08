using System.Reflection;
using System.IO.Compression;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.Desktop;
using Microsoft.Extensions.DependencyInjection;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Desktop lifetime regressions, exercised against the actual windows and host.</summary>
public class DesktopAuditTests
{
    /// <summary>Supplies only the native interface boundary used by a desktop service.</summary>
    public class NativeProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!, args);
    }

    private static T Native<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, NativeProxy>();
        ((NativeProxy)(object)proxy).Call = call;
        return proxy;
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private static IDisposable Lifetime(Window window)
    {
        var field = typeof(Application).GetField("_applicationLifetime", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var old = field.GetValue(Application.Current);
        field.SetValue(Application.Current, Native<IClassicDesktopStyleApplicationLifetime>((method, _) => method.Name switch
        {
            "get_MainWindow" => window,
            "get_Windows" => (IReadOnlyList<Window>)[window],
            _ => throw new NotSupportedException(method.Name),
        }));
        return new Scope(() => field.SetValue(Application.Current, old));
    }

    private sealed class RecordingHost : IViewerHost
    {
        public int Count { get; private set; }
        public void Open(ViewerSubject subject) { Count++; subject.Dispose(); }
    }

    private sealed class AwaitedPage : ViewModelBase, IUnsavedWork
    {
        public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HasUnsavedWork => true;
        public bool IsBusy => false;
        public Task<bool> ConfirmLeaveAsync() => Answer.Task;
    }

    private sealed class FinishedCard : ViewModelBase, IUnsavedWork, ICancellableDialog
    {
        public bool HasUnsavedWork => false;
        public bool IsBusy => false;
        public Task<bool> ConfirmLeaveAsync() => Task.FromResult(true);
        public Task CancelAndWaitAsync() => Task.CompletedTask;
    }

    private sealed class UnknownBusyCard : ViewModelBase, IUnsavedWork
    {
        public bool HasUnsavedWork => false;
        public bool IsBusy => true;
        public Task<bool> ConfirmLeaveAsync() => Task.FromResult(false);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Escape_dismisses_finished_cards_but_preserves_unknown_busy_work(bool busy)
    {
        var dialogs = new DialogService();
        using var vm = new ViewerViewModel(new ViewerViewTests.TrackedSubject("card", null, null),
            new FilePickerService(), new NoopClipboardService(), dialogs);
        var window = new ViewerWindow(vm, dialogs, new ViewerWindowStateService(StateStore.InMemory()),
            ViewerWindowState.DefaultTwoD);
        window.Show();
        var answer = dialogs.ShowAsync<object>(busy ? new UnknownBusyCard() : new FinishedCard());
        try
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await Task.Yield();
            Assert.Equal(busy, dialogs.Active is not null);
        }
        finally { dialogs.Close(null); await answer; window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_delayed_answer_from_a_replaced_shell_cannot_close_the_current_shell()
    {
        var nav = new FakeNav();
        var page = new AwaitedPage();
        nav.To(page);
        var old = new ShellViewModel(nav, new DialogService(), new ThemeService(), new StatusService());
        var window = new MainWindow { DataContext = old };
        window.Show();
        try
        {
            window.Close();
            Assert.False(old.PendingClose!.IsCompleted);
            window.DataContext = Shell();
            page.Answer.SetResult(true);
            await old.PendingClose;
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
        }
        finally { page.Answer.TrySetResult(false); window.Close(); }
    }

    private static ShellViewModel Shell() => new(new FakeNav(), new DialogService(),
        new ThemeService(), new StatusService());

    [AvaloniaFact]
    public void A_clean_main_window_really_closes_on_its_first_request()
    {
        var window = new MainWindow { DataContext = Shell() };
        window.Show();
        window.Close();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void A_replaced_shell_cannot_control_the_window()
    {
        var old = Shell();
        var current = Shell();
        var window = new MainWindow { DataContext = old };
        window.Show();
        try
        {
            window.DataContext = current;
            old.ToggleMaximizeCommand.Execute(null);
            Assert.Equal(WindowState.Normal, window.WindowState);
            current.ToggleMaximizeCommand.Execute(null);
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Reattaching_the_same_shell_does_not_duplicate_caption_commands()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            window.DataContext = null;
            window.DataContext = shell;
            shell.ToggleMaximizeCommand.Execute(null);
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally { window.Close(); }
    }

    private sealed class FailingState : IViewerWindowState
    {
        public ViewerWindowState Current => throw new IOException("State unavailable");
        public void Save(ViewerWindowState state) { }
    }

    [AvaloniaFact]
    public void A_failed_viewer_handoff_disposes_the_transferred_subject()
    {
        var subject = new ViewerViewTests.TrackedSubject("failed", null, null);
        var host = new DesktopViewerHost(new FilePickerService(), new NoopClipboardService(),
            new FailingState(), new ImageBridge(), () => null);
        Assert.Throws<IOException>(() => host.Open(subject));
        Assert.True(subject.Disposed);
    }

    [AvaloniaFact]
    public void A_failed_window_show_disposes_the_transferred_subject()
    {
        var subject = new ViewerViewTests.TrackedSubject("failed", null, null);
        var host = new DesktopViewerHost(new FilePickerService(), new NoopClipboardService(),
            new ViewerWindowStateService(StateStore.InMemory()), new ImageBridge(), () => null);
        ViewerWindow? created = null;
        host.Opened += window => { created = window; throw new IOException("Show failed"); };
        try
        {
            Assert.Throws<IOException>(() => host.Open(subject));
            Assert.True(subject.Disposed);
        }
        finally { created?.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_a_viewer_completes_its_outstanding_dialog_answer()
    {
        var host = new DesktopViewerHost(new FilePickerService(), new NoopClipboardService(),
            new ViewerWindowStateService(StateStore.InMemory()), new ImageBridge(), () => null);
        ViewerWindow? window = null;
        host.Opened += w => window = w;
        host.Open(new ViewerViewTests.TrackedSubject("dialog", null, null));
        var answer = window!.Dialogs.ShowAsync<object>(new ErrorDialogViewModel(window.Dialogs, "Error", "Read failed"));
        window.Close();
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        Assert.True(answer.IsCompleted);
        Assert.Null(await answer);
    }

    [AvaloniaFact]
    public async Task Closing_the_launch_window_completes_its_passphrase_or_error_answer()
    {
        var window = new ViewerLaunchWindow("missing.png");
        window.Show();
        var answer = window.Dialogs.ShowAsync<object>(new ErrorDialogViewModel(window.Dialogs, "Error", "Read failed"));
        window.Close();
        Dispatcher.UIThread.RunJobs();
        await Task.Yield();
        Assert.True(answer.IsCompleted);
        Assert.Null(await answer);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_busy_window_waits_for_cleanup_before_closing(bool launch)
    {
        var dialogs = new DialogService();
        using var vm = new ViewerViewModel(new ViewerViewTests.TrackedSubject("busy", null, null),
            new FilePickerService(), new NoopClipboardService(), dialogs);
        Window window = launch ? new ViewerLaunchWindow("busy.set") : new ViewerWindow(vm, dialogs,
            new ViewerWindowStateService(StateStore.InMemory()), ViewerWindowState.DefaultTwoD);
        var actualDialogs = launch ? ((ViewerLaunchWindow)window).Dialogs : dialogs;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool canceled = false;
        window.Show();
        var operation = BusyViewModel.RunAsync(actualDialogs, "Reading", "Reading…", async (_, ct) =>
        {
            using var registration = ct.Register(() => canceled = true);
            await release.Task;
            return 1;
        }, cancellable: true);
        try
        {
            window.Close();
            Assert.True(window.IsVisible);
            Assert.True(canceled);
            Assert.IsType<BusyViewModel>(actualDialogs.Active);
            release.SetResult();
            await operation;
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        }
        finally { release.TrySetResult(); await operation; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Escape_requests_busy_cancellation_without_dismissing_the_running_card(bool launch)
    {
        var dialogs = new DialogService();
        using var vm = new ViewerViewModel(new ViewerViewTests.TrackedSubject("busy", null, null),
            new FilePickerService(), new NoopClipboardService(), dialogs);
        Window window = launch ? new ViewerLaunchWindow("busy.set") : new ViewerWindow(vm, dialogs,
            new ViewerWindowStateService(StateStore.InMemory()), ViewerWindowState.DefaultTwoD);
        var actualDialogs = launch ? ((ViewerLaunchWindow)window).Dialogs : dialogs;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool canceled = false;
        window.Show();
        var operation = BusyViewModel.RunAsync(actualDialogs, "Reading", "Reading…", async (_, ct) =>
        {
            using var registration = ct.Register(() => canceled = true);
            await release.Task;
            return 1;
        }, cancellable: true);
        try
        {
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.True(canceled);
            Assert.IsType<BusyViewModel>(actualDialogs.Active);
            Assert.True(window.IsVisible);
        }
        finally { release.TrySetResult(); await operation; window.Close(); }
    }

    [AvaloniaFact]
    public void The_main_window_requests_screen_centering_on_every_launch()
    {
        var window = new MainWindow();
        try { Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_new_viewer_centers_instead_of_restoring_old_coordinates()
    {
        var state = new ViewerWindowStateService(StateStore.InMemory());
        state.Save(state.Current with { TwoD = new ViewerWindowEntry(1000, 600, 30, 40, false) });
        var host = new DesktopViewerHost(new FilePickerService(), new NoopClipboardService(),
            state, new ImageBridge(), () => null);
        ViewerWindow? window = null;
        host.Opened += w => window = w;
        host.Open(new ViewerViewTests.TrackedSubject("center", null, null));
        try
        {
            Assert.Equal(WindowStartupLocation.CenterScreen, window!.WindowStartupLocation);
            Assert.Equal(1000, window.Width);
            Assert.Equal(600, window.Height);
        }
        finally { window?.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("open")]
    [InlineData("save")]
    [InlineData("folder")]
    public async Task Pickers_follow_a_live_viewer_after_the_main_window_closes(string kind)
    {
        var directory = Directory.CreateTempSubdirectory();
        var expected = Path.Combine(directory.FullName, "space % # é.cbk");
        var main = new Window();
        var viewer = new Window();
        main.Show();
        main.Close();
        viewer.Show();
        var oldLifetime = Application.Current!.ApplicationLifetime;
        var lifetime = Native<IClassicDesktopStyleApplicationLifetime>((method, _) => method.Name switch
        {
            "get_MainWindow" => main,
            "get_Windows" => (IReadOnlyList<Window>)[viewer],
            _ => throw new NotSupportedException(method.Name),
        });
        int released = 0;
        object? Release() { released++; return null; }
        var item = Native<IStorageFile>((method, _) => method.Name switch
        {
            "get_Path" => new Uri(expected),
            "Dispose" => Release(),
            _ => throw new NotSupportedException(method.Name),
        });
        var folder = Native<IStorageFolder>((method, _) => method.Name switch
        {
            "get_Path" => new Uri(directory.FullName),
            "Dispose" => Release(),
            _ => throw new NotSupportedException(method.Name),
        });
        var provider = Native<IStorageProvider>((method, _) => method.Name switch
        {
            "OpenFilePickerAsync" => Task.FromResult<IReadOnlyList<IStorageFile>>([item]),
            "SaveFilePickerAsync" => Task.FromResult<IStorageFile?>(item),
            "OpenFolderPickerAsync" => Task.FromResult<IReadOnlyList<IStorageFolder>>([folder]),
            _ => throw new NotSupportedException(method.Name),
        });
        typeof(TopLevel).GetField("_storageProvider", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(viewer, provider);
        var lifetimeField = typeof(Application).GetField("_applicationLifetime", BindingFlags.NonPublic | BindingFlags.Instance)!;
        // Avalonia locks its public setter after startup; this scoped test boundary substitutes only
        // the platform lifetime and restores it before the headless session tears down.
        lifetimeField.SetValue(Application.Current, lifetime);
        try
        {
            var picker = new DesktopFilePicker();
            var actual = kind switch
            {
                "open" => await picker.OpenFileAsync("Open", ".cbk"),
                "save" => await picker.SaveFileAsync("Save", ".cbk"),
                _ => await picker.PickFolderAsync("Folder"),
            };
            Assert.Equal(kind == "folder" ? directory.FullName : expected, actual);
            Assert.Equal(1, released);
        }
        finally
        {
            lifetimeField.SetValue(Application.Current, oldLifetime);
            viewer.Close();
            directory.Delete(true);
        }
    }

    [AvaloniaFact]
    public void An_invalid_viewer_launch_path_shows_an_error_instead_of_silently_exiting()
    {
        var directory = Directory.CreateTempSubdirectory();
        ViewerLaunchWindow? launch = null;
        using var provider = new ServiceCollection()
            .AddSingleton<IViewerHost>(new RecordingHost())
            .AddSingleton<IRecentsService>(new RecentsService(directory.FullName))
            .BuildServiceProvider();
        var desktop = Native<IClassicDesktopStyleApplicationLifetime>((method, args) =>
        {
            if (method.Name == "set_MainWindow") launch = (ViewerLaunchWindow)args![0]!;
            else if (method.Name is not ("set_ShutdownMode" or "add_Exit")) throw new NotSupportedException(method.Name);
            return null;
        });
        try
        {
            typeof(Nfty.Desktop.App).GetMethod("StartViewerOnly", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [desktop, provider, "invalid\0.png"]);
            Assert.NotNull(launch);
            Assert.True(launch!.IsVisible);
            Assert.IsType<ErrorDialogViewModel>(launch.Dialogs.Active);
        }
        finally { launch?.Close(); directory.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Clearing_clipboard_art_releases_the_native_bitmap()
    {
        var window = new Window();
        window.Show();
        using var lifetime = Lifetime(window);
        var clipboard = window.Clipboard!;
        using var art = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(1, 1);
        using var png = new MemoryStream();
        art.Save(png, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        try
        {
            var service = new DesktopClipboard();
            await service.SetImageAsync(png.ToArray());
            var current = await clipboard.TryGetInProcessDataAsync();
            var bitmap = await current!.TryGetValueAsync(DataFormat.Bitmap);
            Assert.NotNull(bitmap);
            Assert.Equal(new PixelSize(1, 1), bitmap!.PixelSize);
            await clipboard.ClearAsync();
            Assert.Throws<ObjectDisposedException>(() => _ = bitmap.PixelSize);
        }
        finally { await clipboard.ClearAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_during_uncancellable_decryption_does_not_open_a_late_viewer()
    {
        var directory = Directory.CreateTempSubdirectory();
        var setDirectory = Path.Combine(directory.FullName, "set");
        using (var book = CoreTestBook.Tiny())
        using (var set = Generator.Generate(book, new GenerateOptions(1, "desktop-late")))
            SetWriter.Write(set, setDirectory, pack: false);
        var packed = Path.Combine(directory.FullName, "set.zip");
        ZipFile.CreateFromDirectory(setDirectory, packed);
        var tin = Path.Combine(directory.FullName, "set.tin");
        using (var plain = File.OpenRead(packed))
            Seal.Write(tin, plain, "Book", 1, null, SealPolicy.ViewOnly, "desktop-test-pass", 1000);
        var host = new RecordingHost();
        ViewerLaunchWindow? launch = null;
        bool askedToClose = false;
        using var provider = new ServiceCollection()
            .AddSingleton<IViewerHost>(host)
            .AddSingleton<IRecentsService>(new RecentsService(Path.Combine(directory.FullName, "recents")))
            .BuildServiceProvider();
        var desktop = Native<IClassicDesktopStyleApplicationLifetime>((method, args) =>
        {
            if (method.Name == "set_MainWindow")
            {
                launch = (ViewerLaunchWindow)args![0]!;
                launch.Dialogs.Changed += () =>
                {
                    if (launch.Dialogs.Active is PassphraseDialogViewModel) launch.Dialogs.Close("desktop-test-pass");
                    else if (launch.Dialogs.Active is BusyViewModel && !askedToClose)
                    {
                        askedToClose = true;
                        launch.Close();
                    }
                };
            }
            else if (method.Name is not ("set_ShutdownMode" or "add_Exit")) throw new NotSupportedException(method.Name);
            return null;
        });
        try
        {
            typeof(Nfty.Desktop.App).GetMethod("StartViewerOnly", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [desktop, provider, tin]);
            Assert.NotNull(launch!.PendingOpen);
            await launch.PendingOpen;
            Assert.True(askedToClose);
            Assert.False(launch.IsVisible);
            Assert.Equal(0, host.Count);
        }
        finally { launch?.Close(); directory.Delete(true); }
    }

    [Theory]
    [InlineData("libXrandr", "libxrandr2", "libXrandr", "libxrandr")]
    [InlineData("libXi", "libxi6", "libXi", "libxi")]
    [InlineData("libXcursor", "libxcursor1", "libXcursor", "libxcursor")]
    public void Linux_startup_hints_include_the_package_that_supplies_the_missing_library(
        string library, string ubuntu, string fedora, string arch)
    {
        var program = typeof(Nfty.Desktop.App).Assembly.GetType("Nfty.Desktop.Program")!;
        var method = program.GetMethod("MissingLinuxLibrary", BindingFlags.NonPublic | BindingFlags.Static)!;
        var message = (string?)method.Invoke(null, [new DllNotFoundException($"Unable to load {library}.so")]);
        Assert.NotNull(message);
        var lines = message!.Split('\n');
        Assert.Contains(ubuntu, lines.Single(l => l.Contains("apt install", StringComparison.Ordinal)).Split(' '));
        Assert.Contains(fedora, lines.Single(l => l.Contains("dnf install", StringComparison.Ordinal)).Split(' '));
        Assert.Contains(arch, lines.Single(l => l.Contains("pacman -S", StringComparison.Ordinal)).Split(' '));
    }

    [Theory]
    [InlineData("ordinary folder")]
    [InlineData("folder with \"quotes\" and #é%")]
    [InlineData("folder\\trailing\\")]
    public void Folder_reveal_transports_the_whole_path_as_one_native_argument(string path)
    {
        var factory = typeof(DesktopFolderRevealer).GetMethod("StartInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
        var launch = Assert.IsType<ProcessStartInfo>(factory.Invoke(null, [path]));
        Assert.Equal(path, Assert.Single(launch.ArgumentList));
        Assert.True(launch.UseShellExecute);
    }
}
