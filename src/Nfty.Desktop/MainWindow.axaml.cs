using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
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
    /// <summary>Loads the window and wires its chrome behaviour.</summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        // Custom chrome (SystemDecorations=None): titlebar drags the window and double-click
        // toggles maximize; the corner grip resizes. The OS provides none of this here. The chrome
        // itself is Nfty.App's ShellChromeView, which exposes these two surfaces as properties -
        // FindControl cannot cross into a UserControl's own name scope.
        var chrome = this.FindControl<ShellChromeView>("Chrome")!;
        chrome.TitlebarArea.PointerPressed += OnTitlebarPointerPressed;
        chrome.ResizeGripArea.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginResizeDrag(WindowEdge.SouthEast, e);
        };
    }

    private void OnTitlebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Let the window-control buttons (min/max/close) handle their own clicks.
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is not null) return;

        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    /// <summary>Subscribes to the shell's window-command events once its ViewModel is attached.</summary>
    /// <param name="e">Ignored beyond forwarding to the base implementation.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ShellViewModel shell)
        {
            shell.MinimizeRequested += () => WindowState = WindowState.Minimized;
            shell.ToggleMaximizeRequested += () =>
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            shell.CloseRequested += Close;
        }
    }
}
