using System;
using System.IO;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Coverage for the composition root itself. Every other test builds ViewModels by hand, so anything
/// that lives only in <see cref="ServiceRegistration"/> is otherwise unexercised.
/// </summary>
public class ServiceRegistrationTests
{
    /// <summary>The real container, except that the state store is pinned to a temp directory —
    /// resolving the shipped <see cref="StateStore"/> would discover a home beside the test binary
    /// or in the working directory and leave a <c>.nfty</c> folder in the developer's build output,
    /// and the shipped <see cref="RecentsService"/> would additionally read their real %APPDATA%
    /// list to migrate it.</summary>
    private static ServiceProvider Container(string recentsDir)
    {
        var services = new ServiceCollection().AddNftyApp();
        // Last registration wins in Microsoft.Extensions.DependencyInjection, so these override the
        // discovered defaults rather than duplicating them.
        services.AddSingleton<IStateStore>(StateStore.At(recentsDir));
        services.AddSingleton<IRecentsService>(new RecentsService(recentsDir));
        return services.BuildServiceProvider();
    }

    /// <summary>Everything registered in the composition root has to be resolvable from it — a
    /// service nothing constructs is a service whose constructor nobody has ever run.</summary>
    [AvaloniaFact]
    public void The_store_and_the_two_things_that_live_in_it_all_resolve()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var provider = Container(dir);

            var store = provider.GetRequiredService<IStateStore>();
            Assert.Equal(dir, store.Resolution.Directory);
            Assert.NotNull(provider.GetRequiredService<IRecentsService>());
            Assert.NotNull(provider.GetRequiredService<IPaletteService>());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void Opening_a_loose_ingredient_editor_records_it_as_a_recent()
    {
        var recentsDir = Directory.CreateTempSubdirectory().FullName;
        var (path, session, _, ing) = IngredientEditorSaveTests.OnDisk();   // dynamic 8x8 on disk
        try
        {
            using var provider = Container(recentsDir);
            var recents = provider.GetRequiredService<IRecentsService>();
            Assert.Empty(recents.Items);

            var factory = provider.GetRequiredService<
                Func<Nfty.Core.Formats.LoadedIngredient, LoadedCookBook, string, IngredientEditorViewModel>>();
            var book = LooseWorkspace.WrapIngredient(ing);
            var editor = factory(ing, book, path);

            // Every route into a loose editor must record — the Explorer's create-loose flow used to
            // open one and leave no trace in Recents.
            var recent = Assert.Single(recents.Items);
            Assert.Equal(Path.GetFullPath(path), recent.Path);
            Assert.True(recent.Loose);
            Assert.Contains("loose ingredient", recent.Meta);
            editor.Dispose();
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            Directory.Delete(recentsDir, recursive: true);
        }
    }
}
