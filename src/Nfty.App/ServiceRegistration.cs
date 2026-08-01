using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Output;

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
        services.AddSingleton<IImageBridge, ImageBridge>();
        services.AddSingleton<IFolderRevealer, NoopFolderRevealer>();

        services.AddSingleton<ShellViewModel>();
        services.AddTransient<HelpViewModel>();
        services.AddTransient<NewCookBookViewModel>();
        services.AddTransient<NewRecipeViewModel>();
        services.AddTransient<NewIngredientViewModel>();
        services.AddTransient<LandingViewModel>();

        services.AddSingleton<Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>>(sp =>
            (ing, recipe, book) => new IngredientEditorViewModel(ing, recipe, book,
                sp.GetRequiredService<IImageBridge>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<INotYetWired>(),
                sp.GetRequiredService<ICookBookSession>(),
                sp.GetRequiredService<IDialogService>()));

        // Loose (.igt) editor: same editor, but with a save-straight-to-.igt path and the synthetic
        // wrapper book it owns. Built directly (not via the cookbook editor factory) so it can pass
        // looseSavePath and the synthetic book.
        services.AddSingleton<Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>(sp =>
            (ing, book, path) => new IngredientEditorViewModel(ing, book.Recipes[0], book,
                sp.GetRequiredService<IImageBridge>(), sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<INotYetWired>(), sp.GetRequiredService<ICookBookSession>(),
                sp.GetRequiredService<IDialogService>(), looseSavePath: path));

        services.AddSingleton<Func<LoadedCookBook, CookDialogViewModel>>(sp =>
            book => new CookDialogViewModel(book,
                sp.GetRequiredService<IFilePickerService>(),
                sp.GetRequiredService<IFolderRevealer>(),
                sp.GetRequiredService<IDialogService>()));

        services.AddSingleton<Func<LoadedCookBook, ExplorerViewModel>>(sp =>
            book => new ExplorerViewModel(book,
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<INotYetWired>(),
                sp.GetRequiredService<IImageBridge>(),
                sp.GetRequiredService<Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>>(),
                sp.GetRequiredService<Func<LoadedCookBook, CookDialogViewModel>>(),
                sp.GetRequiredService<ICookBookSession>(),
                sp.GetRequiredService<IFilePickerService>(),
                sp.GetRequiredService<Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>()));

        services.AddSingleton<Func<LoadedSet, SetBrowserViewModel>>(sp => set => new SetBrowserViewModel(set));

        // Further VM registrations are added incrementally by the task that creates each
        // ViewModel (see Tasks 12-13).

        return services;
    }
}
