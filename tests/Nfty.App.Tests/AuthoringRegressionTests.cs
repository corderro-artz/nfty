using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.Tests;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class AuthoringRegressionTests
{
    [AvaloniaTheory]
    [InlineData("same-layer")]
    [InlineData("recipe-layer")]
    [InlineData("book-recipe")]
    public async Task Delete_resolves_kind_and_parent_when_domain_ids_collide(string collision)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-delete-identity-");
        try
        {
            var first = Book(Ing(collision == "recipe-layer" ? "other" : "a", 10)).Recipes[0];
            var secondIngredient = Ing("a", 20);
            var second = new LoadedRecipe
            {
                Manifest = new RecipeManifest(collision == "book-recipe" ? "b" : "other", "Other", ["a"], []),
                Ingredients = [secondIngredient],
            };
            var book = new LoadedCookBook
            {
                Manifest = Book().Manifest with { RecipeWeights = new Dictionary<string, double>
                    { [first.Manifest.Id] = 1, [second.Manifest.Id] = 1 } },
                Recipes = [first, second],
            };
            using var session = new CookBookSession();
            string path = Path.Combine(dir.FullName, "book.cbk");
            session.Open(book, path);
            using var explorer = new ExplorerViewModel(book, new FakeNav(), new YesDialogs(), new ImageBridge(),
                (_, _, _, _) => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
                session, new FilePickerService(), (_, _, _) => throw new InvalidOperationException(), new StatusService());
            explorer.IsEditing = true;
            explorer.SelectedNode = collision == "same-layer" ? explorer.Root.Children[1].Children[0] : explorer.Root.Children[1];
            await explorer.DeleteSelectedCommand.ExecuteAsync(null);
            using var saved = CookBookArchive.Read(path);
            Assert.Equal((byte)10, Assert.Single(saved.Recipe(first.Manifest.Id).Ingredients).VariantImages["v0"][0, 0].R);
            if (collision == "same-layer") Assert.Empty(saved.Recipe(second.Manifest.Id).Ingredients);
            else Assert.Single(saved.Recipes);
        }
        finally { dir.Delete(true); }
    }

    static LoadedIngredient Ing(string id, params byte[] pixels) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            pixels.Select((p, n) => new Variant("v" + n, "v" + n, 1)).ToArray()),
        VariantImages = pixels.Select((p, n) => (p, n)).ToDictionary(p => "v" + p.n,
            p => new Image<Rgba32>(2, 2, new Rgba32(p.p, p.p, p.p))),
    };
    static LoadedCookBook Book(params LoadedIngredient[] layers) => new()
    {
        Manifest = new CookBookManifest("b", "Book", new Dimensions(2, 2), new Collection("Book", "", "B"),
            new Dictionary<string, double> { ["r"] = 1 }),
        Recipes = new[] { new LoadedRecipe { Manifest = new RecipeManifest("r", "Recipe", layers.Select(l => l.Manifest.Id).ToArray(), []), Ingredients = layers } },
    };
    static void Refresh(IngredientEditorViewModel editor, LoadedCookBook book) => typeof(IngredientEditorViewModel)
        .GetMethod("RefreshFromBook", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, [book]);

    [AvaloniaFact]
    public async Task Export_uses_the_selected_variant()
    {
        using var book = Book(Ing("a", 10, 230));
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-probe-");
        try
        {
            var path = Path.Combine(dir.FullName, "preview.png");
            using var pane = new IngredientDetailViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
                new ImageBridge(), () => { }, () => true, picker: new Picker(path));
            pane.SelectVariantCommand.Execute("v1");
            await pane.ExportPreviewCommand.ExecuteAsync(null);
            using var image = Nfty.Core.Imaging.ImageFormats.Load(path);
            Assert.Equal("v1", pane.Selected!.Id);
            Assert.Equal((byte)230, image[0, 0].R);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Filtered_rule_delete_addresses_authored_order()
    {
        using var source = Book(Ing("a", 10, 20), Ing("b", 30, 40));
        var exclude = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("a", "v0"), [new RuleTarget("b", "v0")]);
        var require = new IncompatibilityRule(RuleType.Require, new RuleTarget("a", "v1"), [new RuleTarget("b", "v1")]);
        var recipe = new LoadedRecipe { Manifest = source.Recipes[0].Manifest with { Rules = [exclude, require] }, Ingredients = source.Recipes[0].Ingredients };
        var book = new LoadedCookBook { Manifest = source.Manifest, Recipes = [recipe] };
        RecipeManifest? result = null;
        using var pane = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { }, canReorder: true,
            editRules: (edit, _) => { result = edit(recipe.Manifest); return Task.FromResult<LoadedCookBook?>(null); }, dialogs: new YesDialogs());
        pane.RuleFilter = RuleFilter.Require;
        Assert.False(pane.Rules.Single().IsExclude);
        await pane.DeleteRuleCommand.ExecuteAsync(pane.Rules.Single());
        Assert.Same(exclude, result!.Rules.Single());
    }

    [AvaloniaFact]
    public async Task Refresh_preserves_reference_rows_and_composite()
    {
        using var book = Book(Ing("a", 0), Ing("b", 200));
        using var session = new CookBookSession();
        using var editor = new IngredientEditorViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
            new ImageBridge(), new FakeNav(), session, new FakeDialogs(), new FilePickerService());
        await editor.ToggleReferenceCommand.ExecuteAsync(editor.Siblings.Single());
        Assert.NotNull(typeof(IngredientEditorViewModel).GetField("_aboveStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor));
        Refresh(editor, book);
        Assert.Single(editor.Siblings);
        Assert.True(editor.Siblings[0].IsOn);
        Assert.NotNull(typeof(IngredientEditorViewModel).GetField("_aboveStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor));
        Refresh(editor, book);
        Assert.Single(editor.Siblings);
    }

    [AvaloniaFact]
    public async Task Loose_save_releases_current_images_on_editor_disposal()
    {
        long before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-probe-");
        try
        {
            var book = Book(Ing("a", 20));
            using var session = new CookBookSession();
            var editor = new IngredientEditorViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
                new ImageBridge(), new FakeNav(), session, new FakeDialogs(), new FilePickerService(),
                looseSavePath: Path.Combine(dir.FullName, "a.igt"));
            editor.SelectedName = "renamed";
            await editor.SaveCommand.ExecuteAsync(null);
            var current = (LoadedIngredient)typeof(IngredientEditorViewModel).GetField("_ing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            editor.Dispose();
            Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
        }
        finally { dir.Delete(true); }
    }

    sealed class Picker(string path) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(path);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(path);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [AvaloniaFact]
    public async Task An_edit_during_loose_save_remains_dirty()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-probe-");
        try
        {
            var book = Book(Ing("a", 20));
            using var session = new CookBookSession();
            var path = Path.Combine(dir.FullName, "a.igt");
            using var editor = new IngredientEditorViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
                new ImageBridge(), new FakeNav(), session, new FakeDialogs(), new FilePickerService(), looseSavePath: path);
            editor.SelectedName = "snapshot";
            var saving = editor.SaveCommand.ExecuteAsync(null);
            Assert.True(editor.IsSaving);
            editor.SelectedName = "typed during save";
            await saving;
            using var stored = IngredientArchive.Read(path);
            Assert.Equal("snapshot", stored.Manifest.Variants.Single().Name);
            Assert.Equal("typed during save", editor.SelectedName);
            Assert.True(editor.IsDirty);
            await editor.SaveCommand.ExecuteAsync(null);
            using var second = IngredientArchive.Read(path);
            Assert.Equal("typed during save", second.Manifest.Variants.Single().Name);
            Assert.False(editor.IsDirty);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Import_over_existing_id_releases_replaced_images()
    {
        long before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-probe-");
        var old = Ing("a", 10);
        try
        {
            var book = Book(old);
            var path = Path.Combine(dir.FullName, "book.cbk");
            var loose = Path.Combine(dir.FullName, "a.igt");
            using (var incoming = Ing("a", 200)) IngredientArchive.Write(loose, incoming.Manifest, incoming.VariantImages, DepthMaps.None);
            using var session = new CookBookSession();
            session.Open(book, path);
            using var explorer = new ExplorerViewModel(book, new FakeNav(), new FakeDialogs(), new ImageBridge(),
                (_, _, _, _) => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
                session, new Picker(loose), (_, _, _) => throw new InvalidOperationException(), new StatusService());
            explorer.IsEditing = true;
            explorer.SelectedNode = explorer.Root.Children.Single();
            await explorer.ImportCommand.ExecuteAsync(null);
            Assert.Equal((byte)200, session.Current!.Recipes[0].Ingredients[0].VariantImages["v0"][0, 0].R);
            explorer.Dispose();
            session.Close();
            Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
        }
        finally { old.Dispose(); dir.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Undo_after_save_marks_the_editor_dirty()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-probe-");
        try
        {
            var book = Book(Ing("a", 20));
            using var session = new CookBookSession();
            using var editor = new IngredientEditorViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
                new ImageBridge(), new FakeNav(), session, new FakeDialogs(), new FilePickerService(),
                looseSavePath: Path.Combine(dir.FullName, "a.igt"));
            editor.BrushValue = 255;
            editor.ActiveTool = EditorTool.Fill;
            editor.ApplyToolStroke([(0, 0)]);
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.False(editor.IsDirty);
            Assert.Equal((byte)255, editor.ViewerArt[0, 0].R);
            editor.UndoCommand.Execute(null);
            Assert.Equal((byte)20, editor.ViewerArt[0, 0].R);
            Assert.True(editor.IsDirty);
            Assert.True(editor.CanSave);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaFact]
    public void Lock_changes_refresh_the_bound_delete_button()
    {
        using var book = Book(Ing("a", 10, 20));
        using var session = new CookBookSession();
        session.Open(book, "unused.cbk");
        using var explorer = new ExplorerViewModel(book, new FakeNav(), new FakeDialogs(), new ImageBridge(),
            (_, _, _, _) => throw new InvalidOperationException(), _ => throw new InvalidOperationException(),
            session, new FilePickerService(), (_, _, _) => throw new InvalidOperationException(), new StatusService());
        explorer.SelectedNode = explorer.Root.Children[0].Children[0];
        var pane = Assert.IsType<IngredientDetailViewModel>(explorer.CurrentDetail);
        var button = new Avalonia.Controls.Button { Command = pane.DeleteVariantCommand };
        var window = new Avalonia.Controls.Window { Content = button };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyEnabled);
        explorer.IsEditing = true;
        Assert.True(pane.DeleteVariantCommand.CanExecute(null));
        Assert.True(button.IsEffectivelyEnabled);
        explorer.IsEditing = false;
        Assert.False(button.IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Concurrent_recipe_edits_refuse_before_deriving_a_second_snapshot()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-");
        try
        {
            using var session = new CookBookSession();
            var book = Book(Ing("a", 10, 20), Ing("b", 30, 40));
            session.Open(book, Path.Combine(dir.FullName, "book.cbk"));
            var status = new StatusService();
            using var explorer = Explorer(book, session, new FilePickerService(), status);
            explorer.IsEditing = true;
            var rule = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("a", "v0"), [new RuleTarget("b", "v0")]);
            Task<LoadedCookBook?>? refused = null;
            Task<bool>? leave = null;
            bool derived = false;
            using var wrapped = new HookedSession(session);
            using var owner = Explorer(book, wrapped, new FilePickerService(), status);
            owner.IsEditing = true;
            wrapped.OnReplace = () =>
            {
                wrapped.OnReplace = null;
                Assert.True(session.IsWriting);
                Assert.True(owner.IsBusy);
                Assert.Throws<InvalidOperationException>(() => session.Close());
                leave = owner.ConfirmLeaveAsync();
                Assert.False(leave.IsCompleted);
                refused = explorer.EditRulesAsync("r", m => { derived = true; return m; }, "edit");
            };
            Assert.NotNull(await owner.EditRulesAsync("r", m => RuleEdits.Add(m, rule), "edit"));
            Assert.NotNull(refused);
            Assert.Null(await refused);
            Assert.False(derived);
            Assert.Contains("Still saving", status.Last);
            Assert.True(await leave!);
            Assert.False(session.IsWriting);
            using var stored = CookBookArchive.Read(session.SourcePath!);
            Assert.Single(stored.Recipes[0].Manifest.Rules);
        }
        finally { dir.Delete(true); }
    }

    private static ExplorerViewModel Explorer(LoadedCookBook book, ICookBookSession session,
        IFilePickerService picker, IStatusService? status = null) => new(book, new FakeNav(), new FakeDialogs(),
        new ImageBridge(), (_, _, _, _) => throw new InvalidOperationException(),
        _ => throw new InvalidOperationException(), session, picker,
        (_, _, _) => throw new InvalidOperationException(), status ?? new StatusService());

    private sealed class HookedSession(CookBookSession inner) : ICookBookSession
    {
        public Action? OnReplace;
        public bool IsWriting => inner.IsWriting;
        public IDisposable? TryBeginWrite() => inner.TryBeginWrite();
        public Task WaitForWriteAsync() => inner.WaitForWriteAsync();
        public LoadedCookBook? Current => inner.Current;
        public string? SourcePath => inner.SourcePath;
        public event Action? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
        public void Open(LoadedCookBook book, string? sourcePath = null) => inner.Open(book, sourcePath);
        public void Replace(LoadedCookBook book) { inner.Replace(book); OnReplace?.Invoke(); }
        public void Close() => inner.Close();
        public void Dispose() { }
    }

    [AvaloniaFact]
    public async Task In_book_save_keeps_pixel_name_and_structure_edits_through_refresh()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-");
        try
        {
            using var session = new CookBookSession();
            var book = Book(Ing("a", 20));
            session.Open(book, Path.Combine(dir.FullName, "book.cbk"));
            using var hooked = new HookedSession(session);
            using var editor = new IngredientEditorViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
                new ImageBridge(), new FakeNav(), hooked, new FakeDialogs(), new FilePickerService());
            editor.Saved += editor.RefreshFromBook;
            editor.SelectedName = "snapshot";
            hooked.OnReplace = () =>
            {
                hooked.OnReplace = null;
                editor.SelectedName = "later";
                editor.ActiveTool = EditorTool.Fill;
                editor.BrushValue = 255;
                editor.ApplyToolStroke([(0, 0)]);
                editor.AddVariantCommand.Execute(null);
            };
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.True(editor.IsDirty);
            Assert.Equal(2, editor.Variants.Count);
            using (var stored = CookBookArchive.Read(session.SourcePath!))
            {
                Assert.Equal("snapshot", stored.Recipes[0].Ingredients[0].Manifest.Variants.Single().Name);
                Assert.Equal((byte)20, stored.Recipes[0].Ingredients[0].VariantImages["v0"][0, 0].R);
            }
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.False(editor.IsDirty);
            using var second = CookBookArchive.Read(session.SourcePath!);
            var saved = second.Recipes[0].Ingredients[0];
            Assert.Equal("later", saved.Manifest.Variants[0].Name);
            Assert.Equal(2, saved.Manifest.Variants.Count);
            Assert.Equal((byte)255, saved.VariantImages["v0"][0, 0].R);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_save_preserves_original_and_releases_prospective_images(bool loose, bool twice)
    {
        long before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-");
        try
        {
            var book = Book(Ing("a", 20));
            using (var session = new CookBookSession())
            {
                string path = Path.Combine(dir.FullName, "part" + (loose ? ".igt" : ".cbk"));
                if (!loose) session.Open(book, path);
                using (var editor = new IngredientEditorViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
                    new ImageBridge(), new FakeNav(), session, new FakeDialogs(), new FilePickerService(), looseSavePath: loose ? path : null))
                {
                    editor.SelectedName = "first";
                    if (twice) { await editor.SaveCommand.ExecuteAsync(null); File.Delete(path); }
                    Directory.CreateDirectory(path); // atomic replacement cannot replace a directory
                    editor.SelectedName = "unsaved";
                    long allocated = MemoryDiagnostics.TotalUndisposedAllocationCount;
                    await editor.SaveCommand.ExecuteAsync(null);
                    Assert.True(editor.IsDirty);
                    Assert.Equal(allocated, MemoryDiagnostics.TotalUndisposedAllocationCount);
                    Assert.Equal((byte)20, editor.ViewerArt[0, 0].R);
                    Assert.False(session.IsWriting);
                    if (!loose) Assert.Equal(twice ? "first" : "v0", session.Current!.Recipes[0].Ingredients[0].Manifest.Variants[0].Name);
                }
            }
            Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Import_owns_incoming_until_adoption_and_retains_shared_images(bool recipeFormat, bool fail)
    {
        long before = MemoryDiagnostics.TotalUndisposedAllocationCount;
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-");
        try
        {
            var a = Ing("a", 10);
            var shared = a.VariantImages["v0"];
            var alias = new LoadedIngredient { Manifest = a.Manifest with { Id = "alias", Name = "alias" }, VariantImages = a.VariantImages };
            var r = Book(a).Recipes[0];
            var other = new LoadedRecipe { Manifest = new RecipeManifest("other", "other", ["alias"], []), Ingredients = [alias] };
            var book = new LoadedCookBook { Manifest = Book().Manifest with { RecipeWeights = new Dictionary<string, double> { ["r"] = 1, ["other"] = 1 } }, Recipes = [r, other] };
            string dest = Path.Combine(dir.FullName, "book.cbk");
            if (fail) Directory.CreateDirectory(dest);
            string incomingPath = Path.Combine(dir.FullName, recipeFormat ? "r.rcp" : "a.igt");
            using (var incoming = Ing("a", 200))
            {
                if (recipeFormat) RecipeArchive.Write(incomingPath, r.Manifest, [incoming]);
                else IngredientArchive.Write(incomingPath, incoming.Manifest, incoming.VariantImages, DepthMaps.None);
            }
            using (var session = new CookBookSession())
            {
                session.Open(book, dest);
                using var explorer = Explorer(book, session, new Picker(incomingPath));
                explorer.IsEditing = true;
                explorer.SelectedNode = explorer.Root.Children[0];
                long allocated = MemoryDiagnostics.TotalUndisposedAllocationCount;
                await explorer.ImportCommand.ExecuteAsync(null);
                Assert.Same(shared, session.Current!.Recipe("other").Ingredients[0].VariantImages["v0"]);
                Assert.Equal((byte)10, shared[0, 0].R); // replaced branch may share pixels with a survivor
                Assert.Equal(fail ? (byte)10 : (byte)200, session.Current.Recipe("r").Ingredients[0].VariantImages["v0"][0, 0].R);
                Assert.Equal(allocated + (fail ? 0 : 1), MemoryDiagnostics.TotalUndisposedAllocationCount);
            }
            Assert.Equal(before, MemoryDiagnostics.TotalUndisposedAllocationCount);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task History_after_save_is_dirty_on_both_surfaces_and_destinations(bool loose, bool color)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-");
        try
        {
            var ing = Ing("a", 20);
            if (!color) ing = new LoadedIngredient { Manifest = ing.Manifest with { Kind = LayerKind.Dynamic,
                Colorization = new Colorization(ColorModel.Hsv, 12, 4, [new ColorEntry(1, new ColorRange(0, 360, 40, 100), null)]) }, VariantImages = ing.VariantImages };
            var book = Book(ing);
            using var session = new CookBookSession();
            string path = Path.Combine(dir.FullName, loose ? "a.igt" : "book.cbk");
            if (!loose) session.Open(book, path);
            using var editor = new IngredientEditorViewModel(ing, book.Recipes[0], book, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new FilePickerService(), looseSavePath: loose ? path : null);
            editor.ActiveTool = EditorTool.Fill;
            editor.BrushValue = 255;
            editor.ApplyToolStroke([(0, 0)]);
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.False(editor.IsDirty);
            editor.UndoCommand.Execute(null);
            Assert.True(editor.IsDirty);
            Assert.True(editor.CanSave);
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.False(editor.IsDirty);
            editor.RedoCommand.Execute(null);
            Assert.True(editor.IsDirty);
            await editor.SaveCommand.ExecuteAsync(null);
            Assert.False(editor.IsDirty);
            editor.RedoCommand.Execute(null); // generated commands can bypass CanExecute
            Assert.False(editor.IsDirty);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData(RuleFilter.All, false, false)]
    [InlineData(RuleFilter.All, false, true)]
    [InlineData(RuleFilter.All, true, false)]
    [InlineData(RuleFilter.All, true, true)]
    [InlineData(RuleFilter.Require, false, false)]
    [InlineData(RuleFilter.Require, false, true)]
    [InlineData(RuleFilter.Require, true, false)]
    [InlineData(RuleFilter.Require, true, true)]
    [InlineData(RuleFilter.Exclude, false, false)]
    [InlineData(RuleFilter.Exclude, false, true)]
    [InlineData(RuleFilter.Exclude, true, false)]
    [InlineData(RuleFilter.Exclude, true, true)]
    public async Task Filtered_and_sorted_equal_looking_rules_keep_authored_identity(RuleFilter filter, bool descending, bool edit)
    {
        using var source = Book(Ing("a", 10, 20), Ing("b", 30, 40));
        var never = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("a", "v0"), [new RuleTarget("b", "v0")]);
        var first = new IncompatibilityRule(RuleType.Require, new RuleTarget("a", "v1"), [new RuleTarget("b", "v1")]);
        var second = first with { }; // equal text, different authored identity
        var replacement = never with { When = new RuleTarget("a", "v1") };
        var recipe = new LoadedRecipe { Manifest = source.Recipes[0].Manifest with { Rules = [never, first, second] }, Ingredients = source.Recipes[0].Ingredients };
        var book = new LoadedCookBook { Manifest = source.Manifest, Recipes = [recipe] };
        RecipeManifest? result = null;
        var dialogs = new AnswerDialogs(dialog => dialog is RuleDialogViewModel ? replacement : true);
        using var pane = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { }, canReorder: true,
            editRules: (change, _) => { result = change(recipe.Manifest); return Task.FromResult<LoadedCookBook?>(null); }, dialogs: dialogs);
        int index = filter == RuleFilter.Exclude ? 0 : 2;
        var row = pane.Rules[index];
        pane.RuleFilter = filter;
        pane.RuleSort.By("Trigger");
        if (descending) pane.RuleSort.By("Trigger");
        Assert.Contains(row, pane.Rules);
        if (edit) await pane.EditRuleCommand.ExecuteAsync(row);
        else await pane.DeleteRuleCommand.ExecuteAsync(row);
        Assert.NotNull(result);
        if (edit)
        {
            Assert.Same(replacement, result.Rules[index]);
            for (int n = 0; n < recipe.Manifest.Rules.Count; n++)
                if (n != index) Assert.Same(recipe.Manifest.Rules[n], result.Rules[n]);
        }
        else
        {
            var expected = recipe.Manifest.Rules.Where((_, n) => n != index).ToArray();
            Assert.Equal(expected.Length, result.Rules.Count);
            for (int n = 0; n < expected.Length; n++) Assert.Same(expected[n], result.Rules[n]);
        }
    }

    [AvaloniaFact]
    public async Task Export_snapshots_selection_before_the_picker_returns()
    {
        using var book = Book(Ing("a", 10, 230));
        var dir = Directory.CreateTempSubdirectory("nfty-authoring-");
        try
        {
            var picker = new PendingPicker();
            using var pane = new IngredientDetailViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
                new ImageBridge(), () => { }, () => true, picker: picker);
            pane.Sort.By("Variant"); pane.Sort.By("Variant");
            pane.SelectVariantCommand.Execute("v1");
            var exporting = pane.ExportPreviewCommand.ExecuteAsync(null);
            pane.SelectVariantCommand.Execute("v0");
            pane.Dispose();
            book.Dispose(); // the picker outlives its borrowed graph
            string path = Path.Combine(dir.FullName, "preview.png");
            picker.Result.SetResult(path);
            await exporting;
            using var image = Nfty.Core.Imaging.ImageFormats.Load(path);
            Assert.Equal((byte)230, image[0, 0].R);
        }
        finally { dir.Delete(true); }
    }

    private sealed class PendingPicker : IFilePickerService
    {
        public TaskCompletionSource<string?> Result { get; } = new();
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Result.Task;
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Result.Task;
        public Task<string?> PickFolderAsync(string title) => Result.Task;
    }
    private sealed class AnswerDialogs(Func<ViewModelBase, object?> answer) : IDialogService
    {
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) => Task.FromResult((TResult?)answer(dialog));
        public void Close(object? result) { }
    }

    [AvaloniaFact]
    public async Task Refresh_retains_kitchen_gap_choices_and_pixels_without_per_stroke_rebuilds()
    {
        var (book, recipe, ing) = IngredientEditorReferencesTests.FourLayerStack();
        using var ownedBook = book;
        var (kitchen, dir) = IngredientEditorReferencesTests.TempKitchen(("scratch", IngredientEditorReferencesTests.Canvas));
        try
        {
            using var editor = IngredientEditorReferencesTests.Editor(ing, recipe, book, kitchen);
            await editor.ToggleReferenceCommand.ExecuteAsync(editor.Siblings[0]);
            var scratch = editor.KitchenLayers.Single();
            await editor.ToggleReferenceCommand.ExecuteAsync(scratch);
            editor.PlaceDownCommand.Execute(scratch);
            int gap = scratch.Gap;
            var before = Enumerable.Range(0, 16).SelectMany(y => Enumerable.Range(0, 16)
                .Select(x => IngredientEditorReferencesTests.Pixel(editor.Canvas, x, y))).ToArray();
            for (int i = 0; i < 2; i++) editor.RefreshFromBook(book);
            Assert.Equal(3, editor.Siblings.Count);
            Assert.Single(editor.KitchenLayers);
            Assert.True(editor.Siblings[0].IsOn);
            Assert.True(editor.KitchenLayers[0].IsOn);
            Assert.Equal(gap, editor.KitchenLayers[0].Gap);
            Assert.Equal("2 / 4 on", editor.ReferenceCountText);
            var after = Enumerable.Range(0, 16).SelectMany(y => Enumerable.Range(0, 16)
                .Select(x => IngredientEditorReferencesTests.Pixel(editor.Canvas, x, y))).ToArray();
            Assert.Equal(before, after);
            int builds = editor.StackRebuildCount;
            editor.ActiveTool = EditorTool.Fill; editor.BrushValue = 10;
            editor.ApplyToolStroke([(0, 8)]);
            Assert.Equal(builds, editor.StackRebuildCount);
        }
        finally { Directory.Delete(dir, true); }
    }

    [AvaloniaFact]
    public void Colorway_bitmaps_are_lazy_and_hero_order_is_materialized_once()
    {
        using var book = Book(Ing("a", 10, 230));
        var bridge = new CountingBridge();
        using var pane = new IngredientDetailViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
            bridge, () => { }, () => true);
        Assert.Equal(3, bridge.Count); // two variant rows and the hero, no unused colorway render
        Assert.Same(pane.HeroBars, pane.HeroBars);
        Assert.Equal("v0", pane.HeroBars[0].Id); // stable equal-weight tie order
        Assert.Single(pane.Colorways);
        Assert.Equal(4, bridge.Count);
        Assert.Same(pane.Colorways, pane.Colorways);
        Assert.Equal(4, bridge.Count);
    }
    private sealed class CountingBridge : IImageBridge
    {
        public int Count;
        public Avalonia.Media.Imaging.Bitmap ToBitmap(Image<Rgba32> image) { Count++; return new ImageBridge().ToBitmap(image); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rule_dialog_completion_resolves_the_original_after_authored_order_changes(bool edit)
    {
        using var source = Book(Ing("a", 10, 20), Ing("b", 30, 40));
        var rule = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("a", "v0"), [new RuleTarget("b", "v0")]);
        var extra = new IncompatibilityRule(RuleType.Require, new RuleTarget("a", "v1"), [new RuleTarget("b", "v1")]);
        var replacement = extra with { Type = RuleType.Exclude };
        var recipe = new LoadedRecipe { Manifest = source.Recipes[0].Manifest with { Rules = [rule] }, Ingredients = source.Recipes[0].Ingredients };
        var book = new LoadedCookBook { Manifest = source.Manifest, Recipes = [recipe] };
        var live = recipe.Manifest;
        RecipeManifest? result = null;
        var dialogs = new AnswerDialogs(_ => { live = live with { Rules = [extra, rule] }; return edit ? replacement : true; });
        using var pane = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { }, canReorder: true,
            editRules: (change, _) => { result = change(live); return Task.FromResult<LoadedCookBook?>(null); }, dialogs: dialogs);
        if (edit) await pane.EditRuleCommand.ExecuteAsync(pane.Rules[0]);
        else await pane.DeleteRuleCommand.ExecuteAsync(pane.Rules[0]);
        Assert.Same(extra, result!.Rules[0]);
        if (edit) Assert.Same(replacement, result.Rules[1]);
        else Assert.Single(result.Rules);
    }

    [AvaloniaFact]
    public void Session_busy_state_does_not_announce_a_graph_change_and_lease_release_is_idempotent()
    {
        using var session = new CookBookSession();
        int changed = 0;
        session.Changed += () => changed++;
        var lease = session.TryBeginWrite();
        Assert.NotNull(lease);
        Assert.Null(session.TryBeginWrite());
        Assert.True(session.IsWriting);
        Assert.False(session.WaitForWriteAsync().IsCompleted);
        lease.Dispose(); lease.Dispose();
        Assert.False(session.IsWriting);
        Assert.True(session.WaitForWriteAsync().IsCompleted);
        Assert.Equal(0, changed);
    }

    sealed class YesDialogs : IDialogService
    {
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog) => Task.FromResult((TResult?)(object)true);
        public void Close(object? result) { }
    }
}
