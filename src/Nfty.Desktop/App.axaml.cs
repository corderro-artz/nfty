using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection().AddNftyApp().BuildServiceProvider();
        var shell = services.GetRequiredService<ShellViewModel>();
        services.GetRequiredService<INavigationService>().To(services.GetRequiredService<LandingViewModel>());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = shell };
        base.OnFrameworkInitializationCompleted();
    }
}
