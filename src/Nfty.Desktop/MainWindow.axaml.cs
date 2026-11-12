using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;
using Nfty.App.Views;

namespace Nfty.Desktop;

/// <summary>
/// The application window. It supplies only what genuinely needs a <see cref="Window"/> — the
/// custom chrome's minimise/maximise/close, the drag region and the resize grip; everything visible
/// lives in <c>Nfty.App</c>'s ShellChromeView so the visual-capture tests render the shipped control
/// rather than a replica.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Loads the window and wires its chrome behavior.</summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Custom chrome (SystemDecorations=None): titlebar drags the window and double-click
        // toggles maximize; the corner grip resizes. The OS provides none of this here. The chrome
        // itself is Nfty.App's ShellChromeView, which exposes these two surfaces as properties -
        // FindControl cannot cross into a UserControl's own name scope.
        var chrome = this.FindControl<ShellChromeView>("Chrome")!;
        ChromeWiring.Attach(this, chrome.Frame, captionCommands: false);   // the shell's commands are bound
    }

    private bool _closeConfirmed;
    private ShellViewModel? _attachedShell;

    /// <summary>Routes window close requests through the current shell's unsaved-work check.
    /// An operating system may still force process termination during shutdown.</summary>
    /// <param name="e">Canceled while the shell asks; the window closes itself once it answers yes.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not ShellViewModel shell) return;
        e.Cancel = true;
        _ = ConfirmThenCloseAsync(shell);
    }

    private async Task ConfirmThenCloseAsync(ShellViewModel shell)
    {
        bool alreadyAsking = shell.PendingClose is { IsCompleted: false };
        var answer = shell.BeginClose();
        if (alreadyAsking) return;        // the request already running closes the window on yes
        if (await answer && ReferenceEquals(DataContext, shell))
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>Subscribes to the shell's window-command events once its ViewModel is attached.</summary>
    /// <param name="e">Ignored beyond forwarding to the base implementation.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        DetachShell();
        if (DataContext is not ShellViewModel shell) return;
        _attachedShell = shell;
        shell.MinimizeRequested += Minimize;
        shell.ToggleMaximizeRequested += ToggleMaximize;
        shell.ToggleFullScreenRequested += ToggleFullScreen;
        shell.IsFullScreen = WindowState == WindowState.FullScreen;
        shell.CloseRequested += Close;
    }

    private void Minimize() => WindowState = WindowState.Minimized;
    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private WindowState _beforeFullScreen = WindowState.Normal;
    private bool _leavingFullScreen;

    /// <summary>Enters or leaves full screen, returning to the state (normal or maximized) it left.</summary>
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            _leavingFullScreen = true;
            try { WindowState = _beforeFullScreen == WindowState.Minimized ? WindowState.Normal : _beforeFullScreen; }
            finally { _leavingFullScreen = false; }
            return;
        }
        _beforeFullScreen = WindowState;
        WindowState = WindowState.FullScreen;
    }

    /// <summary>Keeps the shell's full screen flag equal to the real window state, and keeps full
    /// screen until its own control (F11 / the status-bar button) leaves it: Escape, a title-bar drag,
    /// a double-click or any other gesture that would restore or maximize the window is put back.
    /// Minimizing is the OS's and stays allowed.</summary>
    /// <param name="change">The changed property.</param>
    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty) return;
        if (!_leavingFullScreen && change.OldValue is WindowState.FullScreen
            && change.NewValue is WindowState.Normal or WindowState.Maximized)
        {
            WindowState = WindowState.FullScreen;
            return;
        }
        if (_attachedShell is { } shell) shell.IsFullScreen = WindowState == WindowState.FullScreen;
    }

    private void DetachShell()
    {
        if (_attachedShell is not { } shell) return;
        shell.MinimizeRequested -= Minimize;
        shell.ToggleMaximizeRequested -= ToggleMaximize;
        shell.ToggleFullScreenRequested -= ToggleFullScreen;
        shell.CloseRequested -= Close;
        _attachedShell = null;
    }

    /// <summary>Disconnects caption commands when their window is gone.</summary>
    /// <param name="e">The closed notification.</param>
    protected override void OnClosed(EventArgs e)
    {
        DetachShell();
        base.OnClosed(e);
    }
}
