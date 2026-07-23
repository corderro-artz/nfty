using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Services;
using Nfty.App.ViewModels;

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
        services.AddSingleton<ICookBookSession, CookBookSession>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<NewCookBookViewModel>();
        services.AddTransient<NewRecipeViewModel>();
        services.AddTransient<NewIngredientViewModel>();
        services.AddTransient<LandingViewModel>();
        services.AddTransient<ExplorerViewModel>();
        services.AddTransient<IngredientEditorViewModel>();

        // Further VM registrations are added incrementally by the task that creates each
        // ViewModel (see Tasks 12-13).

        return services;
    }
}
