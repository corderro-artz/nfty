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
    /// <summary>The real container, except that recents are pointed at a temp directory — resolving
    /// the default <see cref="RecentsService"/> would read and write the user's actual app-data.</summary>
    private static ServiceProvider Container(string recentsDir)
    {
        var services = new ServiceCollection().AddNftyApp();
        // Last registration wins in Microsoft.Extensions.DependencyInjection, so this overrides the
        // app-data-backed default rather than duplicating it.
        services.AddSingleton<IRecentsService>(new RecentsService(recentsDir));
        return services.BuildServiceProvider();
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
