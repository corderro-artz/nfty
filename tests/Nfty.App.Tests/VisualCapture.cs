using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
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
    private static string? Dir
    {
        get
        {
            if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null) return null;
            var dir = Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") ?? Path.GetTempPath();
            // Created here rather than assumed: pointing NFTY_CAPTURE_DIR at a path that does not
            // exist yet failed every capture with DirectoryNotFoundException, which reads like a
            // broken harness rather than a missing folder.
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

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
                new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

            var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService());
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
    /// docs/design/archive/mockups/explorer.html can be checked from an actual rendered frame — not
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
                new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
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
    internal static (LoadedCookBook book, LoadedRecipe recipe, LoadedIngredient ing) DynamicIngredient()
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

    internal static (LoadedCookBook book, LoadedRecipe recipe) RecipeWithRules()
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
    /// VMs, so visual parity with docs/design/archive/mockups/explorer.html's CookBook/Recipe/Ingredient bodies
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
            var cookBookVm = new CookBookDetailViewModel(cookBook, () => { }, () => { });
            Capture(new Views.CookBookDetailView { DataContext = cookBookVm }, variant, $"cookbook-detail-{key}.png");

            var (ruleBook, ruleRecipe) = RecipeWithRules();
            using (var vm = new RecipeDetailViewModel(ruleRecipe, ruleBook, new ImageBridge(), _ => { }))
            {
                Capture(new Views.RecipeDetailView { DataContext = vm }, variant, $"recipe-detail-{key}.png");
            }

            var ingredientBook = ExplorerViewModelTests.TwoRecipeBook();
            var catRecipe = ingredientBook.Recipes.First(r => r.Manifest.Id == "cat");
            var firstIngredient = catRecipe.Ingredients[0];
            // picker + dialogs supplied so Export preview captures ENABLED.
            using (var vm = new IngredientDetailViewModel(firstIngredient, catRecipe, ingredientBook, new ImageBridge(),
                () => { }, () => false, null, null,
                new FilePickerService(), new FakeDialogs()))
            {
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-detail-{key}.png");
            }

            var (dynBook, dynRecipe, dynIng) = DynamicIngredient();
            using (var vm = new IngredientDetailViewModel(dynIng, dynRecipe, dynBook, new ImageBridge(),
                () => { }, () => false, null, null,
                new FilePickerService(), new FakeDialogs()))
            {
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-detail-dynamic-{key}.png");
            }
            dynBook.Dispose();
        }
    }

    /// <summary>A four-layer recipe carrying all three kinds, mirroring the table the reorder
    /// exploration draws (docs/design/archive/mockups/explorations/reorder-control-variants.html), so the
    /// frames below can be compared against the variant they implement rather than against nothing.</summary>
    private static (LoadedCookBook Book, LoadedRecipe Recipe) FourLayerRecipe()
    {
        LoadedIngredient Ing(string id, string name, LayerKind kind, int variants)
        {
            var colorization = kind == LayerKind.Custom ? null
                : new Colorization(ColorModel.Hsv, 12, 4,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
            return new LoadedIngredient
            {
                Manifest = new IngredientManifest(id, name, kind, colorization,
                    Enumerable.Range(0, variants).Select(i => new Variant($"v{i}", $"V{i}", 1)).ToArray()),
                VariantImages = Enumerable.Range(0, variants)
                    .ToDictionary(i => $"v{i}", _ => new Image<Rgba32>(8, 8)),
            };
        }
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Aurora",
                new[] { "body", "ears", "eyes", "accessory" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[]
            {
                Ing("body", "Body", LayerKind.Dynamic, 4),
                Ing("ears", "Ears", LayerKind.Dynamic, 3),
                Ing("eyes", "Eyes", LayerKind.Static, 5),
                Ing("accessory", "Accessory", LayerKind.Custom, 6),
            },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return (book, recipe);
    }

    private static RecipeDetailViewModel ReorderablePane(bool canReorder)
    {
        var (book, recipe) = FourLayerRecipe();
        var current = book;
        Task<LoadedCookBook?> Move(string ingredientId, int depth)
        {
            current = Nfty.Core.Editing.CookBookEdits.MoveLayer(current, "cat", ingredientId, depth);
            return Task.FromResult<LoadedCookBook?>(current);
        }
        return new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { },
            Move, canReorder);
    }

    /// <summary>
    /// The Layers table's reorder affordance, in the three states that matter and both themes.
    ///
    /// <para>The pair to compare is <c>locked</c> against <c>unlocked</c>: the spec's hard rule is
    /// that the table is the SAME width in both and that no column shifts, so anything that moves
    /// between those two frames is the defect. The exploration draws variant B <i>prepending</i> its
    /// grip column on unlock, which is exactly the reflow being guarded against — the grip column
    /// here is in the layout of both frames and only its ink changes.</para>
    ///
    /// <para>The third frame is a drag in flight, driven through real simulated pointer input, so the
    /// accent drop line has a rendered frame of its own. That is only possible because the gesture is
    /// pointer-capture rather than <c>DragDrop.DoDragDropAsync</c>, whose platform drag source the
    /// headless harness does not provide.</para></summary>
    [AvaloniaFact]
    public void Capture_layer_reorder()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();

            using (var locked = ReorderablePane(canReorder: false))
                Capture(new Views.RecipeDetailView { DataContext = locked }, variant,
                    $"recipe-reorder-locked-{key}.png");

            using (var unlocked = ReorderablePane(canReorder: true))
                Capture(new Views.RecipeDetailView { DataContext = unlocked }, variant,
                    $"recipe-reorder-unlocked-{key}.png");

            using (var dragging = ReorderablePane(canReorder: true))
            {
                var view = new Views.RecipeDetailView { DataContext = dragging };
                var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 1180, Height = 720 };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var grips = view.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Classes.Contains("grip")).ToList();
                Avalonia.Point Center(Visual c) =>
                    c.TranslatePoint(new Avalonia.Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)
                    ?? default;

                // Drag the bottom layer up over the second row: press, then hold above its midpoint.
                window.MouseDown(Center(grips[3]), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                window.MouseMove(Center(grips[1]) - new Avalonia.Point(0, grips[1].Bounds.Height / 2 + 2));
                Dispatcher.UIThread.RunJobs();

                window.CaptureRenderedFrame()!.Save(
                    Path.Combine(Dir!, $"recipe-reorder-dragging-{key}.png"), PngBitmapEncoderOptions.Default);
                window.MouseUp(Center(grips[1]), MouseButton.Left);
                window.Close();
            }
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
            // Two frames per theme: the populated rows AND the first-run empty state. Capturing only
            // the empty one left the .rrow template (icon tile, name/meta stack, path column) with no
            // rendered evidence at all.
            var recents = new RecentsService(Directory.CreateTempSubdirectory().FullName);
            if (!empty)
            {
                recents.Add(new Models.RecentItem("VaporPets", "cookbook · 2 recipes", @"D:\art\VaporPets.cbk", false));
                recents.Add(new Models.RecentItem("aura", "ingredient · 3 variants", @"D:\art\parts\aura.igt", true));
            }
            var vm = new LandingViewModel(nav, dialogs, new FilePickerService(),
                recents,
                new CookBookSession(),
                book => new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                    ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(),
                    new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService()),
                set => new SetBrowserViewModel(set),
                (_, _, _) => null!,
                // A real KitchenSession, because the shipped container always registers one and
                // NewKitchenCommand.CanExecute is `_kitchen is not null`. Omitting it left the frame
                // showing a DISABLED New Kitchen button at 0.38 opacity — a state the app cannot
                // actually be in, which is worse than not capturing the screen at all: it made a
                // working action look unavailable and matched the bug that was being investigated.
                new KitchenSession());
            Capture(new Views.LandingView { DataContext = vm }, variant, $"landing-{key}.png");

            // The Kitchen shelf's THIRD state. The two frames above both show it with no workspace
            // open, which is the state that needs the least looking at; the one that carries the
            // design — a row of cards, the pager, the kind heading — had no rendered evidence at all.
            if (!empty)
            {
                var (dir, ktn) = ShelfWorkspace();
                try
                {
                    var kitchen = new KitchenSession();
                    kitchen.Open(ktn);
                    var full = new LandingViewModel(nav, dialogs, new FilePickerService(),
                        recents, new CookBookSession(),
                        book => null!, set => null!, (_, _, _) => null!, kitchen);
                    Capture(new Views.LandingView { DataContext = full }, variant, $"landing-kitchen-{key}.png");
                }
                finally { Directory.Delete(dir, true); }
            }
        }
    }

    /// <summary>A throwaway workspace with enough in it to page: seven CookBooks and three loose
    /// parts, so the shelf shows a full row, a page count above one, and both tile treatments.</summary>
    private static (string dir, string ktn) ShelfWorkspace()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        for (int i = 1; i <= 7; i++)
        {
            using var ing = new LoadedIngredient
            {
                Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                    new[] { new Variant("v1", "V1", 1) }),
                VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["v1"] = new(8, 8, new Rgba32(1, 2, 3, 255)) },
            };
            var recipe = new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
                Ingredients = new[] { ing },
            };
            CookBookArchive.Write(Path.Combine(dir, $"Book{i}.cbk"),
                new CookBookManifest("cb", $"Collection {i}", new Dimensions(512, 512),
                    new Collection($"Collection {i}", "", "C"),
                    new Dictionary<string, double> { ["cat"] = 100 }), new[] { recipe });
        }

        using (var loose = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 12, 4,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
                new[] { new Variant("v1", "Glow", 1), new Variant("v2", "Spark", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            { ["v1"] = new(8, 8), ["v2"] = new(8, 8) },
        })
        {
            IngredientArchive.Write(Path.Combine(dir, "aura.igt"), loose.Manifest, loose.VariantImages);
        }

        var ktn = Path.Combine(dir, "Studio.ktn");
        Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));
        return (dir, ktn);
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
            var vm = new IngredientEditorViewModel(ing, cat, book, new ImageBridge(), new FakeNav(),
                new CookBookSession(), new FakeDialogs(), new FilePickerService());
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });   // flood the blank value-map so the canvas visibly changes
            vm.AddVariantCommand.Execute(null);     // second filmstrip entry + populated name/weight editors
            vm.SelectVariantCommand.Execute(vm.Variants[0]);   // back to the painted variant
            vm.FillPanePreviewCommand.Execute(null);   // C1: preview takes over the canvas pane
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-paint-{key}.png");
            vm.Dispose();

            // TwoRecipeBook's layers are ALL LayerKind.Custom. Color mode made those paintable, so
            // the frames above now show a live toolstrip and the palette strip's CUSTOM state: no
            // gray/color tray (a Custom layer has no gray mode), the rainbow ramp, and the rail's
            // paint-hue/saturation axes in place of a colorization it does not have.
            //
            // This pair is the same screen on a value-map layer: the gray/color tray, the gray ramp
            // and the Dynamic colorize rail.
            var (dynBook, dynRecipe, dynIng) = DynamicIngredient();
            var dynVm = new IngredientEditorViewModel(dynIng, dynRecipe, dynBook, new ImageBridge(),
                new FakeNav(), new CookBookSession(), new FakeDialogs(),
                new FilePickerService());
            dynVm.ActiveTool = EditorTool.Fill;
            dynVm.BrushValue = 200;
            dynVm.ApplyToolStroke(new[] { (0, 0) });
            Capture(new Views.IngredientEditorView { DataContext = dynVm }, variant, $"editor-enabled-{key}.png");
            dynVm.Dispose();
            dynBook.Dispose();

            // The palette strip's remaining states, none of which the two frames above reach: a
            // value-map layer switched INTO color (so both halves of the tray are live and the ramp
            // is the rainbow one), saved swatches actually present, and the opacity lock OFF — the
            // one state in which the alpha axis is not dimmed. Set directly rather than through
            // ToggleOpacityLock: the capture is of the unlocked strip, not of the warning dialog.
            var (colBook, colRecipe, colIng) = DynamicIngredient();
            var palette = new PaletteService(StateStore.InMemory());
            palette.Add(new RgbColor(0x6D, 0x4F, 0x9C));
            palette.Add(new RgbColor(0x3D, 0x6B, 0x52));
            var colVm = new IngredientEditorViewModel(colIng, colRecipe, colBook, new ImageBridge(),
                new FakeNav(), new CookBookSession(), new FakeDialogs(),
                new FilePickerService(), palette: palette);
            colVm.SetPaintColorCommand.Execute(null);
            colVm.OpacityMode = OpacityLock.Unlocked;
            colVm.BrushAlpha = 190;
            colVm.PickSwatchCommand.Execute(colVm.Ramp[4]);
            colVm.ActiveTool = EditorTool.Fill;
            colVm.ApplyToolStroke(new[] { (0, 0) });
            Capture(new Views.IngredientEditorView { DataContext = colVm }, variant, $"editor-color-{key}.png");
            colVm.Dispose();
            colBook.Dispose();
        }
    }

    /// <summary>
    /// The reference-layer panel in the Ingredient Editor's colorize rail, in the four states that
    /// matter and both themes.
    ///
    /// <para>The pair to compare is <c>off</c> against <c>split</c>: the spec's hard rule is that a row
    /// is the SAME HEIGHT active and inactive and the panel the same width in every state, so anything
    /// that moves between those two frames is the defect. Variant B grows its rows AS DRAWN — the
    /// over/under tag and the placement stepper appear — which is exactly the reflow being guarded
    /// against, and every one of those cells is in the layout of both frames here with only its ink
    /// changing.</para>
    ///
    /// <para>The <c>ghost</c>/<c>true</c> pair is the other half: at full opacity an above-reference can
    /// hide the art being painted outright, which is why ghosting is the default rather than polish.</para>
    /// </summary>
    [AvaloniaFact]
    public void Capture_editor_references()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var (kitchen, kitchenDir) = IngredientEditorReferencesTests.TempKitchen(
                ("shades", IngredientEditorReferencesTests.Canvas),
                ("visor", IngredientEditorReferencesTests.Canvas));
            var (book, recipe, eyes) = IngredientEditorReferencesTests.FourLayerStack();
            try
            {
                using var vm = IngredientEditorReferencesTests.Editor(eyes, recipe, book, kitchen);

                CaptureReferences(vm, variant, $"editor-refs-off-{key}.png");

                foreach (var id in new[] { "body", "ears", "accessory" })
                    vm.ToggleReferenceCommand.Execute(vm.Siblings.First(s => s.Key == id));
                CaptureReferences(vm, variant, $"editor-refs-split-{key}.png");

                var loose = vm.KitchenLayers[0];
                vm.ToggleReferenceCommand.Execute(loose);
                vm.PlaceDownCommand.Execute(loose);   // gap 3: directly over the layer being edited
                CaptureReferences(vm, variant, $"editor-refs-kitchen-{key}.png");

                vm.ShowTrueColorCommand.Execute(null);
                CaptureReferences(vm, variant, $"editor-refs-true-{key}.png");
            }
            finally { book.Dispose(); Directory.Delete(kitchenDir, recursive: true); }
        }
    }

    /// <summary>Captures the editor with its colorize rail scrolled to the reference panel — which sits
    /// beneath the whole Colorize block and is otherwise below the fold at the mockups' own 720px.</summary>
    private static void CaptureReferences(IngredientEditorViewModel vm, ThemeVariant variant, string fileName)
    {
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        view.FindControl<ScrollViewer>("ColorizeScroll")!.ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, fileName), PngBitmapEncoderOptions.Default);
        window.Close();
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

            Capture(new Views.HelpView { DataContext = new HelpViewModel(dialogs) }, variant, $"help-{key}.png");

            Capture(new Views.NewCookBookView { DataContext = new NewCookBookViewModel(dialogs) { Name = "Vapor Pets", Symbol = "VP", Description = "A cosy little collection." } },
                variant, $"wizard-cookbook-{key}.png");

            // Siblings, or the "Resulting mix" panel hides and the frame proves nothing about it -
            // a weight is only meaningful RELATIVE to the recipes it is normalized against.
            var siblings = new[] { ("Fox", 45d), ("Owl", 25d) };
            Capture(new Views.NewRecipeView { DataContext = new NewRecipeViewModel(dialogs, siblings) { Name = "Cat" } },
                variant, $"wizard-recipe-{key}.png");

            Capture(new Views.NewIngredientView { DataContext = new NewIngredientViewModel(dialogs) { Name = "Aura" } },
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

    /// <summary>
    /// The states no fixture had ever built. This project's worst defect — an unresolved
    /// <c>WarningBrush</c> leaving "1 problem" as black text at 1.14:1 — survived every audit for
    /// exactly one reason: every fixture used a VALID book, so the branch that renders it was never
    /// drawn. A screen that is never captured cannot look wrong.
    ///
    /// <para>The four dialogs are here for the same reason, and a zero-result search because an
    /// empty list is the state most likely to render as nothing at all and be mistaken for correct.</para>
    /// </summary>
    [AvaloniaFact]
    public void Capture_states_no_other_fixture_reaches()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var dialogs = new FakeDialogs();

            // A CookBook Validator rejects: a layerOrder entry naming no ingredient. The identity
            // card must show its problem count, and Cook Set must be visibly disabled.
            using (var broken = InvalidBook())
            {
                var vm = new CookBookDetailViewModel(broken, () => { }, () => { });
                Capture(new Views.CookBookDetailView { DataContext = vm }, variant,
                    $"zz-cookbook-detail-invalid-{key}.png");
            }

            Capture(new Views.HelpView { DataContext = new HelpViewModel(dialogs) }, variant,
                $"zz-help-sheet-{key}.png");

            Capture(new Views.ErrorDialogView
            { DataContext = new ErrorDialogViewModel(dialogs, "Could not open", "The file is not a readable CookBook.") },
                variant, $"zz-dialog-error-{key}.png");

            Capture(new Views.ConfirmDialogView
            {
                DataContext = new ConfirmDialogViewModel(dialogs, "Delete ingredient",
                    "aura and its 3 variants will be removed.", "Delete"),
            },
                variant, $"zz-dialog-confirm-{key}.png");

            // The rule form, in both of its states. Two captures rather than one because the two
            // differ in more than a title: the edit form opens seated on an existing rule, and only
            // the add form shows the "pick a layer and a variant" refusal a fresh row starts in.
            using (var ruleBook = RecipeWithRules().book)
            {
                var ruleRecipe = ruleBook.Recipes[0];
                Capture(new Views.RuleDialogView
                {
                    DataContext = new RuleDialogViewModel(dialogs, ruleRecipe.Manifest,
                        ruleRecipe.Ingredients, editingIndex: -1),
                }, variant, $"zz-dialog-rule-add-{key}.png");

                // Seated on rule 1, with a second target added, so the frame shows a multi-target
                // rule and the remove control that only makes sense once there are two.
                var editing = new RuleDialogViewModel(dialogs, ruleRecipe.Manifest,
                    ruleRecipe.Ingredients, editingIndex: 0);
                editing.AddTargetCommand.Execute(null);
                Capture(new Views.RuleDialogView { DataContext = editing },
                    variant, $"zz-dialog-rule-edit-{key}.png");
            }

            // A search that matches nothing: the tree empties and the pane must say so rather than
            // simply going blank.
            using (var book = ExplorerViewModelTests.TwoRecipeBook())
            {
                var nav = new FakeNav();
                using var explorer = new ExplorerViewModel(book, nav, dialogs,
                    new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                    ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(),
                    new FilePickerService(),
                    ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs),
                    new StatusService());
                explorer.SearchQuery = "zzz-nothing-matches-this";
                Capture(new Views.ExplorerView { DataContext = explorer }, variant,
                    $"zz-explorer-no-results-{key}.png");
            }
        }
    }

    /// <summary>A book with a layerOrder entry naming no ingredient — reported by Validator, so the
    /// detail card takes its invalid branch.</summary>
    private static Nfty.Core.Formats.LoadedCookBook InvalidBook()
    {
        var ing = new Nfty.Core.Formats.LoadedIngredient
        {
            Manifest = new Nfty.Core.Model.IngredientManifest("bg", "Background",
                Nfty.Core.Model.LayerKind.Custom, null,
                new[] { new Nfty.Core.Model.Variant("day", "Day", 1) }),
            VariantImages = new Dictionary<string, SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>>
            { ["day"] = new(8, 8) },
        };
        var recipe = new Nfty.Core.Formats.LoadedRecipe
        {
            Manifest = new Nfty.Core.Model.RecipeManifest("cat", "Cat",
                new[] { "bg", "missing-layer" }, Array.Empty<Nfty.Core.Model.IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new Nfty.Core.Formats.LoadedCookBook
        {
            Manifest = new Nfty.Core.Model.CookBookManifest("cb", "VaporPets",
                new Nfty.Core.Model.Dimensions(8, 8),
                new Nfty.Core.Model.Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
    }
}
