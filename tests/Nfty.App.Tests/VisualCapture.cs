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
using Avalonia.Media.Imaging;
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

    private static TextBlock Label(string text, params string[] classes)
    {
        var tb = new TextBlock { Text = text, Margin = new Thickness(0, 2) };
        foreach (var c in classes) tb.Classes.Add(c);
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

    /// <summary>A kind chip as the app actually draws it: `fchip` plus the kind modifier. The gallery
    /// used to specimen a `kind-dynamic`/`kind-static`/`kind-custom` family that Slice 9 replaced —
    /// so the design-system sheet was documenting a vocabulary no screen used, and those styles
    /// survived a dead-class sweep only because the gallery still referenced them.</summary>
    private static Border KindChip(string kindModifier, string text) => new()
    {
        Classes = { "fchip", kindModifier },
        Margin = new Thickness(0, 0, 8, 0),
        Child = Label(text, "kind-txt", kindModifier),
    };

    /// <summary>Theme-resource lookups for the synthetic swatches below; magenta marks a miss.</summary>
    private static IBrush Res(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var v) ? (IBrush)v! : Brushes.Magenta;

    private static Avalonia.Media.Color ResColor(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var v) ? (Avalonia.Media.Color)v! : Colors.Magenta;

    /// <summary>The real application shell — ShellChromeView is the very control MainWindow hosts,
    /// so this frame shows the shipped titlebar and status bar rather than a replica of them. It is
    /// captured with an Explorer as the current page, since the Kitchen chip, crumbs and lock flag
    /// are bound through ShellViewModel.CurrentExplorer and are absent on every other page.</summary>
    [AvaloniaFact]
    public void Capture_shell()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var session = new CookBookSession();
            using var explorer = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
                new FakeNotYetWired(), new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

            var shell = new ShellViewModel(nav, dialogs, new FakeNotYetWired(), new ThemeService(), new StatusService());
            nav.To(explorer);                             // drives ShellViewModel.CurrentPage
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0]);   // a crumb trail to show
            explorer.ToggleLockCommand.Execute(null);                        // unlocked lock flag

            Capture(new Views.ShellChromeView { DataContext = shell }, variant,
                $"shell-{variant.Key.ToString()!.ToLowerInvariant()}.png",
                width: 1416, height: 864);   // MainWindow's real size — 1180x720 scaled by BaseScale
        }
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
                Label("The quick brown fox — sans body text"),
                Label("nfty", "wordmark"),
                Label("CookBook › Recipe › Ingredient", "cseg"),
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
                        new Border { Classes = { "metric" }, Width = 140, Child = Label("metric surface") },
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
                        KindChip("kdyn", "dynamic"),
                        KindChip("kstat", "static"),
                        KindChip("kcust", "custom"),
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Margin = new Thickness(0, 6, 0, 0),
                    Children =
                    {
                        new TextBox { Text = "aura", PlaceholderText = "id", Width = 110 },
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
            frame!.Save(path, PngBitmapEncoderOptions.Default);
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
                ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(),
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService());
            var view = new Views.ExplorerView { DataContext = vm };
            // MainWindow's own default size. The mockup's pane track alone needs 286+392+336 = 1014px, so
            // capturing the Explorer at less than that judges it in a squeeze the app never ships in.
            var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 1180, Height = 720 };
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
            vm.ToggleLockCommand.Execute(null);   // unlocked: the lock must LOOK different
            Dispatcher.UIThread.RunJobs();

            window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, $"explorer-{variant.Key.ToString()!.ToLowerInvariant()}.png"),
                PngBitmapEncoderOptions.Default);
            vm.Dispose();
        }
    }

    /// <summary>Builds a small recipe (+ owning cookbook) with one Exclude and one Require rule, so a
    /// capture of <see cref="Views.RecipeDetailView"/> exercises the rules rail — mirrors
    /// <see cref="RecipeDetailViewModelTests.Rules_expose_operator_and_traits"/>'s fixture.</summary>
    /// <summary>A DYNAMIC ingredient with a real hue range. TwoRecipeBook's layers are all Custom,
    /// so the colorways hue band - which only a dynamic layer has - had no frame proving it renders.</summary>
    private static (LoadedCookBook book, LoadedRecipe recipe, LoadedIngredient ing) DynamicIngredient()
    {
        var colorization = new Colorization(ColorModel.Hsv, HueQuantize: 24, SatQuantize: 6,
            new[] { new ColorEntry(1, new ColorRange(190, 320, 55, 95), null) });
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic, colorization,
                new[] { new Variant("soft", "Soft", 3), new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["soft"] = new Image<Rgba32>(8, 8), ["glow"] = new Image<Rgba32>(8, 8),
            },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe, ing);
    }

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
            // showReports supplied, or the Reports button captures DISABLED and the frame is no
            // evidence that it renders - the same fixture blind spot as the editor's toolstrip.
            var cookBookVm = new CookBookDetailViewModel(cookBook, new FakeNotYetWired(), () => { }, () => { });
            Capture(new Views.CookBookDetailView { DataContext = cookBookVm }, variant, $"cookbook-detail-{key}.png");

            var (ruleBook, ruleRecipe) = RecipeWithRules();
            using (var vm = new RecipeDetailViewModel(ruleRecipe, ruleBook, new ImageBridge(), new FakeNotYetWired(), _ => { }))
            {
                Capture(new Views.RecipeDetailView { DataContext = vm }, variant, $"recipe-detail-{key}.png");
            }

            var ingredientBook = ExplorerViewModelTests.TwoRecipeBook();
            var catRecipe = ingredientBook.Recipes.First(r => r.Manifest.Id == "cat");
            var firstIngredient = catRecipe.Ingredients[0];
            // picker + dialogs supplied so Export preview captures ENABLED.
            using (var vm = new IngredientDetailViewModel(firstIngredient, catRecipe, ingredientBook, new ImageBridge(),
                new FakeNotYetWired(), () => { }, () => false, null, null,
                new FilePickerService(), new FakeDialogs()))
            {
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-detail-{key}.png");
            }

            var (dynBook, dynRecipe, dynIng) = DynamicIngredient();
            using (var vm = new IngredientDetailViewModel(dynIng, dynRecipe, dynBook, new ImageBridge(),
                new FakeNotYetWired(), () => { }, () => false, null, null,
                new FilePickerService(), new FakeDialogs()))
            {
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-detail-dynamic-{key}.png");
            }
            dynBook.Dispose();
        }
    }

    [AvaloniaFact]
    public void Capture_landing()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var (variant, empty) in
                 from v in new[] { ThemeVariant.Light, ThemeVariant.Dark }
                 from e in new[] { false, true }
                 select (v, e))
        {
            var key = variant.Key.ToString()!.ToLowerInvariant() + (empty ? "-empty" : "");
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var notify = new FakeNotYetWired();
            // Two frames per theme: the populated rows AND the first-run empty state. Capturing only
            // the empty one left the .rrow template (icon tile, name/meta stack, path column) with no
            // rendered evidence at all.
            var recents = new RecentsService(Directory.CreateTempSubdirectory().FullName);
            if (!empty)
            {
                recents.Add(new Models.RecentItem("VaporPets", "cookbook · 2 recipes", @"D:\art\VaporPets.cbk", false));
                recents.Add(new Models.RecentItem("aura", "ingredient · 3 variants", @"D:\art\parts\aura.igt", true));
            }
            var vm = new LandingViewModel(nav, dialogs, notify, new FilePickerService(),
                recents,
                new CookBookSession(),
                book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(),
                    ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(),
                    new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService()),
                set => new SetBrowserViewModel(set),
                (_, _, _) => null!);
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
            var vm = new IngredientEditorViewModel(ing, cat, book, new ImageBridge(), new FakeNav(), new FakeNotYetWired(),
                new CookBookSession(), new FakeDialogs(), new FilePickerService());
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });   // flood the blank value-map so the canvas visibly changes
            vm.AddVariantCommand.Execute(null);     // second filmstrip entry + populated name/weight editors
            vm.SelectVariantCommand.Execute(vm.Variants[0]);   // back to the painted variant
            vm.FillPanePreviewCommand.Execute(null);   // C1: preview takes over the canvas pane
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-paint-{key}.png");
            vm.Dispose();

            // TwoRecipeBook's layers are ALL LayerKind.Custom, and the editor gates painting on
            // CanPaint => !IsCustom. So the frames above render the entire toolstrip - tools,
            // undo/redo, value ramp, swatch, brush size - DISABLED, and the editor's whole reason to
            // exist had no visual evidence at all. Same class of blind spot as the colorways hue
            // band before ingredient-detail-dynamic-* existed: a frame that exercises no code is not
            // evidence. This pair is the enabled editor.
            var (dynBook, dynRecipe, dynIng) = DynamicIngredient();
            var dynVm = new IngredientEditorViewModel(dynIng, dynRecipe, dynBook, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), new CookBookSession(), new FakeDialogs(),
                new FilePickerService());
            dynVm.ActiveTool = EditorTool.Fill;
            dynVm.BrushValue = 200;
            dynVm.ApplyToolStroke(new[] { (0, 0) });
            Capture(new Views.IngredientEditorView { DataContext = dynVm }, variant, $"editor-enabled-{key}.png");
            dynVm.Dispose();
            dynBook.Dispose();
        }
    }

    /// <summary>Renders the Help view and the three wizards, so every screen with a locked mockup
    /// has a real frame to audit against (help.html, wizard-cookbook/recipe/ingredient.html).</summary>
    [AvaloniaFact]
    public void Capture_help_and_wizards()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var dialogs = new FakeDialogs();
            var notify = new FakeNotYetWired();

            Capture(new Views.HelpView { DataContext = new HelpViewModel(dialogs) }, variant, $"help-{key}.png");

            Capture(new Views.NewCookBookView { DataContext = new NewCookBookViewModel(dialogs, notify) { Name = "Vapor Pets", Symbol = "VP", Description = "A cosy little collection." } },
                variant, $"wizard-cookbook-{key}.png");

            // Siblings, or the "Resulting mix" panel hides and the frame proves nothing about it -
            // a weight is only meaningful RELATIVE to the recipes it is normalised against.
            var siblings = new[] { ("Fox", 45d), ("Owl", 25d) };
            Capture(new Views.NewRecipeView { DataContext = new NewRecipeViewModel(dialogs, notify, siblings) { Name = "Cat" } },
                variant, $"wizard-recipe-{key}.png");

            Capture(new Views.NewIngredientView { DataContext = new NewIngredientViewModel(dialogs, notify) { Name = "Aura" } },
                variant, $"wizard-ingredient-{key}.png");
        }
    }

    /// <summary>Default 1180x720 is MainWindow's own size in MOCKUP units — the size a page's layout
    /// is authored against. The shell itself renders at ShellViewModel.BaseScale, so a frame of the
    /// shell must be captured at the scaled window size (1416x864) or the same layout arrives in a
    /// window a fifth too small and correct panes look clipped.</summary>
    private static void Capture(Control view, ThemeVariant variant, string fileName,
        double width = 1180, double height = 720)
    {
        var window = new Window { RequestedThemeVariant = variant, Content = view, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, fileName), PngBitmapEncoderOptions.Default);
    }
}
