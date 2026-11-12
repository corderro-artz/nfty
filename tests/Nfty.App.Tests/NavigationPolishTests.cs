using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Back on every page, fullscreen left only by its own control, the rule dialog's eyebrow,
/// and the die on a Dynamic layer.</summary>
public class NavigationPolishTests
{
    private static ShellViewModel Shell(INavigationService nav) =>
        new(nav, new FakeDialogs(), new ThemeService(), new StatusService());

    [AvaloniaFact]
    public async Task Back_goes_up_one_page_and_home_goes_all_the_way()
    {
        var nav = new NavigationService();
        var shell = Shell(nav);
        var landing = new HelpViewModel(new FakeDialogs());
        var middle = new HelpViewModel(new FakeDialogs());
        nav.To(landing); nav.To(middle); nav.To(new HelpViewModel(new FakeDialogs()));

        Assert.True(shell.GoBackCommand.CanExecute(null));
        await shell.GoBackCommand.ExecuteAsync(null);
        Assert.Same(middle, nav.Current);

        nav.To(new HelpViewModel(new FakeDialogs()));
        await shell.CloseDocumentCommand.ExecuteAsync(null);
        Assert.Same(landing, nav.Current);
        Assert.False(shell.GoBackCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Escape_and_stray_state_changes_never_leave_fullscreen_only_the_toggle_does()
    {
        var shell = Shell(new FakeNav());
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            shell.ToggleFullScreenCommand.Execute(null);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            window.WindowState = WindowState.Normal;       // a title-bar drag or double-click
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            window.WindowState = WindowState.Maximized;
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            shell.ToggleFullScreenCommand.Execute(null);
            Assert.NotEqual(WindowState.FullScreen, window.WindowState);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true, "INCOMPATIBILITY")]
    [InlineData(false, "DEPENDENCY")]
    public void The_rule_dialog_eyebrow_names_the_relationship_being_written(bool never, string kicker)
    {
        var (book, recipe) = VisualCapture.RecipeWithRules();
        book.Dispose();
        var vm = new RuleDialogViewModel(new FakeDialogs(), recipe.Manifest, recipe.Ingredients);
        vm.SetRelationCommand.Execute(never ? "exclude" : "require");
        Assert.Equal(kicker, vm.Kicker);
    }

    private static LoadedCookBook BookOf(LayerKind kind)
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", kind,
                kind == LayerKind.Dynamic
                    ? new Colorization(ColorModel.Hsv, 12, 4, [new ColorEntry(1, new ColorRange(0, 360, 40, 100), null)])
                    : new Colorization(ColorModel.Hsv, 12, 4, [new ColorEntry(1, null, "hsv:200,60,80")]),
                [new Variant("glow", "Glow", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(4, 4, new Rgba32(200, 200, 200)) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "Recipe", ["aura"], []),
            Ingredients = [ing],
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("b", "Book", new Dimensions(4, 4), new Collection("Book", "", "B"),
                new Dictionary<string, double> { ["r"] = 1 }),
            Recipes = [recipe],
        };
    }

    [AvaloniaFact]
    public void The_die_rolls_a_new_color_for_a_dynamic_layer_and_never_repeats_a_face()
    {
        using var book = BookOf(LayerKind.Dynamic);
        using var pane = new IngredientDetailViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
            new ImageBridge(), () => { }, () => true);
        var row = pane.Variants.Single();
        var before = (row.Thumbnail, pane.Hero, pane.DieFace);

        for (int i = 0; i < 20; i++)
        {
            int face = pane.DieFace;
            pane.RollColorCommand.Execute(null);
            Assert.NotEqual(face, pane.DieFace);
        }
        Assert.NotSame(before.Thumbnail, row.Thumbnail);
        Assert.NotSame(before.Hero, pane.Hero);
    }

    [AvaloniaFact]
    public void A_static_layer_has_one_color_so_the_die_changes_nothing()
    {
        using var book = BookOf(LayerKind.Static);
        using var pane = new IngredientDetailViewModel(book.Recipes[0].Ingredients[0], book.Recipes[0], book,
            new ImageBridge(), () => { }, () => true);
        var thumb = pane.Variants.Single().Thumbnail;
        pane.RollColorCommand.Execute(null);
        Assert.False(pane.IsDynamic);
        Assert.Same(thumb, pane.Variants.Single().Thumbnail);
    }
}
