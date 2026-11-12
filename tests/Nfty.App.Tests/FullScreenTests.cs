using System.Linq;
using Avalonia;
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
    [AvaloniaFact]
    public void Custom_chrome_does_not_add_a_platform_fullscreen_caption()
    {
        var window = new MainWindow();
        try
        {
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.False(window.ExtendClientAreaToDecorationsHint);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Caption_icons_keep_equal_spacing_in_both_fullscreen_states()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            var buttons = new[] { "FullScreenButton", "MinimizeButton", "MaximizeButton", "CloseButton" }
                .Select(name => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name)).ToArray();
            Avalonia.Point[] Centers() => buttons.Select(b => b.TranslatePoint(
                new Avalonia.Point(b.Bounds.Width / 2, b.Bounds.Height / 2), window)!.Value).ToArray();
            Dispatcher.UIThread.RunJobs();
            var before = Centers();
            foreach (var state in new[] { WindowState.FullScreen, WindowState.Normal })
            {
                // Full screen is left only through its own control, so go out the way a user does.
                if (state == WindowState.FullScreen) window.WindowState = state;
                else shell.ToggleFullScreenCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var centers = Centers();
                double spacing = centers[1].X - centers[0].X;
                Assert.True(spacing > 0);
                for (int i = 1; i < centers.Length; i++)
                {
                    Assert.Equal(spacing, centers[i].X - centers[i - 1].X, 5);
                    Assert.Equal(centers[0].Y, centers[i].Y, 5);
                    Assert.Equal(buttons[0].Bounds.Size, buttons[i].Bounds.Size);
                }
                var frame = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("frame"));
                if (state == WindowState.FullScreen)
                {
                    Assert.Equal(new Avalonia.Thickness(0), frame.Margin);
                    Assert.Equal(window.ClientSize, frame.Bounds.Size);
                }
                else Assert.Equal(before, centers);
            }
        }
        finally { window.Close(); }
    }

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

    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void Dragging_the_titlebar_never_leaves_full_screen(WindowState before)
    {
        // A title-bar press used to pull the window out of full screen. Only the full screen control
        // (F11 / the button) leaves it now; the titlebar is inert.
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            window.WindowState = before;
            shell.ToggleFullScreenCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsFullScreen);

            var frame = window.GetVisualDescendants().OfType<Nfty.App.Controls.WindowFrame>().First();
            var bar = frame.TitlebarArea;
            var empty = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2), window)!.Value;
            window.MouseDown(empty, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(empty, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.True(shell.IsFullScreen);
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
            Assert.NotSame(paths[0].Data, paths[1].Data);
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
