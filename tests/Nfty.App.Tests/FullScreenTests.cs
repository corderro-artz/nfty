using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Desktop;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Full screen: F11 and the titlebar button toggle it, the shell follows the real window state, and
/// Escape (which closes dialogs) never leaves it.
/// </summary>
public class FullScreenTests
{
    private static ShellViewModel Shell(DialogService? dialogs = null) =>
        new(new FakeNav(), dialogs ?? new DialogService(), new ThemeService(), new StatusService());

    [AvaloniaFact]
    public void The_command_asks_the_head_and_does_not_flip_the_flag_itself()
    {
        var shell = Shell();
        var asked = 0;
        shell.ToggleFullScreenRequested += () => asked++;

        shell.ToggleFullScreenCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.False(shell.IsFullScreen);     // the head reports the real state back
    }

    [AvaloniaFact]
    public void F11_toggles_the_main_window_and_the_shell_follows()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.True(shell.IsFullScreen);

            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.False(shell.IsFullScreen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Leaving_full_screen_returns_a_maximized_window_to_maximized()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            window.WindowState = WindowState.Maximized;
            shell.ToggleFullScreenCommand.Execute(null);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            shell.ToggleFullScreenCommand.Execute(null);
            Assert.Equal(WindowState.Maximized, window.WindowState);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Escape_closes_a_dialog_and_leaves_full_screen_alone()
    {
        var dialogs = new DialogService();
        var shell = Shell(dialogs);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            shell.ToggleFullScreenCommand.Execute(null);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            _ = dialogs.ShowAsync<bool>(new ConfirmDialogViewModel(dialogs, "t", "m", "Yes"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.HasDialog);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.False(shell.HasDialog);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.True(shell.IsFullScreen);

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Equal(WindowState.FullScreen, window.WindowState);   // nothing open: still no exit
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_titlebar_button_is_there_and_shows_the_way_out_while_full_screen()
    {
        var shell = Shell();
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "FullScreenButton");
            Assert.True(button.IsVisible);
            Assert.NotNull(button.Command);

            var paths = button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().ToList();
            Assert.Equal(2, paths.Count);
            Assert.True(paths[0].IsVisible);
            Assert.False(paths[1].IsVisible);

            shell.IsFullScreen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(paths[0].IsVisible);
            Assert.True(paths[1].IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void F11_is_in_the_quick_reference()
    {
        Assert.Contains(HelpCatalog.Entries, e => e.Cap == KeyHints.FullScreen);
    }
}
