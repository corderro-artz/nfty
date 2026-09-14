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
/// The Dynamic/Static toggle: it is an edit, it asks before the destructive direction, and what it
/// writes is a colorization of the shape the kind requires.
/// </summary>
/// <remarks>
/// <para>Three faults met in one control, and every one of them shipped. <b>The toggle did not mark
/// the editor dirty</b>, and <c>CanSave</c> is <c>IsDirty &amp;&amp; …</c> — so a session whose only
/// change was the kind left Save dim, and the change could not be saved at all. Every other control
/// on the colorize rail routes through <c>ColorsChanged</c>; this one did not.</para>
///
/// <para><b>What it would have written was invalid.</b> <c>BuildColorization</c> spliced the edited
/// entry into whatever the layer already carried, which is right while the kind is unchanged and
/// wrong the moment it changes: Dynamic to Static produced a static layer with TWO entries, which
/// <c>Validator</c> refuses, and Static to Dynamic produced a layer rolling fifty-fifty between the
/// new range and the color it was supposed to replace.</para>
///
/// <para>And <b>nothing on the editor's save path validated</b>, so the invalid book was written
/// without complaint and only failed later, on a screen nowhere near the edit.</para>
/// </remarks>
public class EditorKindToggleTests
{
    /// <summary>Answers every dialog with one fixed value — the confirm's yes or no.</summary>
    private sealed class Answering(bool answer) : IDialogService
    {
        public int Shown { get; private set; }
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Shown++;
            Active = d;
            return Task.FromResult((TResult?)(object?)answer);
        }
        public void Close(object? result) { }
    }

    /// <summary>A one-layer book on disk at the given kind, with the colorization that kind
    /// requires — a single fixed color for Static, one range for Dynamic.</summary>
    private static (string Path, CookBookSession Session, LoadedRecipe Recipe, LoadedIngredient Ing)
        OnDisk(LayerKind kind)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        Colorization? colorization = kind switch
        {
            LayerKind.Static => new Colorization(ColorModel.Hsv, 12, 4,
                new[] { new ColorEntry(1, null, "hex:2211aa") }),
            LayerKind.Dynamic => new Colorization(ColorModel.Hsv, 12, 4,
                new[] { new ColorEntry(1, new ColorRange(170, 200, 55, 95), null) }),
            _ => null,
        };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", kind, colorization,
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 });
        CookBookArchive.Write(path, manifest, new[] { recipe });

        var book = CookBookArchive.Read(path);
        var session = new CookBookSession();
        session.Open(book, path);
        var r = book.Recipes[0];
        return (path, session, r, r.Ingredients[0]);
    }

    private static IngredientEditorViewModel Editor(
        LoadedRecipe recipe, LoadedIngredient ing, CookBookSession session, IDialogService dialogs) =>
        new(ing, recipe, session.Current!, new ImageBridge(), new FakeNav(), session, dialogs,
            new FilePickerService());

    private static IngredientManifest Reread(string path)
    {
        using var book = CookBookArchive.Read(path);
        return book.Recipes[0].Ingredients[0].Manifest;
    }

    private static void Cleanup(string path, CookBookSession session)
    {
        session.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { }
    }

    /// <summary>The loading guard. The mode change is an edit now, and the constructor makes
    /// one — so without the guard every editor opens offering to save a layer nobody touched.</summary>
    [AvaloniaTheory]
    [InlineData(LayerKind.Dynamic)]
    [InlineData(LayerKind.Static)]
    [InlineData(LayerKind.Custom)]
    public void An_editor_that_has_just_opened_is_not_dirty(LayerKind kind)
    {
        var (path, session, recipe, ing) = OnDisk(kind);
        using var vm = Editor(recipe, ing, session, new Answering(true));
        try
        {
            Assert.False(vm.IsDirty);
            Assert.False(vm.SaveCommand.CanExecute(null));
        }
        finally { Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task Switching_to_dynamic_is_an_edit_that_can_be_saved()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Static);
        using var vm = Editor(recipe, ing, session, new Answering(true));
        try
        {
            vm.SetModeDynamicCommand.Execute(null);

            // The whole defect, in one line: the kind is part of the layer, so changing it has to
            // reach Save.
            Assert.True(vm.IsDirty);
            Assert.True(vm.SaveCommand.CanExecute(null));

            await vm.SaveCommand.ExecuteAsync(null);

            var saved = Reread(path);
            Assert.Equal(LayerKind.Dynamic, saved.Kind);

            // ONE entry, and it is the range. The fixed color that was there is what the range
            // replaced; keeping it made the layer roll between the two.
            var entry = Assert.Single(saved.Colorization!.Entries);
            Assert.NotNull(entry.Range);
            Assert.Null(entry.Fixed);
        }
        finally { Cleanup(path, session); }
    }

    [AvaloniaFact]
    public async Task Switching_to_static_asks_first_and_then_writes_exactly_one_fixed_color()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Dynamic);
        var dialogs = new Answering(true);
        using var vm = Editor(recipe, ing, session, dialogs);
        try
        {
            await vm.SetModeStaticCommand.ExecuteAsync(null);
            Assert.Equal(1, dialogs.Shown);
            Assert.True(vm.IsModeStatic);

            await vm.SaveCommand.ExecuteAsync(null);

            var saved = Reread(path);
            Assert.Equal(LayerKind.Static, saved.Kind);
            var entry = Assert.Single(saved.Colorization!.Entries);
            Assert.NotNull(entry.Fixed);
            Assert.Null(entry.Range);
        }
        finally { Cleanup(path, session); }
    }

    /// <summary>The confirm is a real gate, not a notice: saying no leaves the layer dynamic and
    /// the editor clean.</summary>
    [AvaloniaFact]
    public async Task Declining_the_warning_leaves_the_layer_alone()
    {
        var (path, session, recipe, ing) = OnDisk(LayerKind.Dynamic);
        var dialogs = new Answering(false);
        using var vm = Editor(recipe, ing, session, dialogs);
        try
        {
            await vm.SetModeStaticCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.Shown);
            Assert.True(vm.IsModeDynamic);
            Assert.False(vm.IsDirty);
        }
        finally { Cleanup(path, session); }
    }

    /// <summary>
    /// The book stays valid across the change, in both directions.
    /// </summary>
    /// <remarks>
    /// This is the assertion the shipped code could not pass: a static layer with two entries is
    /// exactly what <c>Validator.CheckKind</c> refuses, and it was being written silently.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(LayerKind.Dynamic, LayerKind.Static)]
    [InlineData(LayerKind.Static, LayerKind.Dynamic)]
    public async Task The_book_still_validates_after_the_kind_changes(LayerKind from, LayerKind to)
    {
        var (path, session, recipe, ing) = OnDisk(from);
        using var vm = Editor(recipe, ing, session, new Answering(true));
        try
        {
            if (to == LayerKind.Static) await vm.SetModeStaticCommand.ExecuteAsync(null);
            else vm.SetModeDynamicCommand.Execute(null);
            await vm.SaveCommand.ExecuteAsync(null);

            using var book = CookBookArchive.Read(path);
            var problems = Validator.Validate(book);
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
            Assert.Equal(to, book.Recipes[0].Ingredients[0].Manifest.Kind);
        }
        finally { Cleanup(path, session); }
    }

    /// <summary>A layer with entries the rail cannot show keeps them — the pass-through the splice
    /// existed for, which only applies while the kind is unchanged.</summary>
    [AvaloniaFact]
    public async Task A_dynamic_layer_keeps_the_extra_entries_the_rail_cannot_show()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 12, 4, new[]
                {
                    new ColorEntry(3, new ColorRange(0, 90, 20, 60), null),
                    new ColorEntry(1, new ColorRange(180, 260, 30, 70), null),
                }),
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        CookBookArchive.Write(path, new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }), new[] { recipe });

        var book = CookBookArchive.Read(path);
        var session = new CookBookSession();
        session.Open(book, path);
        var r = book.Recipes[0];
        using var vm = Editor(r, r.Ingredients[0], session, new Answering(true));
        try
        {
            vm.HueMin = 10;                       // edit the entry the rail is showing
            await vm.SaveCommand.ExecuteAsync(null);

            var saved = Reread(path);
            Assert.Equal(2, saved.Colorization!.Entries.Count);
            Assert.Equal(10, saved.Colorization.Entries[0].Range!.HueMin);
            Assert.Equal(3, saved.Colorization.Entries[0].Weight);          // its own weight, kept
            Assert.Equal(180, saved.Colorization.Entries[1].Range!.HueMin); // the one it cannot show
        }
        finally { Cleanup(path, session); }
    }
}
