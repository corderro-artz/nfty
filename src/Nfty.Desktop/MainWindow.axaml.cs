using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);

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
