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
using Nfty.Core.Demo;
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

    private static Button Btn(string content, params string[] classes)
    {
        var btn = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var cls in classes) btn.Classes.Add(cls);
        return btn;
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
            var cookBookVm = new CookBookDetailViewModel(cookBook, () => { }, () => { }, () => { }, () => { });
            Capture(new Views.CookBookDetailView { DataContext = cookBookVm }, variant, $"cookbook-detail-{key}.png");

            // The SAME card over the built-in demo, whose DNA space is 7,155,000. Every other fixture
            // in this file is a two-variant toy, so the UNIQUE DNA tile had only ever been captured
            // holding a single digit — no frame in the set was evidence that it can hold a real
            // figure, which is the one number on this card that has no upper bound.
            var demoDir = Directory.CreateTempSubdirectory();
            using (var demo = CookBookArchive.Read(DemoCookBook.WriteTo(demoDir.FullName)))
            {
                Capture(new Views.CookBookDetailView
                {
                    DataContext = new CookBookDetailViewModel(demo, () => { }, () => { }, () => { }, () => { }),
                }, variant, $"cookbook-detail-demo-{key}.png");
            }
            demoDir.Delete(recursive: true);

            var (ruleBook, ruleRecipe) = RecipeWithRules();
            // Rise supplied so the action row captures as the app shows it: die and Rise, both live.
            using (var vm = new RecipeDetailViewModel(ruleRecipe, ruleBook, new ImageBridge(), _ => { },
                null, false, null, null, _ => { }))
            {
                Capture(new Views.RecipeDetailView { DataContext = vm }, variant, $"recipe-detail-{key}.png");
                Capture(new Views.RecipeDetailView { DataContext = vm }, variant, $"recipe-detail-min-{key}.png",
                    RisePages.MinArea.Width, RisePages.MinArea.Height);
            }

            var ingredientBook = ExplorerViewModelTests.TwoRecipeBook();
            var catRecipe = ingredientBook.Recipes.First(r => r.Manifest.Id == "cat");
            var firstIngredient = catRecipe.Ingredients[0];
            // picker + dialogs supplied so Export preview captures ENABLED.
            using (var vm = new IngredientDetailViewModel(firstIngredient, catRecipe, ingredientBook, new ImageBridge(),
                () => { }, () => false, null, null,
                new FilePickerService(), new FakeDialogs(), null, _ => { }))
            {
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-detail-{key}.png");
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-detail-min-{key}.png",
                    RisePages.MinArea.Width, RisePages.MinArea.Height);
            }

            var (dynBook, dynRecipe, dynIng) = DynamicIngredient();
            using (var vm = new IngredientDetailViewModel(dynIng, dynRecipe, dynBook, new ImageBridge(),
                () => { }, () => false, null, null,
                new FilePickerService(), new FakeDialogs(), null, _ => { }))
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
                new KitchenSession(), rise: _ => null!);
            Capture(new Views.LandingView { DataContext = vm }, variant, $"landing-{key}.png");
            if (!empty)
            {
                // The OPEN column of six, at the area the smallest window gives a page and at 1366.
                Capture(new Views.LandingView { DataContext = vm }, variant, $"landing-rise-min-{key}.png",
                    (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
                    (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale);
                Capture(new Views.LandingView { DataContext = vm }, variant, $"landing-rise-1366-{key}.png",
                    (1366 - 24) / ShellViewModel.BaseScale, 640);
            }

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
                        book => null!, set => null!, (_, _, _) => null!, kitchen, rise: _ => null!);
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
            IngredientArchive.Write(Path.Combine(dir, "aura.igt"), loose.Manifest, loose.VariantImages, DepthMaps.None);
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
                var vm = new SetBrowserViewModel(loaded, rise: _ => throw new NotSupportedException(),
                    nav: new FakeNav(), bridge: new ImageBridge());
                vm.SelectedItem = vm.Items[0];
                Capture(new Views.SetBrowserView { DataContext = vm }, variant, $"set-browser-{key}.png");
                vm.Dispose();
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }

    /// <summary>The Set browser with Rise in its header, at the smallest page and at 1366, both themes.</summary>
    [AvaloniaFact]
    public void Capture_set_browser_rise()
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
                var vm = new SetBrowserViewModel(SetReader.Read(dir), rise: _ => throw new NotSupportedException(),
                    nav: new FakeNav(), bridge: new ImageBridge());
                vm.SelectedItem = vm.Items[0];
                Capture(new Views.SetBrowserView { DataContext = vm }, variant, $"set-rise-min-{key}.png",
                    (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
                    (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale);
                Capture(new Views.SetBrowserView { DataContext = vm }, variant, $"set-rise-1366-{key}.png",
                    (1366 - 24) / ShellViewModel.BaseScale, 640);
                vm.Dispose();
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }

    /// <summary>The editor's footer with a Rise factory wired: Rise lit on a clean editor, dim (with its
    /// reason on the tooltip) once the editor has unsaved edits.</summary>
    [AvaloniaFact]
    public void Capture_editor_rise()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var book = ExplorerViewModelTests.TwoRecipeBook();
            var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();
            var vm = new IngredientEditorViewModel(cat.Ingredients[0], cat, book, new ImageBridge(), nav,
                new CookBookSession(), dialogs, new FilePickerService(), isEditing: () => true,
                rise: src => new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), new StatusService()));
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-rise-clean-{key}.png");
            vm.ApplyToolStroke(new[] { (0, 0) });
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-rise-dirty-{key}.png");
            vm.Dispose();
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

            // THE SAME SCREEN, MAGNIFIED. Zoom touches four things that each map between canvas
            // pixels and the screen - the drawn art, the pointer, the marquee and the backdrop
            // lattice - and a frame is where the last of those is checked: the squares have to grow
            // with the art and stay in phase with its corner, which no assertion about a scale
            // factor can show. The pan is off-center on purpose, because a lattice anchored on the
            // panel rather than on the art looks right until the art moves.
            // Real pixels first: the fixture is a flood fill, and a magnified flat gray shows nothing
            // about whether the lattice lines up with the art.
            dynVm.ActiveTool = EditorTool.Brush;
            dynVm.BrushSize = 1;
            dynVm.BrushValue = 40;
            foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (4, 3), (5, 4), (6, 5), (2, 6) })
                dynVm.ApplyToolStroke(new[] { (x, y) });
            // Two, not four: this fixture is 8x8 in a 318px tile, so it is already forty device
            // pixels to the art pixel before any zoom at all, and four times that shows two pixels
            // and a half. The zoom range is sized for a 512px drawing, not for a sprite.
            dynVm.Zoom = 2;
            dynVm.PanBy(60, 40);
            Capture(new Views.IngredientEditorView { DataContext = dynVm }, variant, $"editor-zoom-{key}.png");
            dynVm.Dispose();
            dynBook.Dispose();

            // The palette strip's remaining states, none of which the two frames above reach: a
            // value-map layer switched INTO color (so both halves of the tray are live and the ramp
            // is the rainbow one), saved swatches actually present, and the opacity lock OFF — the
            // one state in which the alpha axis is not dimmed. Set directly rather than through
            // ToggleOpacityLock: the capture is of the unlocked strip, not of the warning dialog.
            var (colBook, colRecipe, colIng) = DynamicIngredient();
            var palette = new PaletteService(StateStore.InMemory());
            palette.Add(new RgbColor(0x6D, 0x4F, 0x9C), PaletteMode.Color);
            palette.Add(new RgbColor(0x3D, 0x6B, 0x52), PaletteMode.Color);
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

    /// <summary>The HSV / HSL tray: the editor's colorize rail with HSL chosen, and the New Ingredient
    /// wizard's color column with HSL chosen.</summary>
    [AvaloniaFact]
    public void Capture_color_model_toggle()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var (book, recipe, ing) = DynamicIngredient();
            var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
                new CookBookSession(), new FakeDialogs(), new FilePickerService());
            vm.SetModelHslCommand.Execute(null);
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-color-hsl-{key}.png");
            vm.Dispose();
            book.Dispose();

            var wiz = new NewIngredientViewModel(new FakeDialogs()) { Name = "Aura" };
            wiz.SetModelHslCommand.Execute(null);
            Capture(new Views.NewIngredientView { DataContext = wiz }, variant, $"wizard-ingredient-hsl-{key}.png");

            using var import = ImportImageForm(new FakeDialogs());
            import.SetModelHslCommand.Execute(null);
            Capture(new Views.ImportImageView { DataContext = import }, variant,
                $"import-image-hsl-{key}.png", width: 1180, height: 780);
        }
    }

    /// <summary>The editor's canvas mapping in the states <see cref="Capture_editor_paint"/> does not
    /// reach: a coarser lattice step under zoom and pan with a marquee drawn through the same
    /// geometry, the grid off, and the grid at a non-square art size. They exist so a refactor of the
    /// mapping arithmetic can be proved pixel-identical.</summary>
    [AvaloniaFact]
    public void Capture_editor_mapping()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var (book, recipe, ing) = DynamicIngredient();
            var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
                new CookBookSession(), new FakeDialogs(), new FilePickerService());
            vm.ActiveTool = EditorTool.Brush;
            vm.BrushSize = 1;
            vm.BrushValue = 40;
            foreach (var (x, y) in new[] { (1, 1), (2, 1), (1, 2), (4, 3), (5, 4), (6, 5), (2, 6) })
                vm.ApplyToolStroke(new[] { (x, y) });

            vm.GridSize = 2;
            vm.Zoom = 3;
            vm.PanBy(-70, 25);
            vm.ActiveTool = EditorTool.Select;
            vm.ApplyToolStroke(new[] { (1, 1), (4, 3) });
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-mapping-step-{key}.png");

            vm.ShowPixelGrid = false;
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-mapping-flat-{key}.png");

            vm.ShowPixelGrid = true;
            vm.GridSize = 1;
            vm.Zoom = 1;
            Capture(new Views.IngredientEditorView { DataContext = vm }, variant, $"editor-mapping-fit-{key}.png");
            vm.Dispose();
            book.Dispose();
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

    /// <summary>
    /// Captures the editor with its rail on the REFERENCES tab, which is where the reference panel
    /// lives.
    /// </summary>
    /// <remarks>
    /// <b>These four frames spent a release showing the Colorize tab.</b> The panel used to sit
    /// beneath the whole Colorize block in one scroller, so this scrolled to the end to reach it;
    /// splitting the rail into two tabs moved it out of that scroller entirely and the scroll became
    /// a no-op — leaving four frames named <c>refs</c>, every one of them a picture of the colorize
    /// rail, with the tab bar beside them announcing "REFERENCES 3/5" about a panel nothing showed.
    /// Nothing failed, because a capture asserts nothing; it is the looking that catches this, which
    /// is the whole argument for the harness. <c>RailTabCaptureTests</c> now fails if the frame is
    /// taken on the wrong tab.
    /// </remarks>
    private static void CaptureReferences(IngredientEditorViewModel vm, ThemeVariant variant, string fileName)
    {
        vm.ShowReferencesTabCommand.Execute(null);
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // A CAPTURE ASSERTS NOTHING, SO THIS ONE ASSERTS THE ONE THING THAT WENT WRONG. The frame has
        // to show the panel it is named for. IsEffectivelyVisible rather than Bounds: Avalonia leaves
        // a control's arranged bounds alone when an ancestor goes invisible, so a panel that was ever
        // laid out keeps reporting the size it had.
        if (!view.GetVisualDescendants().OfType<TextBlock>()
                 .Any(t => t.Text == "IN THIS RECIPE" && t.IsEffectivelyVisible))
            throw new InvalidOperationException(
                $"{fileName} was about to be captured with the reference panel off screen — "
                + "the rail is on the wrong tab.");

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

            // The card the window close, the editor's Back and Rise's Back all raise over unsaved work.
            Capture(new Views.LeaveDialogView { DataContext = new LeaveDialogViewModel(dialogs, "Leave the editor?", "Aura has unsaved changes. Leaving discards them.", canSave: true) }, variant, $"leave-editor-{key}.png");

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
    /// <summary>
    /// The export dialog, in both themes and in both of its states — closed, and armed for sealing
    /// with its passphrase fields open and its honest-limits sentence showing.
    /// </summary>
    /// <remarks>
    /// Both states are captured because the seal panel is the one place in this app that uses
    /// <c>IsVisible</c> on something with content below it, under the reveal exception. What a frame
    /// has to show is that opening it pushes the manifest down and moves nothing else — which is
    /// invisible in the markup, where the panel plainly has two states.
    /// </remarks>
    [AvaloniaFact]
    public void Capture_export_dialog()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();

            var plain = new ExportDialogViewModel(ExportCaptureSet(withModels: true), new FilePickerService(),
                new NoopFolderRevealer(), new FakeDialogs());
            Capture(new Views.ExportDialogView { DataContext = plain }, variant,
                $"export-{key}.png", width: 1180, height: 780);

            // A Set cooked without models: the row stays laid out, disabled, with its reason.
            var bare = new ExportDialogViewModel(ExportCaptureSet(), new FilePickerService(),
                new NoopFolderRevealer(), new FakeDialogs());
            Capture(new Views.ExportDialogView { DataContext = bare }, variant,
                $"export-nomodels-{key}.png", width: 1180, height: 780);

            // THE IMAGES PAGE, WITH BOTH GROUPS ARMED. A capture of a tab nothing switched to is a
            // picture of the tab beside it - the same way four `editor-refs` frames were four
            // pictures of the colorize rail. Each of these states its Tab explicitly.
            var images = new ExportDialogViewModel(ExportCaptureSet(withModels: true), new FilePickerService(),
                new NoopFolderRevealer(), new FakeDialogs())
            {
                Tab = ExportTab.Images,
                MakeSpriteSheet = true,
                NumberWatermark = true,
            };
            Capture(new Views.ExportDialogView { DataContext = images }, variant,
                $"export-images-{key}.png", width: 1180, height: 780);

            var sealing = new ExportDialogViewModel(ExportCaptureSet(withModels: true), new FilePickerService(),
                new NoopFolderRevealer(), new FakeDialogs())
            {
                Tab = ExportTab.Seal,
                IsSealed = true,
                Passphrase = "correct-horse-battery",
                PassphraseConfirm = "correct-horse-battery",
                Note = "Draft for review - please don't redistribute",
            };
            Capture(new Views.ExportDialogView { DataContext = sealing }, variant,
                $"export-sealed-{key}.png", width: 1180, height: 780);

            // The busy card, in the state it is actually seen in: mid-job, with a real phase line.
            var busy = new BusyViewModel("Opening Set", "Unpacking...")
            {
                IsIndeterminate = false,
                Progress = 0.42,
            };
            Capture(new Views.BusyView { DataContext = busy }, variant,
                $"busy-{key}.png", width: 1180, height: 400);

            // The Rise batch export's card: counted and stoppable, the one busy card with a Cancel.
            using var exportCts = new System.Threading.CancellationTokenSource();
            var exporting = new BusyViewModel("Exporting models", "Rising 7 of 20 models…", exportCts)
            {
                IsIndeterminate = false,
                Progress = 0.35,
            };
            Capture(new Views.BusyView { DataContext = exporting }, variant,
                $"busy-rise-export-{key}.png", width: 1180, height: 400);

            using var import = ImportImageForm(new FakeDialogs());
            Capture(new Views.ImportImageView { DataContext = import }, variant,
                $"import-image-{key}.png", width: 1180, height: 780);
        }
    }

    /// <summary>
    /// The import-an-image form, on the state that carries everything it can say at once: a COLOR
    /// picture, the Dynamic kind (which discards those colors), and a name that clashes with a layer
    /// already in the recipe.
    /// </summary>
    /// <remarks>
    /// Both of its warning runs are painted only in that state, and a frame of the clean form would
    /// show neither. The picture is a real file on disk because the form reads one - the preview,
    /// the color check and the built layer all come off that one decode.
    /// </remarks>
    /// <param name="dialogs">The dialog layer.</param>
    /// <returns>The loaded form; the caller disposes it.</returns>
    internal static ImportImageViewModel ImportImageForm(IDialogService dialogs)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-imp-").FullName;
        string picture = Path.Combine(dir, "bg.png");
        using (var img = new Image<Rgba32>(64, 64))
        {
            // A little drawing rather than a flat fill: a flat rectangle previews identically in
            // color and in grayscale, which is the one thing this frame exists to tell apart.
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    img[x, y] = new Rgba32((byte)(x * 4), (byte)(y * 3), (byte)(200 - x * 2), 255);
            img.Save(picture);
        }

        var form = new ImportImageViewModel(dialogs, picture, new Dimensions(64, 64),
            new ImageBridge(), new[] { "bg" });
        form.TryLoad();
        form.Kind = LayerKind.Dynamic;
        return form;
    }

    /// <summary>A real cooked Set on disk, so the manifest lines are real counts and real sizes
    /// rather than a fixture's idea of them.</summary>
    internal static string ExportCaptureSet(bool withModels = false)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-cap-").FullName, "Chest Demo");
        var ids = new[] { "a", "b", "c", "d" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v,
                v => new SixLabors.ImageSharp.Image<Rgba32>(8, 8, new Rgba32((byte)v[0], 40, 60, 255))),
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(8, 8),
                new Collection("Chest Demo", "d", "CHST"),
                new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
        using var set = Generator.Generate(book, new GenerateOptions(4, "launch"));
        SetWriter.Write(set, dir, pack: false);
        if (withModels)
        {
            var models = Path.Combine(dir, SetLayout.ModelsDir);
            Directory.CreateDirectory(models);
            File.WriteAllBytes(Path.Combine(models, "0001.glb"), new byte[] { 1, 2, 3, 4 });
        }
        return dir;
    }

    /// <summary>The asset inspector, which has no capture of its own and is the second-tallest
    /// modal in the app.</summary>
    [AvaloniaFact]
    public void Capture_inspector()
    {
        if (Dir is null) return;

        var dir = Directory.CreateTempSubdirectory().FullName;
        using (var gen = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(3, "seed1")))
            SetWriter.Write(gen, dir, pack: false);
        using var loaded = SetReader.Read(dir);
        using var browser = new SetBrowserViewModel(loaded);

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            using var ins = new SetInspectViewModel(browser.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());
            Capture(new Views.SetInspectView { DataContext = ins }, variant,
                $"inspector-{variant.Key.ToString()!.ToLowerInvariant()}.png", 1180, 820);
        }
    }

    /// <summary>The Explorer at the EXACT page area the smallest allowed window gives it — the
    /// check that lowering the window minimum did not quietly clip a page.</summary>
    [AvaloniaFact]
    public void Capture_explorer_at_minimum()
    {
        if (Dir is null) return;

        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            new CookBookSession(), new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs),
            new StatusService());
        vm.SelectNodeCommand.Execute(vm.Root);

        Capture(new Views.ExplorerView { DataContext = vm }, ThemeVariant.Dark,
            "explorer-minimum.png",
            (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale);
        vm.Dispose();
    }

    /// <summary>The width the page area really has at the smallest window the app allows.</summary>
    internal static readonly double PageWidth =
        ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale;

    /// <summary>The height it really has, once the titlebar and status bar are taken off.</summary>
    internal static readonly double PageHeight =
        (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale;

    /// <summary>
    /// Whether this view is content for the DIALOG LAYER rather than a page.
    /// </summary>
    /// <param name="view">The view about to be captured.</param>
    /// <returns>True for a modal, the quick-reference sheet, or the inspector.</returns>
    /// <remarks>
    /// Derived from the MARKUP — the root's own style class — rather than from a hand-written list
    /// of view names, for the reason <c>PixelPerfectRenderingTests</c> and <c>ThemeResourceTests</c>
    /// already enumerate themselves: a list is a second statement of the fact, and the one screen
    /// nobody remembers to add to it is the one that goes unchecked. A modal added later is framed
    /// correctly without anyone touching this file.
    /// </remarks>
    private static bool IsDialogLayer(Control view) =>
        view is UserControl { Content: Border root }
            && (root.Classes.Contains("modal") || root.Classes.Contains("sheet")
                || root.Classes.Contains("inspect"));

    /// <summary>
    /// Renders one view into a themed window and saves the frame.
    /// </summary>
    /// <param name="view">The view to draw.</param>
    /// <param name="variant">Which theme.</param>
    /// <param name="fileName">Where to save it, inside the capture directory.</param>
    /// <param name="width">Page width. Ignored for dialog-layer content.</param>
    /// <param name="height">Page height. Ignored for dialog-layer content.</param>
    /// <remarks>
    /// <b>A MODAL IS CAPTURED AT THE AREA THE APP REALLY GIVES IT, AND A PAGE IS NOT.</b> A page may
    /// scroll, so 1180x720 is the right frame for one: it is the mockups' own units, and the pane
    /// track alone needs 1014px, so a narrower capture makes a correct layout look clipped. A modal
    /// may NOT scroll, so the only frame that says anything true about one is the page area it is
    /// actually handed — <c>MinWindow / BaseScale</c> by <c>(MinWindow - ChromeReserve) /
    /// BaseScale</c>, about 1067x526. The export card was being drawn at 780 tall, 254px more than
    /// any window this app allows, so it photographed as a tidy card while the running app scrolled
    /// it on every tab — the same defect this file has already shipped twice, once when a card was
    /// rendered alone in a 1180px window 200px wider than the page ever is, and once when five
    /// wizards were measured at a raw 1180x720 and all five overflowed in the app. A frame of a card
    /// nothing hosts is not evidence.
    /// </remarks>
    /// <summary>The Cook dialog's <em>Include 3D models</em> row in its three states: the box unticked
    /// (collapsed), ticked with the options open (the tallest the card gets), and a 2D-only book.</summary>
    [AvaloniaFact]
    public void Capture_cook_dialog_models()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var dialogs = new FakeDialogs();
            using var clean = CookDialogModelsTests.Book();
            using var flat = CookDialogModelsTests.Book(variants: 3, alpha: 128);

            CookDialogViewModel Vm(Nfty.Core.Formats.LoadedCookBook b) =>
                new(b, new FilePickerService(), new NoopFolderRevealer(), dialogs) { Count = 500, Seed = "vapor" };

            Capture(new Views.CookDialogView { DataContext = Vm(clean) }, variant, $"cook-models-collapsed-{key}.png");
            var ticked = Vm(clean); ticked.IncludeModels = true;
            Capture(new Views.CookDialogView { DataContext = ticked }, variant, $"cook-models-ticked-closed-{key}.png");
            var open = Vm(clean);
            open.IncludeModels = true; open.ShowModelOptions = true;
            Capture(new Views.CookDialogView { DataContext = open }, variant, $"cook-models-expanded-{key}.png");
            Capture(new Views.CookDialogView { DataContext = Vm(flat) }, variant, $"cook-models-2d-only-{key}.png");
        }
    }

    /// <summary>The Cook card with the recipe choice, and an extend with the total it reaches.</summary>
    [AvaloniaFact]
    public void Capture_cook_dialog_recipe_and_total()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            using var book = CookRecipeAndTotalTests.Book();
            CookDialogViewModel Vm() =>
                new(book, new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs()) { Count = 50, Seed = "vapor" };

            var chosen = Vm();
            chosen.SelectedRecipe = chosen.RecipeChoices.Single(c => c.Id == "dog");
            Capture(new Views.CookDialogView { DataContext = chosen }, variant, $"cook-recipe-choice-{key}.png");

            var extending = Vm();
            extending.IsExtending = true; extending.ExistingCount = 4200;
            Capture(new Views.CookDialogView { DataContext = extending }, variant, $"cook-extending-total-{key}.png");

            var running = Vm();
            running.IsExtending = true; running.ExistingCount = 4200; running.IsRunning = true;
            running.PhaseText = $"Extending 4200 assets {running.ResultingTotalText}…"; running.Progress = 0.4;
            Capture(new Views.CookDialogView { DataContext = running }, variant, $"cook-running-{key}.png");
        }
    }

    /// <summary>The 2D-only marks (Explorer tree, Kitchen shelf) and the opacity-lock warning.</summary>
    [AvaloniaFact]
    public void Capture_two_d_only_marks()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            var nav = new FakeNav();
            var dialogs = new FakeDialogs();

            // Explorer: a book whose first layer carries one half-transparent pixel.
            var book = ExplorerViewModelTests.TwoRecipeBook();
            var layer = book.Recipes[0].Ingredients[0];
            var img = layer.VariantImages.Values.First();
            img[0, 0] = new Rgba32(200, 20, 20, 120);
            var vm = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
                new CookBookSession(), new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService());
            vm.Root.Children[0].IsExpanded = true;
            Capture(new Views.ExplorerView { DataContext = vm }, variant, $"t15-explorer-{key}.png");

            // Kitchen shelf: the demo workspace plus one 2D-only book.
            var (dir, ktn) = ShelfWorkspace();
            try
            {
                using var flat = new LoadedIngredient
                {
                    Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                        new[] { new Variant("v1", "V1", 1) }),
                    VariantImages = new Dictionary<string, Image<Rgba32>>
                    { ["v1"] = new(8, 8, new Rgba32(1, 2, 3, 100)) },
                };
                var rcp = new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { flat },
                };
                CookBookArchive.Write(Path.Combine(dir, "Aa-flat.cbk"),
                    new CookBookManifest("cb", "A Flat Collection Name Far Too Long For One Card", new Dimensions(512, 512),
                        new Collection("Flat Collection", "", "F"),
                        new Dictionary<string, double> { ["cat"] = 100 }), new[] { rcp });
                var kitchen = new KitchenSession();
                kitchen.Open(ktn);
                var landing = new LandingViewModel(nav, dialogs, new FilePickerService(),
                    new RecentsService(Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
                    b => null!, s => null!, (_, _, _) => null!, kitchen, rise: _ => null!);
                Capture(new Views.LandingView { DataContext = landing }, variant, $"t15-shelf-{key}.png");
            }
            finally { Directory.Delete(dir, true); }

            // The warning, as the editor shows it.
            Capture(new Views.ConfirmDialogView
            {
                DataContext = new ConfirmDialogViewModel(dialogs, "Allow partial transparency?",
                    "A pixel that is only partly there cannot become a voxel. Save one, and this CookBook "
                    + "becomes 2D only: it can no longer be cooked into 3D models until every such pixel is "
                    + "made fully opaque or fully clear (Make voxel-ready on the Rise page does this).\n\n"
                    + "This applies to what you paint from here on; nothing already on the canvas changes.",
                    "Allow partial alpha"),
            }, variant, $"t15-warning-{key}.png");
        }
    }

    private static void Capture(Control view, ThemeVariant variant, string fileName,
        double width = 1180, double height = 720)
    {
        if (IsDialogLayer(view)) (width, height) = (PageWidth, PageHeight);
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
                var vm = new CookBookDetailViewModel(broken, () => { }, () => { }, () => { }, () => { });
                Capture(new Views.CookBookDetailView { DataContext = vm }, variant,
                    $"zz-cookbook-detail-invalid-{key}.png");
            }

            Capture(new Views.HelpView { DataContext = new HelpViewModel(dialogs) }, variant,
                $"zz-help-sheet-{key}.png");

            var autoSetting = new ParallelismSettingService(StateStore.InMemory(), () => null);
            Capture(new Views.SettingsDialogView { DataContext = new SettingsDialogViewModel(dialogs, autoSetting) },
                variant, $"zz-dialog-settings-auto-{key}.png");
            var customSetting = new ParallelismSettingService(StateStore.InMemory(), () => null);
            customSetting.Set(Nfty.Core.Parallelism.LogicalProcessors);
            Capture(new Views.SettingsDialogView { DataContext = new SettingsDialogViewModel(dialogs, customSetting) },
                variant, $"zz-dialog-settings-custom-{key}.png");

            Capture(new Views.ErrorDialogView
            { DataContext = new ErrorDialogViewModel(dialogs, "Could not open", "The file is not a readable CookBook.") },
                variant, $"zz-dialog-error-{key}.png");

            Capture(new Views.ConfirmDialogView
            {
                DataContext = new ConfirmDialogViewModel(dialogs, "Delete ingredient",
                    "aura and its 3 variants will be removed.", "Delete"),
            },
                variant, $"zz-dialog-confirm-{key}.png");

            Capture(new Views.LeaveDialogView
            {
                DataContext = new LeaveDialogViewModel(dialogs, "Leave Rise?",
                    "Body / Oak has unsaved depth on 2 variants. Leaving discards it.", canSave: true),
            },
                variant, $"zz-dialog-leave-{key}.png");

            // The validity report: the status chip opens it, so each of its three branches is a state a
            // reader meets — a clean book, a broken one, and a list long enough to cap + scroll.
            using (var okBook = ExplorerViewModelTests.TwoRecipeBook())
                Capture(new Views.ValidityDialogView
                { DataContext = new ValidityDialogViewModel(dialogs, okBook, Array.Empty<string>()) },
                    variant, $"zz-dialog-validity-valid-{key}.png");
            using (var badBook = InvalidBook())
            {
                Capture(new Views.ValidityDialogView
                { DataContext = new ValidityDialogViewModel(dialogs, badBook, Nfty.Core.Formats.Validator.Validate(badBook)) },
                    variant, $"zz-dialog-validity-broken-{key}.png");
                var many = Enumerable.Range(1, 14)
                    .Select(n => $"Recipe 'cat' layerOrder references unknown ingredient 'missing-layer-{n}'.").ToList();
                Capture(new Views.ValidityDialogView
                { DataContext = new ValidityDialogViewModel(dialogs, badBook, many) },
                    variant, $"zz-dialog-validity-many-{key}.png");
            }

            // The book settings card: a weight edited (the caveat lit), then the same card refusing.
            foreach (var (tag, locked, refuse) in new (string, bool, bool)[]
            {
                ("edited", false, false),
                ("refused", false, true),
                ("locked", true, false),
            })
            {
                var card = BookSettingsCard(dialogs, out var settingsBook, locked, refuse);
                Capture(new Views.BookSettingsDialogView { DataContext = card }, variant,
                    $"zz-dialog-book-settings-{tag}-{key}.png");
                settingsBook.Dispose();
            }

            // Make voxel-ready's preview, hugging a short list and capped (scrolling inside) on a long one.
            foreach (var (n, tag) in new[] { (3, "short"), (14, "long") })
            {
                using var card = RiseVoxelReadyTests.PreviewCard(dialogs, n);
                Capture(new Views.VoxelReadyDialogView { DataContext = card }, variant,
                    $"zz-dialog-voxelready-{tag}-{key}.png");
            }

            // The colorize save prompt, in its riskiest state: the overwrite box TICKED, which is
            // the one path that discards a colorization nothing on disk can rebuild. It had never
            // been captured at all, which is how it stayed a flat slab with a literal radius long
            // after every other modal had moved to the shared grammar - a state no fixture renders
            // looks fine forever.
            var colorVm = new ColorSaveDialogViewModel(dialogs, "aura") { Overwrite = true };
            Capture(new Views.ColorSaveDialogView { DataContext = colorVm }, variant,
                $"zz-dialog-colorsave-{key}.png");

            // The cook dialog in the state that carries its riskiest layout: FINISHED, with the
            // output path in it. That path is the longest string this card ever holds and the only
            // one it cannot wrap, so a fixture with a short path proves nothing - this one is a real
            // Windows path of the length a temp directory actually produces. Nothing rendered this
            // state until the strip existed, which is exactly how it shipped as three lines of
            // wrapped prose nobody could click.
            using (var cookBook = RecipeWithRules().book)
            {
                var cookVm = new CookDialogViewModel(cookBook, new FilePickerService(),
                    new NoopFolderRevealer(), dialogs)
                {
                    IsDone = true,
                    ResultText = "Cooked 20 assets.",
                    OutputPath = @"C:\Users\Corde\AppData\Local\Temp\nfty-demo\VaporPets-launch",
                };
                Capture(new Views.CookDialogView { DataContext = cookVm }, variant,
                    $"zz-dialog-cook-done-{key}.png");

                // The same done state after extending over a changed book: the CLI's warning, in
                // warning ink, below the path. The riskiest ink and the tallest content the done
                // state carries, so it is the frame that shows the card does not grow.
                cookVm.ProvenanceWarning = Nfty.Core.Output.SetProvenance.Warning(
                    new string('a', 64), new string('b', 64))!;
                Capture(new Views.CookDialogView { DataContext = cookVm }, variant,
                    $"zz-dialog-cook-done-warned-{key}.png");

                // And the FORM state, which is where the repeats switch lives. The engine and the
                // CLI always had that switch; this dialog never offered it, so a person using only
                // the app could not mint past the unique space - and no capture rendered the form,
                // which is how the omission survived.
                var cookForm = new CookDialogViewModel(cookBook, new FilePickerService(),
                    new NoopFolderRevealer(), dialogs) { Count = 500, Seed = "vapor", Unlimited = true }.Landed();
                Capture(new Views.CookDialogView { DataContext = cookForm }, variant,
                    $"zz-dialog-cook-form-{key}.png");
            }

            // The Menu export card, atlas-refused (the long warning on its reserved line) and quiet.
            {
                using var wide = MenuExportDialogTests.WideBook();
                Capture(new Views.MenuExportDialogView { DataContext = MenuExportDialogTests.RefusedVm(wide) },
                    variant, $"zz-dialog-menu-export-{key}.png");
                using var calm = CookDialogModelsTests.Book();
                Capture(new Views.MenuExportDialogView
                {
                    DataContext = new MenuExportDialogViewModel(calm, @"C:/Users/Corde/Kitchen/Blocks.cbk",
                        new FilePickerService(), dialogs, new StatusService()),
                }, variant, $"zz-dialog-menu-export-quiet-{key}.png");
            }

            // The ingredient pane for a chase layer: its absent flag beside the rule flag, and the
            // variant percentages that flag exists to explain — they read the odds of GETTING each
            // variant, not its share among its siblings.
            {
                var (rawBook, chaseRecipe, _) = DynamicIngredient();
                var optional = new LoadedRecipe
                {
                    Manifest = chaseRecipe.Manifest with
                    {
                        AbsentPercent = new Dictionary<string, double>
                        {
                            [chaseRecipe.Manifest.LayerOrder[^1]] = 90,
                        },
                    },
                    Ingredients = chaseRecipe.Ingredients,
                };
                // The BOOK carries the optional recipe too. Handing the pane a detached recipe left
                // the flag saying 90% above percentages that still read their share among siblings,
                // because the percentages come from the book — a fixture that lies about the
                // product is worse than no fixture.
                var chaseBook = new LoadedCookBook
                {
                    Manifest = rawBook.Manifest,
                    Recipes = new[] { optional },
                    SourceSha256 = rawBook.SourceSha256,
                };
                var chaseIng = optional.Ingredients
                    .First(i => i.Manifest.Id == optional.Manifest.LayerOrder[^1]);
                using var chasePane = new IngredientDetailViewModel(chaseIng, optional, chaseBook,
                    new ImageBridge(), () => { }, () => false);
                Capture(new Views.IngredientDetailView { DataContext = chasePane }, variant,
                    $"zz-ingredient-chase-{key}.png");
                rawBook.Dispose();
            }

            // A recipe with an optional layer, unlocked, so the frame carries the chance column,
            // its editable fields and the factor chip that counts one higher for it. Every other
            // recipe frame is a book that does not use the feature, and a state no fixture reaches
            // renders nothing and therefore looks fine.
            using (var chanceBook = RecipeWithRules().book)
            {
                var chanceRecipe = chanceBook.Recipes[0];
                var withChance = new LoadedRecipe
                {
                    Manifest = chanceRecipe.Manifest with
                    {
                        AbsentPercent = new Dictionary<string, double> { ["aura"] = 85 },
                    },
                    Ingredients = chanceRecipe.Ingredients,
                };
                using var chanceVm = new RecipeDetailViewModel(withChance, chanceBook,
                    new ImageBridge(), _ => { },
                    moveLayer: (_, _) => Task.FromResult<LoadedCookBook?>(null), canReorder: true,
                    editRules: (_, _) => Task.FromResult<LoadedCookBook?>(null), dialogs: dialogs);
                Capture(new Views.RecipeDetailView { DataContext = chanceVm }, variant,
                    $"zz-recipe-optional-layers-{key}.png");
            }

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

    /// <summary>
    /// The whole icon set at the three sizes the app draws it (12, 14, 18 px), both themes. A glyph
    /// has to differ from its NEIGHBOUR, which only shows beside it at size: Rise sits next to Kitchen,
    /// 2D-only next to Rise, and the rest follow in key order.
    /// </summary>
    [AvaloniaFact]
    public void Capture_icon_set()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        var all = new SortedDictionary<string, Geometry>(StringComparer.Ordinal);
        // Keys come from the generated dictionary's source; geometries resolve through the live app,
        // so a key that does not resolve there fails the capture instead of being skipped.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Nfty.App", "Themes", "Icons.axaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Nfty.App", "Themes", "Icons.axaml"));
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, "x:Key=\"(Icon[A-Za-z0-9]+)\""))
        {
            Assert.True(Application.Current!.TryFindResource(m.Groups[1].Value, out var res) && res is Geometry, m.Groups[1].Value);
            all[m.Groups[1].Value] = (Geometry)res!;
        }
        var order = new List<string> { "IconKitchen", "IconRise", "Icon2dOnly", "IconLock", "IconRect", "IconImport", "IconDownload", "IconTypeSet" };
        order.AddRange(all.Keys.Where(k => !order.Contains(k)));
        Assert.Contains("IconRise", all.Keys);
        Assert.Contains("Icon2dOnly", all.Keys);

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            string theme = variant.Key.ToString()!.ToLowerInvariant();
            foreach (var (cls, px) in new[] { ("xs", 12), ("", 14), ("ti", 18) })
            {
                var panel = new WrapPanel { Margin = new Thickness(12), Width = 880 };
                foreach (var key in order)
                {
                    var path = new Avalonia.Controls.Shapes.Path { Data = all[key] };
                    path.Classes.Add("ico");
                    if (cls.Length > 0) path.Classes.Add(cls);
                    var cell = new StackPanel { Width = 84, Margin = new Thickness(0, 0, 0, 10), Spacing = 4 };
                    cell.Children.Add(new Border { Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left, Child = path });
                    cell.Children.Add(new TextBlock { Text = key.Substring(4), FontSize = 9 });
                    panel.Children.Add(cell);
                }
                // Three times larger: a 12px glyph at 1:1 cannot be inspected, and the stroke weight, the cut
                // corners and the gap to its neighbour are exactly what this sheet is for.
                var big = new LayoutTransformControl { LayoutTransform = new ScaleTransform(3, 3), Child = panel };
                var window = new Window { RequestedThemeVariant = variant, Width = 2700, Height = 1080, Content = big };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame()!;
                frame.Save(Path.Combine(Dir!, $"icons-{px}-{theme}.png"), PngBitmapEncoderOptions.Default);
                window.Close();
            }
        }
    }

    /// <summary>
    /// The Rise page, in every state its layout has to survive, in both themes, at the mockups' 1180x720
    /// AND at the area the smallest window really gives a page (<c>-min</c>).
    /// </summary>
    /// <remarks>
    /// <b>A CAPTURE ASSERTS NOTHING, SO EACH ARRANGED STATE IS VERIFIED INSIDE THE HELPER BEFORE THE
    /// FRAME IS SAVED.</b> A frame named <c>set-sealed-export</c> that is really a picture of the DEPTH
    /// tab is exactly how <c>editor-refs-*</c> spent a release photographing the wrong rail. The
    /// verification reads what the page itself says is on screen (the tab, the pill, the caveat), not
    /// the view model alone.
    /// </remarks>
    [AvaloniaFact]
    public void Capture_rise_page()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        var states = new (string Name, Func<(RiseViewModel Vm, IDisposable? Owner)> Build, Action<RisePage> Arrange)[]
        {
            // Oak with a front/back pattern; 8px art is 40 device px a cell at fit, so the numbers show.
            ("variant-painted", () => (RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), null),
                _ => { }),
            // Locked book: painting is a scratch surface, and the footer says why Save is not live.
            ("variant-locked", () => (RisePages.VariantVm(isEditing: () => false, oakDepth: RisePages.PaintedOak()), null),
                p => p.Vm.ApplyDepthStroke(new[] { (2, 2) }, Nfty.Core.Editing.DepthSide.Front, lower: false)),
            // A picture with partial alpha: listed under 2D ONLY, flagged on its row, inked on the tile.
            ("variant-ghost", () => (RisePages.VariantVm(), null),
                p => p.Vm.SelectItemCommand.Execute(p.Vm.Items.First(i => i.Name == "Ghost"))),
            // A right-drag in progress, never released: the canvas shows the back raised along row 3,
            // the 3D tile is being repainted on its budget, and the hover readout is blanked by the drag.
            ("mid-stroke", () => (RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), null),
                p =>
                {
                    var art = p.View.GetVisualDescendants().OfType<Avalonia.Controls.Image>().First(i => i.Name == "CanvasImage");
                    var origin = art.TranslatePoint(default, p.Window)!.Value;
                    var bmp = (Avalonia.Media.Imaging.Bitmap)art.Source!;
                    double scale = Math.Min(art.Bounds.Width / bmp.PixelSize.Width, art.Bounds.Height / bmp.PixelSize.Height);
                    double ox = (art.Bounds.Width - bmp.PixelSize.Width * scale) / 2;
                    double oy = (art.Bounds.Height - bmp.PixelSize.Height * scale) / 2;
                    Avalonia.Point At(int x, int y) => new(origin.X + ox + (x + 0.5) * scale, origin.Y + oy + (y + 0.5) * scale);
                    p.Window.MouseDown(At(1, 3), Avalonia.Input.MouseButton.Right);
                    p.Window.MouseMove(At(6, 3));
                    Dispatcher.UIThread.RunJobs();
                }),
            ("recipe", () => (RisePages.RecipeVm(), null), _ => { }),
            // The recipe page that writes through (step 10): before a layer is chosen, with one chosen, and
            // with a stroke painted on it (unsaved, the row's chip moved, the footer saying so).
            ("wt-picking", () => (RisePages.DemoWriteThroughVm(), null), _ => { }),
            ("wt-targeted", () => (RisePages.DemoWriteThroughVm(), null), p => RisePages.TargetLayer(p.Vm, 2)),
            ("wt-targeted-3d", () => (RisePages.DemoWriteThroughVm(), null), p =>
            {
                RisePages.TargetLayer(p.Vm, 2);
                p.Vm.SwapPreviewCommand.Execute(null);
            }),
            ("wt-locked", () => (RisePages.DemoWriteThroughVm(locked: true), null), p =>
            {
                RisePages.TargetLayer(p.Vm, 2);
                RisePages.PaintSolidPixel(p.Vm);
            }),
            ("wt-painted", () => (RisePages.DemoWriteThroughVm(), null), p =>
            {
                RisePages.TargetLayer(p.Vm, 2);
                RisePages.PaintSolidPixel(p.Vm);
            }),
            // The shipped Chest Demo, with the depth it was authored with: a body variant, a rolled
            // recipe and a cooked Set, so the frames judge the demo and not a test fixture.
            ("demo-variant", () => (RisePages.DemoVariantVm(), null), _ => { }),
            ("demo-recipe", () => (RisePages.DemoRecipeVm(), null), _ => { }),
            ("demo-set", () => { var (v, o) = RisePages.DemoSetVm(); return (v, o); }, _ => { }),
            // The 3D view fills the pane (swapped with the canvas), from the default camera, from a
            // three-quarter angle and from the side: the side view is where a flat sprite shows.
            ("demo-recipe-3d", () => (RisePages.DemoRecipeVm(), null), p => p.Vm.SwapPreviewCommand.Execute(null)),
            ("demo-recipe-3d-angle", () => (RisePages.DemoRecipeVm(), null), p =>
            {
                p.Vm.SwapPreviewCommand.Execute(null);
                p.Vm.Preview.OrbitStep(new System.Numerics.Vector2(2, 0));
                p.Vm.Preview.OrbitStep(new System.Numerics.Vector2(0, -1));
            }),
            ("demo-recipe-3d-side", () => (RisePages.DemoRecipeVm(), null), p =>
            {
                p.Vm.SwapPreviewCommand.Execute(null);
                p.Vm.Preview.OrbitStep(new System.Numerics.Vector2(6, 0));
            }),
            // From behind, where the back owners' colors show (the union rule: the body under a strap).
            ("demo-recipe-3d-back", () => (RisePages.DemoRecipeVm(), null), p =>
            {
                p.Vm.SwapPreviewCommand.Execute(null);
                p.Vm.Preview.OrbitStep(new System.Numerics.Vector2(12, 0));
            }),
            // An asset of a cooked Set, once the book that cooked it has been found, seen from the side
            // and from behind: the page and the Set's models/ agree on its colors.
            ("demo-set-3d-side", () => { var (v, o) = RisePages.DemoSetVm(); return (v, o); }, p =>
            {
                while (!p.Vm.Source.ExportReady.IsCompleted) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(1); }
                Dispatcher.UIThread.RunJobs();
                p.Vm.SwapPreviewCommand.Execute(null);
                p.Vm.Preview.OrbitStep(new System.Numerics.Vector2(6, 0));
            }),
            ("demo-set-3d-back", () => { var (v, o) = RisePages.DemoSetVm(); return (v, o); }, p =>
            {
                while (!p.Vm.Source.ExportReady.IsCompleted) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(1); }
                Dispatcher.UIThread.RunJobs();
                p.Vm.SwapPreviewCommand.Execute(null);
                p.Vm.Preview.OrbitStep(new System.Numerics.Vector2(12, 0));
            }),
            ("picture", () => (RisePages.PictureVm(), null), _ => { }),
            ("sheet", () => (RisePages.SheetVm(), null), _ => { }),
            ("sheet-grid-cells", () => (RisePages.SheetVm(), null), p => p.Vm.SetGridModeCommand.Execute(GridMode.CellSize)),
            ("sheet-grid-auto", () => (RisePages.SheetVm(), null), p => p.Vm.SetGridModeCommand.Execute(GridMode.Auto)),
            ("sheet-grid-problem", () => (RisePages.SheetVm(), null), p => p.Vm.GridA = 5),
            ("set-batch-export", () => { var (v, o) = RisePages.SetVm(); return (v, o); },
                p =>
                {
                    foreach (var item in p.Vm.Items.Take(5)) item.IsChecked = true;
                    p.Vm.ShowExportTabCommand.Execute(null);
                }),
            ("set-sealed-export", () => { var (v, o) = RisePages.SealedSetVm(); return (v, o); },
                p => p.Vm.ShowExportTabCommand.Execute(null)),
            // Make voxel-ready: the button dim on a locked book beside the sentence that says why,
            // and the undo line after a snap (a real snap, written to a temp book, through the page).
            ("voxel-locked", () => (RisePages.VariantVm("ghost", isEditing: () => false), null), _ => { }),
            ("voxel-undo", () =>
            {
                var (s, _) = RiseVoxelReadyTests.Disk(secondGhost: true);
                var vm = new RiseViewModel(RiseFixtures.Variant(s, "ghost"), new ImageBridge(), new FakeNav(),
                    new RiseVoxelReadyTests.ScriptedDialogs { Answer = true }, new FilePickerService(), new StatusService());
                return (vm, null);
            }, p =>
            {
                UiThreadWait.For(p.Vm.MakeVoxelReadyCommand.ExecuteAsync(null));
            }),
            ("swapped", () => (RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), null),
                p => p.Vm.SwapPreviewCommand.Execute(null)),
            ("enlarged", () => (RisePages.VariantVm(oakDepth: RisePages.PaintedOak()), null),
                p => p.Vm.EnlargePreviewCommand.Execute(null)),
        };

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            string theme = variant.Key.ToString()!.ToLowerInvariant();
            foreach (var (name, build, arrange) in states)
            {
                // NFTY_CAPTURE_ONLY=wt- re-shoots one family of states instead of the whole page's gallery.
                if (Environment.GetEnvironmentVariable("NFTY_CAPTURE_ONLY") is { Length: > 0 } only
                    && !name.StartsWith(only, StringComparison.Ordinal)) continue;
                foreach (var (suffix, w, h) in new[]
                {
                    ("", 1180d, 720d),
                    ("-min", RisePages.MinArea.Width, RisePages.MinArea.Height),
                })
                {
                    var (vm, owner) = build();
                    using var page = RisePages.Show(vm, w, h, variant, owner);
                    arrange(page);
                    var model = page.View.FindControl<Views.ModelPreviewView>("ModelView")!;
                    model.RepaintNow();     // the 3D view paints on a posted job; the frame must not beat it
                    Dispatcher.UIThread.RunJobs();

                    var file = $"rise-{name}-{theme}{suffix}.png";
                    using var frame = page.Window.CaptureRenderedFrame()!;
                    VerifyRiseState(name, page, file);
                    frame.Save(Path.Combine(Dir!, file), PngBitmapEncoderOptions.Default);
                }
            }
        }
    }

    /// <summary>
    /// The Rise page over chosen assets of a Set the real CLI cooked, front and back, for an author's
    /// review. Inert unless <c>NFTY_REVIEW_SET</c> names the Set folder; <c>NFTY_REVIEW_BOOK</c> names
    /// the book that cooked it (so faces take owner colors) and <c>NFTY_REVIEW_ASSETS</c> the asset
    /// numbers, comma-separated. Writes <c>chest{i}-front.png</c> and <c>chest{i}-back.png</c>.
    /// </summary>
    [AvaloniaFact]
    public void Capture_demo_review()
    {
        string? setDir = Environment.GetEnvironmentVariable("NFTY_REVIEW_SET");
        if (Dir is null || setDir is null) return;
        string? book = Environment.GetEnvironmentVariable("NFTY_REVIEW_BOOK");
        var numbers = (Environment.GetEnvironmentVariable("NFTY_REVIEW_ASSETS") ?? "1")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            string theme = variant.Key.ToString()!.ToLowerInvariant();
            for (int i = 0; i < numbers.Count; i++)
                foreach (var (side, steps) in new[] { ("front", 0), ("back", 12) })
                {
                    var set = SetReader.Read(setDir);
                    var src = new Nfty.App.Rise.SetRiseSource(set, numbers[i], () => book is null ? Array.Empty<string>() : new[] { book }, new ImageBridge());
                    using var page = RisePages.Show(RisePages.Vm(src), 1180, 720, variant, set);
                    while (!page.Vm.Source.ExportReady.IsCompleted) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(1); }
                    Dispatcher.UIThread.RunJobs();
                    page.Vm.SwapPreviewCommand.Execute(null);
                    if (steps > 0) page.Vm.Preview.OrbitStep(new System.Numerics.Vector2(steps, 0));
                    var model = page.View.FindControl<Views.ModelPreviewView>("ModelView")!;
                    model.RepaintNow();
                    Dispatcher.UIThread.RunJobs();
                    var file = $"chest{i + 1}-{side}{(variant == ThemeVariant.Light ? "" : "-dark")}.png";
                    if (!(page.Vm.PreviewSwapped && page.Vm.Subject.Faces is not null))
                        throw new InvalidOperationException($"{file}: the 3D view is not the big pane, or the book was not found.");
                    using var frame = page.Window.CaptureRenderedFrame()!;
                    frame.Save(Path.Combine(Dir!, file), PngBitmapEncoderOptions.Default);
                }
        }
    }

    private static bool DemoSolidHasDepth(RiseViewModel vm)
    {
        for (int y = 0; y < vm.Surface.Height; y++)
            for (int x = 0; x < vm.Surface.Width; x++)
            {
                var d = vm.Surface.Get(x, y);
                if (d.Front > 0 || d.Back > 0) return true;
            }
        return false;
    }

    /// <summary>Fails the capture if the page is not showing what the file is named for.</summary>
    private static void VerifyRiseState(string state, RisePage page, string file)
    {
        void Need(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException($"{file} was about to be captured but {what}.");
        }
        var vm = page.Vm;
        var view = page.View;
        var model = view.FindControl<Views.ModelPreviewView>("ModelView")!;
        bool Shown(Func<TextBlock, bool> match) =>
            view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Bounds.Width > 0 && match(t));
        var labels = view.FindControl<Controls.DepthLabelLayer>("DepthLabels")!;

        switch (state)
        {
            case "variant-painted":
                Need(vm.Surface.Get(3, 1).Front > 0, "no depth was painted");
                Need(labels.LastLabelCount > 0, "the cell labels did not draw");
                Need(model.RepaintCount > 0, "the 3D tile never painted");
                break;
            case "mid-stroke":
                Need(vm.Surface.Get(3, 3).Back == 1 && vm.Surface.Get(3, 3).Front == 1, "the right-drag did not raise the back along row 3");
                Need(model.HostInteracting, "the stroke is not in progress");
                break;
            case "variant-locked":
                Need(vm.Source.IsLocked && vm.HasUnsavedDepth, "the book is not locked with unsaved depth");
                Need(Shown(t => t.Text is { } s && s.Contains("read-only", StringComparison.Ordinal)), "the footer does not say read-only");
                break;
            case "variant-ghost":
                Need(vm.SelectedItem.Name == "Ghost" && vm.ShowTwoDOnly, "the ghost is not on the canvas");
                Need(Shown(t => t.Text == "2D ONLY"), "the 2D ONLY section is not on screen");
                Need(Shown(t => t.Text == "partial alpha cannot rise"), "the tile ink is not showing");
                Need(vm.ShowVoxelReadyButton && vm.CanMakeVoxelReady && view.GetVisualDescendants().OfType<Button>().Any(b =>
                    b.IsEffectivelyVisible && b.IsEnabled && b.Content as string == "Make voxel-ready…"), "the Make voxel-ready button is not live");
                break;
            case "voxel-locked":
                Need(vm.Source.IsLocked && !vm.CanMakeVoxelReady, "the book is not locked");
                Need(view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && !b.IsEffectivelyEnabled
                    && b.Content as string == "Make voxel-ready…"), "the button is not on screen dim");
                Need(Shown(t => t.Text == "The CookBook is read-only — unlock it in the Explorer."), "the reason line is not on screen");
                break;
            case "voxel-undo":
                Need(vm.ShowVoxelReadyUndo && Shown(t => t.Text == "Voxel-ready: 2 variants snapped"), "the undo line is not on screen");
                Need(view.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && b.Content as string == "Undo"), "the Undo button is missing");
                break;
            case "demo-variant":
                // No label check: at 32 px a cell is too small for numbers, by the page's own rule.
                Need(DemoSolidHasDepth(vm), "the demo variant shows no depth on a solid pixel");
                Need(model.RepaintCount > 0, "the 3D tile never painted");
                break;
            case "demo-recipe":
                Need(vm.IsRecipeRail && vm.LayerRows.Count >= 2, "the layer rail is not listing the rolled layers");
                Need(model.RepaintCount > 0, "the 3D tile never painted");
                break;
            case "demo-recipe-3d" or "demo-recipe-3d-angle" or "demo-recipe-3d-side" or "demo-recipe-3d-back":
                Need(vm.PreviewSwapped && model.RepaintCount > 0, "the 3D view is not the big pane");
                Need(vm.Subject.Faces is not null && vm.Texture.Width == (2 * vm.Subject.Art.Width) + 1, "the preview is not the owner-colored atlas");
                break;
            case "demo-set-3d-side" or "demo-set-3d-back":
                Need(vm.PreviewSwapped && model.RepaintCount > 0, "the 3D view is not the big pane");
                Need(vm.Subject.Faces is not null && vm.Texture.Width == (2 * vm.Subject.Art.Width) + 1, "the book was not found, so there are no owner colors");
                break;
            case "demo-set":
                Need(vm.Source.Kind == Nfty.App.Rise.RiseSourceKind.Set, "the source is not the cooked Set");
                Need(model.RepaintCount > 0, "the 3D tile never painted");
                break;
            case "recipe":
                Need(vm.IsRecipeRail && vm.LayerRows.Count == 2, "the layer rail is not showing the roll");
                Need(Shown(t => t.Text == "combined depth · read-only"), "the read-only pill is not on screen");
                Need(view.GetVisualDescendants().OfType<Button>().Any(b => b.Classes.Contains("sampleroll") && b.IsEffectivelyVisible), "the die is missing");
                break;
            case "wt-picking":
                Need(vm.IsPicking && Shown(t => t.Text == "click a pixel to choose its layer"), "the header does not ask for a pick");
                Need(vm.BelowCanvas is null && vm.AboveCanvas is null, "layers are dimmed before one was chosen");
                break;
            case "wt-targeted" or "wt-painted":
                Need(!vm.IsPicking && Shown(t => t.Text is { } s && s.StartsWith("editing ", StringComparison.Ordinal)
                    && s.Contains(" · in ", StringComparison.Ordinal)), "the header does not name the target and its reach");
                Need(vm.BelowCanvas is not null && vm.AboveCanvas is not null, "the layers around the target are not there to dim");
                Need(view.GetVisualDescendants().OfType<Avalonia.Controls.Image>().Count(i => i.Classes.Contains("dimlayer") && i.IsEffectivelyVisible && i.Source is not null) == 2,
                    "the dimmed layers are not both on the canvas");
                Need(vm.LayerRows.Count(r => r.IsTarget) == 1, "no single row wears the target mark");
                Need(model.RepaintCount > 0 && vm.Mesh is not null, "the 3D tile never painted the character");
                if (state == "wt-painted")
                {
                    Need(vm.HasUnsavedDepth && Shown(t => t.Text is { } s && s.StartsWith("unsaved depth on 1 variant", StringComparison.Ordinal)),
                        "the footer does not say there is unsaved depth");
                    Need(vm.LayerRows.Any(r => r.IsTarget && r.DepthText == "varied"), "the target's row chip did not follow the stroke");
                }
                break;
            case "wt-targeted-3d":
                Need(vm.PreviewSwapped && vm.IsWriteThrough && !vm.IsPicking && vm.Mesh is not null, "the 3D view is not the big one over a chosen layer");
                Need(model.RepaintCount > 0, "the 3D view never painted the whole character");
                break;
            case "wt-locked":
                Need(vm.Source.IsLocked && vm.HasUnsavedDepth, "the book is not locked with unsaved depth");
                Need(Shown(t => t.Text is { } s && s.Contains("read-only", StringComparison.Ordinal)), "the footer does not say read-only");
                break;
            case "picture":
                Need(vm.Source.Kind == Nfty.App.Rise.RiseSourceKind.Picture && vm.Items.Count == 1, "the source is not one loose picture");
                break;
            case "sheet":
                Need(vm.Source.Kind == Nfty.App.Rise.RiseSourceKind.Sheet && vm.IsTileRail && vm.Items.Count == 5, "the source is not a five-cell sheet on the tile rail");
                Need(Shown(t => t.Text == "#5"), "the last cell's tile is not on screen");
                break;
            case "sheet-grid-cells":
                Need(vm.GridMode == GridMode.CellSize && vm.GridA == 16 && vm.GridB == 16, "the row is not in cell-size mode on the 16 px cut");
                break;
            case "sheet-grid-auto":
                Need(vm.GridMode == GridMode.Auto && !vm.GridNumbersEnabled, "the row is not in auto mode");
                break;
            case "sheet-grid-problem":
                Need(vm.GridProblem.Contains("does not divide", StringComparison.Ordinal)
                    && Shown(t => t.Text is { } s && s.Contains("does not divide", StringComparison.Ordinal)), "the problem line is not on screen");
                break;
            case "set-batch-export":
                Need(vm.IsExportTab && vm.IsBatch, "the EXPORT tab is not showing a batch");
                Need(vm.IsTileRail && Shown(t => t.Text == "SHAPE"), "the SHAPE section is not on screen");
                Need(view.GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("ship") && b.IsEffectivelyVisible && b.Bounds.Height > 0), "the manifest is not on screen");
                break;
            case "set-sealed-export":
                Need(vm.IsExportTab && vm.ExportProblem is { } p && p.Contains("sealed", StringComparison.Ordinal), "the sealed refusal is not the manifest's caveat");
                Need(Shown(t => t.Text is { } s && s.Contains("sealed", StringComparison.Ordinal)), "the refusal is not on screen");
                break;
            case "swapped":
                Need(vm.PreviewSwapped && ReferenceEquals(model.Parent, view.FindControl<Border>("BigHost")), "the 3D view is not in the big slot");
                Need(model.RepaintCount > 0, "the big 3D view never painted");
                break;
            case "enlarged":
                Need(vm.PreviewEnlarged && view.FindControl<Border>("ModelTile")!.Bounds.Width >= 208, "the tile is not enlarged");
                break;
            default:
                throw new InvalidOperationException("unknown state " + state);
        }
    }

    /// <summary>A book with a layerOrder entry naming no ingredient — reported by Validator, so the
    /// detail card takes its invalid branch.</summary>
    /// <summary>The book settings card over the two-recipe fixture with a palette, a weight edited
    /// (so the reserved caveat is lit) and, for the riskiest ink, a refusal printed.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="book">Disposed by the caller; the card only reads it.</param>
    /// <param name="locked">Whether the book is locked, so Save is dimmed with its reason.</param>
    /// <param name="refuse">Whether to shelve every recipe and press Save, so the footer prints the
    /// REAL refusal the Validator gives for that edit (zero total weight, introduced by it) over
    /// weights that show the cause. Never a hand-written sentence over contradicting values.</param>
    internal static BookSettingsDialogViewModel BookSettingsCard(IDialogService dialogs,
        out Nfty.Core.Formats.LoadedCookBook book, bool locked, bool refuse)
    {
        var plain = ExplorerViewModelTests.TwoRecipeBook();
        book = new Nfty.Core.Formats.LoadedCookBook
        {
            Manifest = plain.Manifest with
            {
                TargetSupply = 1200,
                Palette = ["hex:d6249f", "hex:3d7f8f", "hex:2b2b2b", "hex:e8c547", "hex:7fb0c4"],
            },
            Recipes = plain.Recipes,
        };
        var live = book;
        var vm = new BookSettingsDialogViewModel(dialogs, book, null, () => !locked,
            edit => System.Threading.Tasks.Task.FromResult(
                Nfty.Core.Formats.Validator.Validate(live).Count == 0
                    && Nfty.Core.Formats.Validator.Validate(edit(live)) is { Count: > 0 } introduced
                    ? new BookSettingsOutcome(false, introduced)
                    : new BookSettingsOutcome(true, [])));
        if (refuse)
        {
            foreach (var w in vm.Weights) w.Weight = 0;
            // The commit above completes synchronously, so this returns having printed the refusal.
            vm.SaveCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        }
        else vm.Weights[0].Weight = vm.Weights[0].SavedWeight + 15;
        return vm;
    }

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
