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
        shell.CloseRequested += Close;
    }

    private void Minimize() => WindowState = WindowState.Minimized;
    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void DetachShell()
    {
        if (_attachedShell is not { } shell) return;
        shell.MinimizeRequested -= Minimize;
        shell.ToggleMaximizeRequested -= ToggleMaximize;
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
