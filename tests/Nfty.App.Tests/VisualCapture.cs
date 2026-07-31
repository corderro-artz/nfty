using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Renders styled primitives under the themed test app and saves a PNG, so visual parity
/// with the mockups can be checked from a real rendered frame (not imagined from XAML). No-ops in a
/// normal test run; set env var NFTY_CAPTURE=1 (and optionally NFTY_CAPTURE_DIR) to activate.</summary>
public class VisualCapture
{
    private static string? Dir =>
        Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null
            ? null
            : (Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") ?? Path.GetTempPath());

    private static TextBlock Label(string text, string? cls = null)
    {
        var tb = new TextBlock { Text = text, Margin = new Thickness(0, 2) };
        if (cls is not null) tb.Classes.Add(cls);
        return tb;
    }

    private static Button Btn(string content, string cls) =>
        new() { Content = content, Classes = { cls }, Margin = new Thickness(0, 0, 8, 0) };

    private static Button Btn(string content, params string[] classes)
    {
        var btn = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var cls in classes) btn.Classes.Add(cls);
        return btn;
    }

    /// <summary>Mirrors MainWindow.axaml's wordmark TextBlock: "nft" in default foreground, "y" in
    /// AccentTextBrush.</summary>
    private static TextBlock Wordmark(ThemeVariant variant)
    {
        var tb = new TextBlock { Margin = new Thickness(0, 2) };
        tb.Classes.Add("wordmark");
        tb.Inlines = new InlineCollection
        {
            new Run("nft"),
            new Run("y") { Foreground = Res("AccentTextBrush", variant) },
        };
        return tb;
    }

    private static Border KindChip(string cls, string text) => new()
    {
        Classes = { cls },
        Margin = new Thickness(0, 0, 8, 0),
        Child = Label(text, "kind-txt"),
    };

    /// <summary>Mirrors MainWindow's titlebar + status bar chrome (brand tile, wordmark, borderless
    /// window controls, zoom/help controls) so it can be visually captured — MainWindow itself can't
    /// be instantiated headlessly (it's the desktop head's top-level Window). Keep this structurally
    /// in sync with src/Nfty.Desktop/MainWindow.axaml's titlebar/status-bar markup.</summary>
    private static IBrush Res(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var v) ? (IBrush)v! : Brushes.Magenta;

