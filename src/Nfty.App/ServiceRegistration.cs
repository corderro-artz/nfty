using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Models;
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
        services.AddSingleton<IStatusService, StatusService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IRecentsService, RecentsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ICookBookSession, CookBookSession>();
        // Singleton: at most one Kitchen is open at a time, which is the mockup's own rule - the
        // workspace chip is "fixed for every item below it".
        services.AddSingleton<IKitchenSession, KitchenSession>();
        services.AddSingleton<IImageBridge, ImageBridge>();
        services.AddSingleton<IFolderRevealer, NoopFolderRevealer>();
        services.AddSingleton<IClipboardService, NoopClipboardService>();

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
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IFilePickerService>()));

        // Loose (.igt) editor: same editor, but with a save-straight-to-.igt path and the synthetic
        // wrapper book it owns. Built directly (not via the cookbook editor factory) so it can pass
        // looseSavePath and the synthetic book.
        services.AddSingleton<Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>(sp =>
            (ing, book, path) =>
            {
                // Recording the recent here, at the composition root, rather than in each caller:
                // every route into a loose editor is an "open" the user expects to find again, but
                // only Landing's remembered to record one - a loose ingredient created from the
                // Explorer opened fine and then vanished from Recents. One rule beats N callers each
                // having to remember, and it keeps IRecentsService out of the Explorer's constructor.
                sp.GetRequiredService<IRecentsService>()
                  .Add(new RecentItem(ing.Manifest.Name, $"loose ingredient · {ing.Manifest.Variants.Count} variants",
                                      path, Loose: true));
                return new IngredientEditorViewModel(ing, book.Recipes[0], book,
                    sp.GetRequiredService<IImageBridge>(), sp.GetRequiredService<INavigationService>(),
                    sp.GetRequiredService<INotYetWired>(), sp.GetRequiredService<ICookBookSession>(),
                    sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<IFilePickerService>(),
                    looseSavePath: path);
            });

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
                sp.GetRequiredService<Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>(),
                sp.GetRequiredService<IStatusService>(),
                sp.GetRequiredService<IKitchenSession>(),
                sp.GetRequiredService<IClipboardService>()));

        services.AddSingleton<Func<LoadedSet, SetBrowserViewModel>>(sp => set => new SetBrowserViewModel(set));

        // Further VM registrations are added incrementally by the task that creates each
        // ViewModel (see Tasks 12-13).

        return services;
    }
}
