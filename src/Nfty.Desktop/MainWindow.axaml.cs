using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
