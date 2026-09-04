using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Output;

namespace Nfty.App;

/// <summary>Registers every head-agnostic service and ViewModel. A head adds only the services
/// that need a window — the file picker, the folder revealer and the clipboard — on top.</summary>
public static class ServiceRegistration
{
    /// <summary>Registers all Nfty.App services and ViewModels. Extended by later tasks.</summary>
    public static IServiceCollection AddNftyApp(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IStatusService, StatusService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ICookBookSession, CookBookSession>();
        // Singleton: at most one Kitchen is open at a time, which is the mockup's own rule - the
        // workspace chip is "fixed for every item below it".
        services.AddSingleton<IKitchenSession, KitchenSession>();

        // The .nfty store, and the two things that live in it. Built by hand rather than by type so
        // the Kitchen session reaches the store (rule 3 of its discovery order) and so the legacy
        // %APPDATA% recents file is named HERE, at the composition root, and nowhere else - a test
        // that constructs a RecentsService can then never reach the developer's own list.
        services.AddSingleton<IStateStore>(sp => new StateStore(sp.GetRequiredService<IKitchenSession>()));
        services.AddSingleton<IRecentsService>(sp =>
            new RecentsService(sp.GetRequiredService<IStateStore>(), RecentsService.LegacyFile));
        services.AddSingleton<IPaletteService>(sp =>
            new PaletteService(sp.GetRequiredService<IStateStore>()));
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
            (ing, recipe, book) => RememberOnSave(sp, new IngredientEditorViewModel(ing, recipe, book,
                sp.GetRequiredService<IImageBridge>(),
                sp.GetRequiredService<INavigationService>(),
                sp.GetRequiredService<ICookBookSession>(),
                sp.GetRequiredService<IDialogService>(),
                sp.GetRequiredService<IFilePickerService>(),
                // The open workspace, so the editor's reference panel can borrow loose .igt files.
                // Null is a normal state (nothing requires a Kitchen), but the shipped app always has
                // the session registered — a factory that omitted it would leave the whole "From the
                // Kitchen" section permanently empty with nothing saying why.
                looseSavePath: null,
                kitchen: sp.GetRequiredService<IKitchenSession>(),
                // The app-wide saved swatches. The editor defaults to an in-memory palette when this
                // is omitted, which is right for a test and wrong for the app: without it every
                // swatch the author saved would be gone at the next launch.
                palette: sp.GetRequiredService<IPaletteService>())));

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
                    sp.GetRequiredService<ICookBookSession>(),
                    sp.GetRequiredService<IDialogService>(), sp.GetRequiredService<IFilePickerService>(),
                    looseSavePath: path,
                    kitchen: sp.GetRequiredService<IKitchenSession>(),
                    palette: sp.GetRequiredService<IPaletteService>());
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
                sp.GetRequiredService<IImageBridge>(),
                sp.GetRequiredService<Func<LoadedIngredient, LoadedRecipe, LoadedCookBook, IngredientEditorViewModel>>(),
                sp.GetRequiredService<Func<LoadedCookBook, CookDialogViewModel>>(),
                sp.GetRequiredService<ICookBookSession>(),
                sp.GetRequiredService<IFilePickerService>(),
                sp.GetRequiredService<Func<LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>(),
                sp.GetRequiredService<IStatusService>(),
                sp.GetRequiredService<IKitchenSession>(),
                sp.GetRequiredService<IClipboardService>()));

        services.AddSingleton<Func<LoadedSet, SetBrowserViewModel>>(sp => set => new SetBrowserViewModel(
            set, sp.GetRequiredService<IFilePickerService>(), sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IStatusService>()));

        // Further VM registrations are added incrementally by the task that creates each
        // ViewModel (see Tasks 12-13).

        return services;
    }

    /// <summary>
    /// Re-records a CookBook's Recent entry whenever an editor saves into it.
    /// </summary>
    /// <remarks>
    /// The Landing screen's subtitle ("2 recipes · 512x512") is recorded once, when the book is
    /// opened. A save can change it — a color save adds a layer, and a book opened with none showed
    /// "0 recipes" forever after — so the line has to be re-recorded against what is now on disk.
    ///
    /// <para>Here, at the composition root, for the same reason the loose-editor factory records its
    /// own open here: one rule in one place beats N callers each remembering, and it keeps
    /// <c>IRecentsService</c> out of the Explorer's constructor.</para>
    /// </remarks>
    private static IngredientEditorViewModel RememberOnSave(IServiceProvider sp, IngredientEditorViewModel editor)
    {
        var session = sp.GetRequiredService<ICookBookSession>();
        var recents = sp.GetRequiredService<IRecentsService>();
        editor.Saved += book =>
        {
            if (session.SourcePath is { } path)
                recents.Add(new RecentItem(book.Manifest.Name, LandingViewModel.RecentMeta(book), path, false));
        };
        return editor;
    }

}
