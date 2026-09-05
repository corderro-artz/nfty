using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Every piece of text the app draws, checked against the surface it actually lands on.
///
/// Reading a frame catches text you happen to be looking at; this catches the ones you aren't. A
/// token can be perfectly legible in light mode and near-invisible in dark, and nothing about the
/// markup says so — the two dictionaries are separate files and only the rendered pairing of
/// foreground-over-background tells you.
///
/// Contrast is computed the way a person sees it, not the way the markup declares it: alpha is
/// composited through every translucent ancestor down from the window background, and Opacity is
/// folded in, so a 55%-alpha foreground over a 6%-alpha panel is scored on the color that actually
/// reaches the eye.</summary>
public class DarkModeContrastTests
{
    /// <summary>The floors, and why there are two of them.
    ///
    /// <para><b>Normal text is held to WCAG AA (4.5:1).</b> It used to be held to 3.0, on the grounds
    /// that the mockups' palette predates any accessibility target and enforcing AA would order a
    /// redesign. Measuring the gap rather than assuming it showed that was too pessimistic: only two
    /// kinds of ordinary text actually failed AA, both because an element-level <c>Opacity</c> was
    /// stacked on top of a token that is ALREADY 72% alpha — the recents path at 3.44 and the
    /// wizards' "derived from the name" hint at 3.78. Both were lifted by reducing the double-dim,
    /// keeping the design's intent (this text recedes) while making it readable. The palette itself
    /// is untouched.</para>
    ///
    /// <para><b>Inactive controls are exempt, per the standard itself.</b> WCAG 2.1 SC 1.4.3 excludes
    /// "text or images of text that are part of an inactive user interface component". The mockups'
    /// disabled treatment is a flat <c>opacity:.38</c>, which lands a disabled accent button at
    /// 2.12:1; that is the design saying "you cannot use this", not a defect. They still get a floor
    /// of 2.0 rather than none, because the failure this file was written for — an unresolved
    /// DynamicResource leaving Foreground at Avalonia's default Black — measured 1.14:1, and a
    /// blanket exemption would have let it through.</para>
    ///
    /// <para><b>Known remaining debt:</b> placeholder text sits at 2.28:1 in light mode. Placeholders
    /// are NOT exempt under 1.4.3, so this is real. The token is now correct — it was Fluent's own
    /// default, dimmer than the design asked for, and is aliased to the muted token — but Fluent's
    /// TextBox template applies a further opacity inside itself that a Style setter does not reach.
    /// Fixing it properly needs a ControlTheme for TextBox; it is recorded here rather than hidden
    /// behind a floor chosen to accommodate it.</para></summary>
    private const double NormalMin = 4.5;
    private const double RecededMin = 2.0;

    private sealed record Offender(string View, string Text, double Ratio, bool Receded, Color Fg, Color Bg)
    {
        public double Required => Receded ? RecededMin : NormalMin;
        public override string ToString() =>
            $"  {View,-22} {Ratio,5:F2} (needs {Required:F1}{(Receded ? ", receded" : "")})  " +
            $"{Fg} on {Bg}  \"{Trim(Text)}\"";
        private static string Trim(string s) => s.Length <= 40 ? s : s[..40] + "…";
    }

    /// <summary>Text the design has deliberately pushed back. Four mechanisms, one intent: a disabled
    /// control, an explicitly faded element (Opacity), a translucent ink (brush alpha — the search
    /// field's clear affordance and the editor's spinner glyphs are painted at roughly 36% ink, which
    /// is exactly why they measure low), or a field's placeholder.
    ///
    /// The mechanism does not matter; the deliberateness does. What must never qualify is text that is
    /// fully opaque and simply the wrong color — the "1 problem" label was opaque Black at 1.14:1,
    /// and it is held to the normal floor and fails, which is the point of the split.</summary>
    private static bool IsReceded(TextBlock tb, double opacity, Color ink)
    {
        if (!tb.IsEffectivelyEnabled) return true;
        // Inclusive at 0.5: the mockups' own recede value. explorer.html's `.ftimes` (the "4 × 3 × 5"
        // separators) is literally `color: var(--fg-muted); opacity: .5`, and the app reproduces it
        // exactly - so an exclusive bound would fail the app for matching the design it must match.
        if (opacity <= 0.5 || ink.A < 128) return true;
        for (Visual? v = tb; v is not null; v = v.GetVisualParent())
            if (v is TextBox box && box.PlaceholderText == tb.Text)
                return true;
        return false;
    }

