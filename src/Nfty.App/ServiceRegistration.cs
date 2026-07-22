using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Services;

namespace Nfty.App;

public static class ServiceRegistration
{
    /// <summary>Registers all Nfty.App services and ViewModels. Extended by later tasks.</summary>
    public static IServiceCollection AddNftyApp(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INotYetWired, NotYetWired>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IRecentsService, RecentsService>();
        services.AddSingleton<IThemeService, ThemeService>();

        // VM registrations (ShellViewModel, LandingViewModel, ExplorerViewModel, etc.) are added
        // incrementally by the task that creates each ViewModel (see Tasks 5-13).

        return services;
    }
}
