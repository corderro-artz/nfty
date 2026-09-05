using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Import brings a loose <c>.rcp</c> or <c>.igt</c> into the open CookBook, and the file on disk
/// says so afterwards.
/// </summary>
/// <remarks>
/// The command existed and said "isn't available yet" — honest, and still a toolbar button that did
/// nothing when pressed. That matters beyond the one button: a Kitchen holds the loose parts
/// precisely so they can be pulled into a project, and a CookBook you cannot import into makes it a
/// place things only ever leave.
/// </remarks>
public class ExplorerImportTests
{
    /// <summary>A picker that answers with a path the test chose, instead of showing a dialog.</summary>
    private sealed class FakePicker(string? answer) : IFilePickerService
    {
        public string? LastTitle { get; private set; }
        public string[] LastExtensions { get; private set; } = [];

        public Task<string?> OpenFileAsync(string title, params string[] extensions)
        {
            LastTitle = title;
            LastExtensions = extensions;
            return Task.FromResult(answer);
        }

        public Task<string?> SaveFileAsync(string title, string defaultExtension)
            => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static LoadedIngredient Ing(string id, params string[] variants) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            variants.Select(v => new Variant(v, v, 1)).ToArray()),
        VariantImages = variants.ToDictionary(v => v, _ => new Image<Rgba32>(8, 8)),
    };

    private static LoadedCookBook MemoryBook() => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" },
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { Ing("bg", "day", "night") },
            },
        },
    };

    private static (string Path, CookBookSession Session) OnDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        using (var seed = MemoryBook())
            CookBookArchive.Write(path, seed.Manifest, seed.Recipes);
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (path, session);
    }

    /// <summary>Writes a loose ingredient beside the book for the picker to answer with.</summary>
    private static string LooseIngredient(string bookPath, string id)
    {
        string p = Path.Combine(Path.GetDirectoryName(bookPath)!, id + ".igt");
        using var ing = Ing(id, "one", "two");
        IngredientArchive.Write(p, ing.Manifest, ing.VariantImages);
        return p;
    }

    private static string LooseRecipe(string bookPath, string id)
    {
        string p = Path.Combine(Path.GetDirectoryName(bookPath)!, id + ".rcp");
        using var ing = Ing("fur", "short", "long");
        var manifest = new RecipeManifest(id, id.ToUpperInvariant(), new[] { "fur" },
            Array.Empty<IncompatibilityRule>());
        RecipeArchive.Write(p, manifest, new[] { ing });
        return p;
    }

    private static ExplorerViewModel Explorer(CookBookSession session, IFilePickerService picker,
                                              IStatusService status)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, picker,
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
    }

    private static void Cleanup(CookBookSession session, string path)
    {
        session.Dispose();
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [AvaloniaFact]
    public async Task An_imported_layer_lands_in_the_selected_recipe_and_reaches_the_archive()
    {
        var (path, session) = OnDisk();
        try
        {
            string loose = LooseIngredient(path, "hat");
            using var explorer = Explorer(session, new FakePicker(loose), new StatusService());
            explorer.ToggleLockCommand.Execute(null);          // structure: the lock governs it
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);   // the Cat recipe

            await explorer.ImportCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            var cat = reread.Recipes.Single(r => r.Manifest.Id == "cat");
            Assert.Contains("hat", cat.Manifest.LayerOrder);
            Assert.Contains(cat.Ingredients, i => i.Manifest.Id == "hat");
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task An_imported_recipe_joins_the_book()
    {
        var (path, session) = OnDisk();
        try
        {
            string loose = LooseRecipe(path, "fox");
            using var explorer = Explorer(session, new FakePicker(loose), new StatusService());
            explorer.ToggleLockCommand.Execute(null);

            await explorer.ImportCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Contains(reread.Recipes, r => r.Manifest.Id == "fox");

            // At the average of its siblings, not at a literal 100: a book weighted 10 and 20 would
            // otherwise gain a newcomer outweighing both together.
            Assert.Equal(100, reread.Manifest.RecipeWeights["fox"]);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>
    /// A layer belongs to one recipe, so importing one with only the book selected has no answer to
    /// guess at — and the app says which choice to make rather than picking for you.
    /// </summary>
    [AvaloniaFact]
    public async Task Importing_a_layer_with_no_recipe_selected_says_what_to_do_and_writes_nothing()
    {
        var (path, session) = OnDisk();
        var status = new StatusService();
        try
        {
            string loose = LooseIngredient(path, "hat");
            using var explorer = Explorer(session, new FakePicker(loose), status);
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root);   // the BOOK, not a recipe

            await explorer.ImportCommand.ExecuteAsync(null);

            Assert.Contains("recipe", status.Last, StringComparison.OrdinalIgnoreCase);
            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes.SelectMany(r => r.Manifest.LayerOrder), l => l == "hat");
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task A_locked_book_refuses_the_import_and_says_so()
    {
        var (path, session) = OnDisk();
        var status = new StatusService();
        try
        {
            string loose = LooseRecipe(path, "fox");
            var picker = new FakePicker(loose);
            using var explorer = Explorer(session, picker, status);   // opens LOCKED

            await explorer.ImportCommand.ExecuteAsync(null);

            Assert.Contains("locked", status.Last, StringComparison.OrdinalIgnoreCase);
            // And it refused BEFORE asking for a file: being made to choose one and then told no is
            // worse than being told no.
            Assert.Null(picker.LastTitle);

            using var reread = CookBookArchive.Read(path);
            Assert.DoesNotContain(reread.Recipes, r => r.Manifest.Id == "fox");
        }
        finally { Cleanup(session, path); }
    }

    [AvaloniaFact]
    public async Task Canceling_the_picker_changes_nothing()
    {
        var (path, session) = OnDisk();
        try
        {
            using var explorer = Explorer(session, new FakePicker(null), new StatusService());
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);

            await explorer.ImportCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Single(reread.Recipes);
            Assert.Single(reread.Recipes[0].Manifest.LayerOrder);
        }
        finally { Cleanup(session, path); }
    }

    /// <summary>The picker is asked for exactly the two kinds a CookBook can absorb.</summary>
    [AvaloniaFact]
    public async Task The_picker_offers_recipes_and_ingredients_only()
    {
        var (path, session) = OnDisk();
        try
        {
            var picker = new FakePicker(null);
            using var explorer = Explorer(session, picker, new StatusService());
            explorer.ToggleLockCommand.Execute(null);

            await explorer.ImportCommand.ExecuteAsync(null);

            Assert.Equal([".rcp", ".igt"], picker.LastExtensions);
        }
        finally { Cleanup(session, path); }
    }
}