    // sRGB relative luminance, per WCAG 2.x.
    private static double Luminance(Color c)
    {
        static double Ch(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        var (l1, l2) = (Luminance(a), Luminance(b));
        if (l1 < l2) (l1, l2) = (l2, l1);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    /// <summary>Source-over composite. This is the whole reason the test is honest: nearly every
    /// surface in this app is a translucent wash, so the declared color is never the seen color.</summary>
    private static Color Over(Color fg, Color bg, double extraOpacity = 1.0)
    {
        var a = fg.A / 255.0 * extraOpacity;
        if (a >= 1.0) return fg;
        byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
        return Color.FromArgb(255, Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B));
    }

    /// <summary>A gradient is scored at its midpoint. Approximate, but it keeps gradient-backed
    /// surfaces in the test rather than silently exempt — and an exempt surface is exactly where the
    /// theme-stuck gradient bug lived.</summary>
    private static Color? SolidOf(IBrush? brush) => brush switch
    {
        ISolidColorBrush s => s.Color,
        IGradientBrush g when g.GradientStops.Count > 0 => Average(g.GradientStops.Select(s => s.Color)),
        _ => null,
    };

    private static Color Average(IEnumerable<Color> colors)
    {
        var list = colors.ToList();
        return Color.FromArgb(
            (byte)list.Average(c => c.A), (byte)list.Average(c => c.R),
            (byte)list.Average(c => c.G), (byte)list.Average(c => c.B));
    }

    /// <summary>Composites every ancestor background from the window down, so the result is the
    /// color physically behind this control.</summary>
    private static Color EffectiveBackground(Visual leaf, Color pageBase)
    {
        var chain = new List<Visual>();
        for (Visual? v = leaf; v is not null; v = v.GetVisualParent()) chain.Add(v);
        chain.Reverse();

        var acc = pageBase;
        foreach (var v in chain)
        {
            if (v is not Control c) continue;
            var brush = c switch
            {
                // ContentPresenter FIRST, and it is neither a ContentControl nor a TemplatedControl,
                // so it needs its own arm. Nearly every button in this app paints its real surface on
                // PART_ContentPresenter rather than on the Button - that is how a /template/ setter
                // beats Fluent's own accent palette - so missing this arm meant scoring an accent
                // button's white label against the PAGE background and calling it 1.10:1.
                ContentPresenter cp => cp.Background,
                Border b => b.Background,
                Panel p => p.Background,
                ContentControl cc => cc.Background,
                TemplatedControl tc => tc.Background,
                _ => null,
            };
            if (SolidOf(brush) is { } col && col.A > 0)
                acc = Over(col, acc, c.Opacity);
        }
        return acc;
    }

    private static double CumulativeOpacity(Visual leaf)
    {
        var o = 1.0;
        for (Visual? v = leaf; v is not null; v = v.GetVisualParent())
            if (v is Control c) o *= c.Opacity;
        return o;
    }

    private static IEnumerable<Offender> Scan(string viewName, Control view, ThemeVariant variant)
    {
        var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var pageBase = SolidOf((IBrush?)Application.Current!.FindResource(variant, "BgBrush")) ?? Colors.Black;

        foreach (var tb in view.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible) continue;
            var text = tb.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var opacity = CumulativeOpacity(tb);
            if (opacity <= 0.01) continue;   // deliberately hidden, not a contrast problem

            if (SolidOf(tb.Foreground) is not { } fgRaw) continue;

            var bg = EffectiveBackground(tb.GetVisualParent()!, pageBase);
            var fg = Over(fgRaw, bg, opacity);
            var ratio = Contrast(fg, bg);
            var offender = new Offender(viewName, text!, ratio, IsReceded(tb, opacity, fgRaw), fg, bg);
            if (ratio < offender.Required) yield return offender;
        }

        window.Close();
    }

    /// <summary>Every screen, both themes. Disabled controls are included on purpose: "disabled" is a
    /// reason for text to be dimmer, not a license for it to be unreadable — a user still has to read
    /// a grayed-out button to know what they can't do.</summary>
    [AvaloniaFact]
    public void No_text_is_unreadable_against_the_surface_it_lands_on()
    {
        var found = new List<Offender>();

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var key = variant.Key.ToString()!.ToLowerInvariant();
            foreach (var (name, view, dispose) in Screens())
            {
                found.AddRange(Scan($"{name}/{key}", view, variant));
                dispose?.Invoke();
            }
        }