    private static Control ChromeStrip(ThemeVariant variant)
    {
        var radiusSm = Application.Current!.TryGetResource("RadiusSm", variant, out var r)
            ? (CornerRadius)r!
            : new CornerRadius(5);

        var brandTile = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = radiusSm,
            Background = Res("AccentWashBrush", variant),
            Child = new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(2),
                Background = Res("AccentBrush", variant),
                RenderTransform = new RotateTransform(45),
            },
        };

        var titlebar = new Grid
        {
            Height = 46,
            Background = Res("PanelBrush", variant),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 9,
            Children = { brandTile, Wordmark(variant) },
        };
        Grid.SetColumn(brand, 0);
        var winControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Children = { Btn("—", "icon"), Btn("▢", "icon"), Btn("✕", "icon", "danger") },
        };
        Grid.SetColumn(winControls, 2);
        titlebar.Children.Add(brand);
        titlebar.Children.Add(winControls);

        var statusBar = new Grid
        {
            Height = 34,
            Background = Res("BgAltBrush", variant),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        var statusText = Label("Ready · 1,024 assets", "muted");
        statusText.Margin = new Thickness(16, 0);
        statusText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(statusText, 0);
        var zoomGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                Btn("−", "icon"),
                new TextBlock { Text = "100%", Width = 46, TextAlignment = Avalonia.Media.TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Btn("+", "icon"),
                Btn("?", "icon"),
            },
        };
        Grid.SetColumn(zoomGroup, 1);
        statusBar.Children.Add(statusText);
        statusBar.Children.Add(zoomGroup);

        return new StackPanel
        {
            Spacing = 0,
            Children = { titlebar, statusBar },
        };
    }

    private static Control Gallery(ThemeVariant variant) => new Border
    {
        Background = Application.Current!.TryGetResource("BgBrush", variant, out var bg) ? (IBrush)bg! : Brushes.Magenta,
        Padding = new Thickness(20),
        Width = 640,
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                ChromeStrip(variant),
                Label("The quick brown fox — sans body text"),
                Label("nfty", "wordmark"),
                Label("CookBook › Recipe › Ingredient", "crumbs"),
                Label("dna-0x9f3a  ·  mono 0123456789", "mono"),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { Btn("Open CookBook", "tbtn"), Btn("Cook", "accent"), Btn("Ghost", "ghost"), Btn("✕", "icon"), Btn("⚄", "dice") },
                },
                new Border
                {
                    Classes = { "panel" },
                    Padding = new Thickness(14, 12),
                    Margin = new Thickness(0, 6, 0, 0),
                    Child = Label("panel surface — shadow + border + r-md"),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new Border { Classes = { "card" }, Width = 140, Child = Label("card surface") },
                        new Border { Classes = { "tile" }, Width = 120, Height = 44, Child = Label("tile surface") },
                    },
                },
                new Border
                {
                    Classes = { "idchip" },
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = Label("id: aura", "mono"),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        KindChip("kind-dynamic", "dynamic"),
                        KindChip("kind-static", "static"),
                        KindChip("kind-custom", "custom"),
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Margin = new Thickness(0, 6, 0, 0),
                    Children =
                    {
                        new TextBox { Text = "aura", Watermark = "id", Width = 110 },
                        new Slider { Minimum = 0, Maximum = 1, Value = 0.6, Width = 110 },
                        new CheckBox { IsChecked = true, Content = "checked" },
                        new RadioButton { IsChecked = true, Content = "picked" },
                        new NumericUpDown { Value = 12, Width = 110 },
                    },
                },
            },
        },
    };

    [AvaloniaFact]
    public void Capture_style_gallery()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            // A focusable sink at the top takes initial focus so no button shows a focus adorner
            // in the capture (buttons auto-focus on window show otherwise, muddying the comparison).
            var sink = new Button { Width = 0, Height = 0, Opacity = 0 };
            var content = new StackPanel { Children = { sink, Gallery(variant) } };
            var window = new Window
            {
                RequestedThemeVariant = variant,
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            sink.Focus();
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var path = Path.Combine(Dir!, $"gallery-{variant.Key.ToString()!.ToLowerInvariant()}.png");
            frame!.Save(path);
        }
    }

    /// <summary>Renders the real <see cref="Views.ExplorerView"/> bound to an
    /// <see cref="ExplorerViewModel"/> (with an ingredient node selected) so visual parity with
    /// docs/design/mockups/explorer.html can be checked from an actual rendered frame — not
    /// imagined from XAML.</summary>
    [AvaloniaFact]
    public void Capture_explorer()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var vm = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
                new FakeNotYetWired(), new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs));
            var view = new Views.ExplorerView { DataContext = vm };
            var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 900, Height = 560 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // A TreeViewItem starts collapsed (default IsExpanded=false), so the ingredient row
            // isn't realised — and can't show a selection highlight — until its ancestors are
            // expanded. Drive the real Tree control the way a user's clicks would, rather than
            // just setting the ViewModel's SelectedNode (which the tree can't visually reflect
            // for a row it hasn't realised yet).
            var tree = view.FindControl<TreeView>("Tree")!;
            var rootContainer = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(c => ReferenceEquals(c.DataContext, vm.Root));
            rootContainer.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var recipeContainer = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(c => ReferenceEquals(c.DataContext, vm.Root.Children[0]));
            recipeContainer.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            vm.SelectNodeCommand.Execute(vm.Root.Children[0].Children[0]);   // select an ingredient
            Dispatcher.UIThread.RunJobs();

            window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, $"explorer-{variant.Key.ToString()!.ToLowerInvariant()}.png"));
            vm.Dispose();
        }
    }

    /// <summary>Builds a small recipe (+ owning cookbook) with one Exclude and one Require rule, so a
    /// capture of <see cref="Views.RecipeDetailView"/> exercises the rules rail — mirrors
    /// <see cref="RecipeDetailViewModelTests.Rules_expose_operator_and_traits"/>'s fixture.</summary>
    private static (LoadedCookBook book, LoadedRecipe recipe) RecipeWithRules()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "day"),
                new[] { new RuleTarget("aura", "none") }),
            new IncompatibilityRule(RuleType.Require, new RuleTarget("bg", "night"),
                new[] { new RuleTarget("aura", "glow") }),
        };
        LoadedIngredient Ing(string id, params string[] variantIds) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                variantIds.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" }, rules),
            Ingredients = new[] { Ing("bg", "day", "night"), Ing("aura", "none", "glow") },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe);
    }

    /// <summary>Renders the three real Explorer detail-body views (<see cref="Views.CookBookDetailView"/>,
    /// <see cref="Views.RecipeDetailView"/>, <see cref="Views.IngredientDetailView"/>) bound to fixture
    /// VMs, so visual parity with docs/design/mockups/explorer.html's CookBook/Recipe/Ingredient bodies
    /// can be checked from actual rendered frames — not imagined from XAML.</summary>
    [AvaloniaFact]
    public void Capture_detail_bodies()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();

            var cookBook = ExplorerViewModelTests.TwoRecipeBook();
            var cookBookVm = new CookBookDetailViewModel(cookBook, new FakeNotYetWired(), () => { });
            Capture(new Views.CookBookDetailView { DataContext = cookBookVm }, variant, $"cookbook-detail-{key}.png");

            var (ruleBook, ruleRecipe) = RecipeWithRules();
            using (var vm = new RecipeDetailViewModel(ruleRecipe, ruleBook, new ImageBridge(), new FakeNotYetWired(), _ => { }))
            {
                Capture(new Views.RecipeDetailView { DataContext = vm }, variant, $"recipe-detail-{key}.png");
            }

            var ingredientBook = ExplorerViewModelTests.TwoRecipeBook();
            var catRecipe = ingredientBook.Recipes.First(r => r.Manifest.Id == "cat");
            var firstIngredient = catRecipe.Ingredients[0];
            using (var vm = new IngredientDetailViewModel(firstIngredient, catRecipe, ingredientBook, new ImageBridge(),
                new FakeNotYetWired(), () => { }, () => false))
            {
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-detail-{key}.png");
            }
        }
    }

    [AvaloniaFact]
    public void Capture_landing()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var notify = new FakeNotYetWired();
            var vm = new LandingViewModel(nav, dialogs, notify, new FilePickerService(), new RecentsService(),
                new CookBookSession(),
                book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                    ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs)),
                set => new SetBrowserViewModel(set));
            Capture(new Views.LandingView { DataContext = vm }, variant, $"landing-{key}.png");
        }
    }

    /// <summary>Renders the real <see cref="Views.SetBrowserView"/> bound to a
    /// <see cref="SetBrowserViewModel"/> over a cooked Set (an item selected) so visual parity with
    /// the Set-browser design intent can be checked from an actual rendered frame — not imagined
    /// from XAML. Cooks a tiny 6-item set to a temp dir per theme, reads it back, renders, then
    /// disposes the VM and deletes the temp dir.</summary>
    [AvaloniaFact]
    public void Capture_set_browser()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                using var generated = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(6, "seed1"));
                SetWriter.Write(generated, dir, pack: false);
                var loaded = SetReader.Read(dir);
                var vm = new SetBrowserViewModel(loaded);
                vm.SelectedItem = vm.Items[0];
                Capture(new Views.SetBrowserView { DataContext = vm }, variant, $"set-browser-{key}.png");
                vm.Dispose();
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }

    /// <summary>Renders the real <see cref="Views.IngredientEditorView"/> bound to an
    /// <see cref="IngredientEditorViewModel"/> after a Fill stroke, so visual parity of the editor
    /// (filmstrip + tools + painted canvas + colorize rail + preview) can be checked from an actual
    /// rendered frame — and that the canvas reflects a committed paint edit — not imagined from XAML.</summary>
    [AvaloniaFact]
    public void Capture_editor_paint()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var book = ExplorerViewModelTests.TwoRecipeBook();
            var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
            var ing = cat.Ingredients[0];
            var vm = new IngredientEditorViewModel(ing, cat, book, new ImageBridge(), new FakeNav(), new FakeNotYetWired());
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });   // flood the blank value-map so the canvas visibly changes
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-paint-{key}.png");
            vm.Dispose();
        }
    }

    private static void Capture(Control view, ThemeVariant variant, string fileName)
    {
        var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, fileName));
    }
}
