using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.App.Views;

namespace Nfty.Desktop;

public partial class MainWindow : Window
{
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
