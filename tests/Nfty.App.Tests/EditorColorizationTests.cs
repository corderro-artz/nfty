using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The colorize rail is part of the layer, not a preview toy.
/// </summary>
/// <remarks>
/// Found by opening the shipped app on a layer configured for hue 170-200 and saturation 60-90: the
/// rail showed 0-360 and 40-100 — its field defaults — so it misreported the layer, rendered the
/// live preview from the wrong range, and discarded anything the author changed there. Both halves
/// are covered here: what it LOADS, and what Save WRITES.
/// </remarks>
public class EditorColorizationTests
{
    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    /// <summary>A book whose one layer carries a specific, non-default colorization.</summary>
    private static (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing)
        OnDisk(Colorization coloriz, LayerKind kind = LayerKind.Dynamic)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", kind, coloriz,
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
        return (path, session, r, r.Ingredients[0]);
    }

    private static Colorization Ranged(double hMin, double hMax, double sMin, double sMax,
        int hq = 7, int sq = 3) =>
        new(ColorModel.Hsv, hq, sq, new[] { new ColorEntry(1, new ColorRange(hMin, hMax, sMin, sMax), null) });

    private static IngredientEditorViewModel Editor(
        (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) f) =>
        new(f.ing, f.recipe, f.session.Current!, new ImageBridge(), new FakeNav(),
            f.session, new FakeDialogs(), new NoPicker());

    [AvaloniaFact]
    public void The_rail_opens_showing_the_layers_own_configuration()
    {
        var f = OnDisk(Ranged(170, 200, 60, 90));
        try
        {
            using var vm = Editor(f);

            Assert.Equal(170, vm.HueMin);
            Assert.Equal(200, vm.HueMax);
            Assert.Equal(60, vm.SatMin);
            Assert.Equal(90, vm.SatMax);
            Assert.Equal(7, vm.HueQuantize);
            Assert.Equal(3, vm.SatQuantize);

            // Reading a layer is not editing it.
            Assert.False(vm.IsDirty);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public void A_static_layer_opens_showing_its_fixed_color()
    {
        var coloriz = new Colorization(ColorModel.Hsv, 1, 1,
            new[] { new ColorEntry(1, null, "hex:1188cc") });
        var f = OnDisk(coloriz, LayerKind.Static);
        try
        {
            using var vm = Editor(f);

            Assert.Equal("hex:1188cc", vm.FixedColor);
            Assert.False(vm.IsDirty);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public async Task What_the_rail_shows_is_what_Save_writes()
    {
        var f = OnDisk(Ranged(170, 200, 60, 90));
        try
        {
            using (var vm = Editor(f))
            {
                vm.HueMin = 10; vm.HueMax = 40;
                vm.SatMin = 20; vm.SatMax = 55;
                vm.HueQuantize = 9; vm.SatQuantize = 2;

                Assert.True(vm.IsDirty);   // the rail is part of the layer, so touching it dirties it
                await vm.SaveCommand.ExecuteAsync(null);
            }

            using var book = CookBookArchive.Read(f.path);
            var c = book.Recipes[0].Ingredients[0].Manifest.Colorization;
            Assert.NotNull(c);
            Assert.Equal(9, c!.HueQuantize);
            Assert.Equal(2, c.SatQuantize);
            var range = Assert.Single(c.Entries).Range;
            Assert.NotNull(range);
            Assert.Equal(10, range!.HueMin);
            Assert.Equal(40, range.HueMax);
            Assert.Equal(20, range.SatMin);
            Assert.Equal(55, range.SatMax);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>The rail edits ONE entry because that is all it can show. A hand-authored layer with
    /// several weighted entries must come back with the rest of them intact.</summary>
    [AvaloniaFact]
    public async Task Entries_the_rail_cannot_show_are_passed_through_untouched()
    {
        var coloriz = new Colorization(ColorModel.Hsv, 12, 4, new[]
        {
            new ColorEntry(3, new ColorRange(170, 200, 60, 90), null),
            new ColorEntry(1, null, "hex:ff0000"),
        });
        var f = OnDisk(coloriz);
        try
        {
            using (var vm = Editor(f))
            {
                vm.HueMin = 5; vm.HueMax = 15;
                await vm.SaveCommand.ExecuteAsync(null);
            }

            using var book = CookBookArchive.Read(f.path);
            var c = book.Recipes[0].Ingredients[0].Manifest.Colorization!;
            Assert.Equal(2, c.Entries.Count);

            Assert.Equal(5, c.Entries[0].Range!.HueMin);
            Assert.Equal(15, c.Entries[0].Range!.HueMax);
            Assert.Equal(3, c.Entries[0].Weight);        // the entry's own weight survives the edit

            Assert.Equal("hex:ff0000", c.Entries[1].Fixed);   // the entry the rail never showed
            Assert.Equal(1, c.Entries[1].Weight);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>Saving without touching the rail must leave the colorization exactly as it was —
    /// a round trip, not a re-derivation that quietly normalizes something.</summary>
    [AvaloniaFact]
    public async Task Saving_an_untouched_rail_writes_the_same_colorization_back()
    {
        var f = OnDisk(Ranged(170, 200, 60, 90));
        try
        {
            using (var vm = Editor(f))
            {
                vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
                vm.ApplyToolStroke(new[] { (0, 0) });     // dirty via PAINT, not via the rail
                await vm.SaveCommand.ExecuteAsync(null);
            }

            using var book = CookBookArchive.Read(f.path);
            var c = book.Recipes[0].Ingredients[0].Manifest.Colorization!;
            var range = Assert.Single(c.Entries).Range!;
            Assert.Equal(170, range.HueMin);
            Assert.Equal(200, range.HueMax);
            Assert.Equal(60, range.SatMin);
            Assert.Equal(90, range.SatMax);
            Assert.Equal(7, c.HueQuantize);
            Assert.Equal(3, c.SatQuantize);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>Switching the tray to Static writes a fixed-color layer, kind and all.</summary>
    [AvaloniaFact]
    public async Task Switching_to_static_writes_a_static_layer_with_its_fixed_color()
    {
        var f = OnDisk(Ranged(170, 200, 60, 90));
        try
        {
            using (var vm = Editor(f))
            {
                vm.SetModeStaticCommand.Execute(null);
                vm.FixedColor = "hex:22aa44";
                await vm.SaveCommand.ExecuteAsync(null);
            }

            using var book = CookBookArchive.Read(f.path);
            var ing = book.Recipes[0].Ingredients[0];
            Assert.Equal(LayerKind.Static, ing.Manifest.Kind);
            Assert.Equal("hex:22aa44", ing.Manifest.Colorization!.Entries.First(e => e.Fixed is not null).Fixed);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }
}