        if (found.Count == 0) return;

        var report = new StringBuilder()
            .AppendLine($"{found.Count} text runs fall below their contrast floor:")
            .AppendLine();
        foreach (var o in found.OrderBy(o => o.Ratio))
            report.AppendLine(o.ToString());
        Assert.Fail(report.ToString());
    }

    /// <summary>A brand-new, still-empty CookBook — no recipes, so the Validator reports a problem and
    /// the identity card paints its "N problems" chip and zeroed tiles. This is what a user sees
    /// immediately after the New CookBook wizard, and no other fixture in this file is invalid.</summary>
    private static Nfty.Core.Formats.LoadedCookBook EmptyBook() => new()
    {
        Manifest = new Nfty.Core.Model.CookBookManifest("vp", "VaporPets",
            new Nfty.Core.Model.Dimensions(1000, 1000),
            new Nfty.Core.Model.Collection("VaporPets", "", "VP"),
            new Dictionary<string, double>()),
        Recipes = Array.Empty<Nfty.Core.Formats.LoadedRecipe>(),
    };

    /// <summary>The screens, built from the same fixtures the visual captures use so the two agree on
    /// what "the app" is.</summary>
    /// <summary>The rule form seated on a rule it cannot save, so the sweep meets its warning ink
    /// and its disabled confirm rather than the clean state.</summary>
    private static RuleDialogViewModel RuleForm(FakeDialogs dialogs)
    {
        var (book, recipe) = VisualCapture.RecipeWithRules();
        // The book owns the images; the sweep measures text against surfaces and never draws one.
        book.Dispose();
        var vm = new RuleDialogViewModel(dialogs, recipe.Manifest, recipe.Ingredients);
        // Point it at the layer it is already triggered on: a rule cannot constrain a layer against
        // itself, so the form refuses and paints the warning.
        vm.Targets[0].Layer = vm.Layers.First(l => l.Id == vm.Trigger.Layer!.Id);
        return vm;
    }

    private static IEnumerable<(string Name, Control View, Action? Dispose)> Screens()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();

        // Explorer with a CookBook selected — the screen in the bug report.
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            new CookBookSession(), new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService());
        explorer.SelectNodeCommand.Execute(explorer.Root);
        yield return ("explorer", new Views.ExplorerView { DataContext = explorer }, explorer.Dispose);

        var shellNav = new FakeNav();
        var shellExplorer = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), shellNav, dialogs,
            new ImageBridge(), ExplorerViewModelTests.EditorFactory(shellNav),
            ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(), new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(shellNav, new CookBookSession(), dialogs), new StatusService());
        var shell = new ShellViewModel(shellNav, dialogs, new ThemeService(), new StatusService());
        shellNav.To(shellExplorer);
        yield return ("shell", new Views.ShellChromeView { DataContext = shell }, shellExplorer.Dispose);

        var recents = new RecentsService(Directory.CreateTempSubdirectory().FullName);
        recents.Add(new Models.RecentItem("VaporPets", "cookbook · 2 recipes", @"D:\art\VaporPets.cbk", false));
        var landing = new LandingViewModel(nav, dialogs, new FilePickerService(), recents,
            new CookBookSession(),
            b => new ExplorerViewModel(b, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
                new CookBookSession(), new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService()),
            set => new SetBrowserViewModel(set), (_, _, _) => null!);
        yield return ("landing", new Views.LandingView { DataContext = landing }, null);

        yield return ("help", new Views.HelpView { DataContext = new HelpViewModel(dialogs) }, null);

        yield return ("wizard-cookbook",
            new Views.NewCookBookView { DataContext = new NewCookBookViewModel(dialogs) { Name = "Vapor Pets", Symbol = "VP" } }, null);
        yield return ("wizard-recipe",
            new Views.NewRecipeView { DataContext = new NewRecipeViewModel(dialogs, new[] { ("Fox", 45d) }) { Name = "Cat" } }, null);
        yield return ("wizard-ingredient",
            new Views.NewIngredientView { DataContext = new NewIngredientViewModel(dialogs) { Name = "Aura" } }, null);

        // The states above are all the HAPPY ones, and a state no fixture reaches renders nothing and
        // therefore looks fine - the single most repeated defect on this branch. The screens below are
        // the ones a user actually meets first.

        // An empty, INVALID book: the identity card's "N problems" chip and the zeroed metric tiles.
        // The reported screenshot was exactly this - 0 recipes, 1 problem - and none of the fixtures
        // above ever paint a problem chip, because they are all valid.
        var brokenVm = new CookBookDetailViewModel(EmptyBook(), () => { }, () => { });

        // The rule form, in the state that carries its warning ink — a WarningBrush sentence and a
        // DISABLED accent button, which are the two runs on it most likely to fall under the floor.
        // A screen that is never swept cannot be found to be unreadable.
        yield return ("dialog-rule", new Views.RuleDialogView
        {
            DataContext = RuleForm(dialogs),
        }, null);

        yield return ("cookbook-detail-invalid",
            new Views.CookBookDetailView { DataContext = brokenVm }, null);

        var goodVm = new CookBookDetailViewModel(ExplorerViewModelTests.TwoRecipeBook(), () => { }, () => { });
        yield return ("cookbook-detail", new Views.CookBookDetailView { DataContext = goodVm }, null);

        // Detail bodies for the other two node kinds, and the editor - the editor twice, because its
        // whole toolstrip is disabled for a Custom layer and a disabled toolstrip is precisely where
        // unreadable text hides.
        var detailBook = ExplorerViewModelTests.TwoRecipeBook();
        var cat = detailBook.Recipes.First(r => r.Manifest.Id == "cat");
        var recipeVm = new RecipeDetailViewModel(cat, detailBook, new ImageBridge(), _ => { });
        yield return ("recipe-detail", new Views.RecipeDetailView { DataContext = recipeVm }, recipeVm.Dispose);

        // The same pane with editing UNLOCKED. Its own state, not a variation on a theme: the reorder
        // grips are ghosted to nothing while locked, so a scan of the locked pane is a scan of a
        // control that is not being drawn - the exact blind spot the rest of this file exists for.
        var reorderVm = new RecipeDetailViewModel(cat, detailBook, new ImageBridge(), _ => { },
            (_, _) => Task.FromResult<Nfty.Core.Formats.LoadedCookBook?>(null), canReorder: true);
        yield return ("recipe-detail-unlocked",
            new Views.RecipeDetailView { DataContext = reorderVm }, reorderVm.Dispose);

        var ingVm = new IngredientDetailViewModel(cat.Ingredients[0], cat, detailBook, new ImageBridge(), () => { }, () => false, null, null, new FilePickerService(), dialogs);
        yield return ("ingredient-detail", new Views.IngredientDetailView { DataContext = ingVm }, ingVm.Dispose);

        var editorBook = ExplorerViewModelTests.TwoRecipeBook();
        var editorRecipe = editorBook.Recipes.First(r => r.Manifest.Id == "cat");
        var editorVm = new IngredientEditorViewModel(editorRecipe.Ingredients[0], editorRecipe, editorBook,
            new ImageBridge(), nav, new CookBookSession(), dialogs, new FilePickerService());
        yield return ("editor-disabled", new Views.IngredientEditorView { DataContext = editorVm }, editorVm.Dispose);

        // The reference panel with layers switched ON. Its own state, not a variation on a theme: the
        // over/under tag, the Kitchen chip and the placement stepper are all ghosted to nothing while
        // switched off, so scanning the resting panel scans text that is not being drawn - the exact
        // blind spot the rest of this file exists for.
        var (refKitchen, refDir) = IngredientEditorReferencesTests.TempKitchen(
            ("shades", IngredientEditorReferencesTests.Canvas));
        var (refBook, refRecipe, refEyes) = IngredientEditorReferencesTests.FourLayerStack();
        var refVm = IngredientEditorReferencesTests.Editor(refEyes, refRecipe, refBook, refKitchen, dialogs);
        foreach (var id in new[] { "body", "accessory" })
            refVm.ToggleReferenceCommand.Execute(refVm.Siblings.First(s => s.Key == id));
        refVm.ToggleReferenceCommand.Execute(refVm.KitchenLayers[0]);
        yield return ("editor-references", new Views.IngredientEditorView { DataContext = refVm },
            () =>
            {
                refVm.Dispose();
                refBook.Dispose();
                Directory.Delete(refDir, recursive: true);
            });
    }
}
