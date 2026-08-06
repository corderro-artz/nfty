using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

/// <summary>
/// The desktop head. Composes the head-agnostic services from <c>Nfty.App</c> with the three whose
/// real implementation needs a window — the file picker, the folder revealer and the clipboard — and
/// puts the shell in a <see cref="MainWindow"/>.
/// </summary>
public partial class App : Application
{
    /// <summary>Loads the application's XAML.</summary>
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>Builds the container, navigates to Landing, and shows the main window. The
    /// container is disposed on exit because it owns the singleton <c>ICookBookSession</c>, which
    /// owns the open cookbook's decoded images.</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection()
            .AddNftyApp()
            .AddSingleton<IFilePickerService, DesktopFilePicker>()
            .AddSingleton<IFolderRevealer, DesktopFolderRevealer>()
            .AddSingleton<IClipboardService, DesktopClipboard>()
            .BuildServiceProvider();
        var shell = services.GetRequiredService<ShellViewModel>();
        services.GetRequiredService<INavigationService>().To(services.GetRequiredService<LandingViewModel>());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow { DataContext = shell };
            // The container owns the singleton ICookBookSession, which owns the open
            // cookbook's decoded images; dispose it on exit so that cleanup runs.
            desktop.Exit += (_, _) => services.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
