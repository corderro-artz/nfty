using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.Desktop;

/// <summary>
/// The desktop head. Composes shared services with native pickers, folder reveal, clipboard and
/// viewer windows, then starts either the shell or a viewer-only launch.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The application keeps running until its LAST window closes. Avalonia's default is the same, so
    /// this is stated rather than changed: pop-out viewers are separate windows that outlive the
    /// main one, and a later edit must not be able to end the whole app when the shell closes by
    /// silently changing the default.
    /// </summary>
    public const ShutdownMode Shutdown = ShutdownMode.OnLastWindowClose;

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
            .AddSingleton<IUrlOpener, DesktopUrlOpener>()
            .AddSingleton<IClipboardService, DesktopClipboard>();
        // A head with a second window opens a viewer in one; a single-view head draws it over the shell.
        if (ApplicationLifetime is ISingleViewApplicationLifetime) services.AddOverlayViewerHost();
        else services.AddSingleton<IViewerHost, DesktopViewerHost>();
        var provider = services.BuildServiceProvider();
        var shell = provider.GetRequiredService<ShellViewModel>();   // applies the remembered theme
        if (Program.ViewerOnly && Program.StartupPath is { } viewerPath
            && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime viewerDesktop)
        {
            StartViewerOnly(viewerDesktop, provider, viewerPath);
            base.OnFrameworkInitializationCompleted();
            return;
        }
        var landing = provider.GetRequiredService<LandingViewModel>();
        provider.GetRequiredService<INavigationService>().To(landing);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Shutdown;
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            // A path given at launch opens once the shell is ON SCREEN, through the routing a Recent
            // row uses. A missing or unknown path lands on the ordinary error card; nothing here throws.
            if (Program.StartupPath is { } startup)
                window.Opened += async (_, _) =>
                {
                    try { await landing.OpenFromCommandLineAsync(startup); }
                    catch (Exception) { /* the open reports its own failures; never crash the launch */ }
                };
            // The container owns the singleton ICookBookSession, which owns the open
            // cookbook's decoded images; dispose it on exit so that cleanup runs.
            desktop.Exit += (_, _) => provider.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// <c>nfty --viewer &lt;path&gt;</c>: the path opens in a pop-out viewer ALONE, the app shell never
    /// shown. A small launch window carries the dialog layer the open may need (busy, passphrase,
    /// error) and closes once the viewer is up - after it, so the viewer is never the window that
    /// leaves the app with none - or once a failure's error card is dismissed, which ends the app.
    /// Each launch is its own process: a second double-click opens a second viewer, which is what a
    /// viewer is for, so no single-instance forwarding is attempted.
    /// </summary>
    private static void StartViewerOnly(IClassicDesktopStyleApplicationLifetime desktop, ServiceProvider provider, string path)
    {
        desktop.ShutdownMode = Shutdown;
        var host = provider.GetRequiredService<IViewerHost>();
        var recents = provider.GetRequiredService<IRecentsService>();
        var launch = new ViewerLaunchWindow(path);
        desktop.MainWindow = launch;
        launch.Opened += (_, _) => { _ = launch.BeginOpen(host, () => recents.Items.Select(i => i.Path).ToList()); };
        desktop.Exit += (_, _) => provider.Dispose();
        launch.Show();
    }
}
