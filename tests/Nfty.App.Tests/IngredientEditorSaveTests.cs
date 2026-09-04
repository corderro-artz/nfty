using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorSaveTests
{
    // Build a dynamic (value-map) 1-recipe cookbook on disk, return (path, session opened over it).
    internal static (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) OnDisk() =>
        OnDisk(LayerKind.Dynamic);

    // As above, but lets a test build a CUSTOM (full-colour, un-colorized) fixture instead of the
    // default dynamic (value-map) one. Custom ingredients carry no Colorization (CLAUDE.md: "Colorization
    // must be null" for custom).
    internal static (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) OnDisk(LayerKind kind)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var coloriz = kind == LayerKind.Custom ? null
            : new Colorization(ColorModel.Hsv, 12, 4,
                new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", kind, coloriz,
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, manifest, new[] { recipe });
        var book = CookBookArchive.Read(path);      // fresh graph with real images + hash
        var session = new CookBookSession();
        session.Open(book, path);
        var r = book.Recipes[0];
        return (path, session, r, r.Ingredients[0]);
    }

    [AvaloniaFact]
    public async Task Save_writes_the_painted_value_back_to_the_cbk()
    {
        var (path, session, recipe, ing) = OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new FilePickerService());
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });          // flood the blank value-map to 200
            Assert.True(vm.CanSave);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.IsDirty);
            Assert.False(File.Exists(path + ".tmp"));      // temp cleaned up

            using var reread = CookBookArchive.Read(path);
            var rip = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(200, ValueMap.FromImage(rip.VariantImages["glow"]).GetValue(4, 4));
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void CanSave_is_gated_by_dirty_source_and_kind()
    {
        var (path, session, recipe, ing) = OnDisk();
        try
        {
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                new FakeNav(), session, new FakeDialogs(), new FilePickerService());
            Assert.False(vm.CanSave);                      // clean → disabled
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 50;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.CanSave);                        // dirty dynamic w/ source → enabled
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Back_when_dirty_confirms_before_navigating()
    {
        var (path, session, recipe, ing) = OnDisk();
        try
        {
            var nav = new FakeNav();
            var dialogs = new FakeConfirmingDialogs(confirm: false);   // user cancels the discard
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                nav, session, dialogs, new FilePickerService());
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 10;
            vm.ApplyToolStroke(new[] { (0, 0) });
            await vm.BackCommand.ExecuteAsync(null);
            Assert.True(dialogs.Shown);            // a confirm was shown
            Assert.Equal(0, nav.BackCount);        // cancelled → did not navigate
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Back_when_clean_navigates_without_a_dialog()
    {
        var (path, session, recipe, ing) = OnDisk();
        try
        {
            var nav = new FakeNav();
            var dialogs = new FakeConfirmingDialogs(confirm: true);
            var vm = new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
                nav, session, dialogs, new FilePickerService());
            await vm.BackCommand.ExecuteAsync(null);
            Assert.False(dialogs.Shown);           // clean → no confirm
            Assert.Equal(1, nav.BackCount);        // navigated straight back
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    private sealed class FakeConfirmingDialogs : IDialogService
    {
        private readonly bool _confirm;
        public bool Shown { get; private set; }
        public FakeConfirmingDialogs(bool confirm) => _confirm = confirm;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        { Shown = true; return Task.FromResult((TResult?)(object?)_confirm); }
        public void Close(object? result) { }
    }
}
