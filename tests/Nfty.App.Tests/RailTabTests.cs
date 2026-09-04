using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Ingredient editor's rail is two panels behind one tab bar.
/// </summary>
/// <remarks>
/// Together they overflowed the rail, and the half that lost was the references — the sibling list
/// and the Kitchen section sat below the fold with nothing on screen saying they were there. They
/// are also two different jobs: one sets what the layer looks like, the other what you are looking
/// at it against.
/// </remarks>
public class RailTabTests
{
    // A recipe that actually stacks something, so the References half has rows to lay out. The
    // single-layer fixture would let every assertion here pass on an empty panel.
    private static (Window window, IngredientEditorViewModel vm, Views.IngredientEditorView view) Render()
    {
        var (book, recipe, _) = IngredientEditorReferencesTests.FourLayerStack();
        // The DYNAMIC layer of that stack: the fixture hands back a Static one, whose Colorize half
        // has no range or quantize section at all, so half of what this sweeps would not be there.
        var edited = recipe.Ingredients.Single(i => i.Manifest.Id == "body");
        var vm = IngredientEditorReferencesTests.Editor(edited, recipe, book);
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    private static Button Tab(Views.IngredientEditorView view, string label) =>
        view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("railtab"))
            .Single(b => (b.Content as string) == label
                         || b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label));

    /// <summary>Each tab really does swap the panel under it — neither half is laid out at once.</summary>
    [AvaloniaFact]
    public void Each_tab_shows_its_own_half_and_only_that_half()
    {
        var (window, vm, view) = Render();
        try
        {
            // IsEffectivelyVisible, not Bounds: Avalonia does not reset a control's arranged bounds
            // when an ancestor goes invisible, so a section that has been shown once keeps reporting
            // the size it had - which would make the second half of this test pass either way.
            bool Shown(string label) => view.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.Text == label && t.IsEffectivelyVisible);
            bool ColorizeShown() => Shown("QUANTIZE");
            bool ReferencesShown() => Shown("IN THIS RECIPE");

            Assert.True(vm.IsColorizeTab);                 // opens on colorize
            Assert.True(ColorizeShown());
            Assert.False(ReferencesShown());

            vm.ShowReferencesTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.IsReferencesTab);
            Assert.True(ReferencesShown());
            Assert.False(ColorizeShown());
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// GEOMETRY IS FIXED: switching tabs moves neither tab.
    /// </summary>
    /// <remarks>Each tab carries its 2px underline in both states and only the ink changes, so the
    /// row cannot reflow under the pointer that just clicked it.</remarks>
    [AvaloniaFact]
    public void Switching_tabs_moves_neither_tab()
    {
        var (window, vm, view) = Render();
        try
        {
            var before = new[] { Tab(view, "COLORIZE").Bounds, Tab(view, "REFERENCES").Bounds };

            vm.ShowReferencesTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var after = new[] { Tab(view, "COLORIZE").Bounds, Tab(view, "REFERENCES").Bounds };
            Assert.Equal(before[0], after[0]);
            Assert.Equal(before[1], after[1]);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>Only the active tab is underlined, and it is the accent that draws it.</summary>
    [AvaloniaFact]
    public void Only_the_active_tab_is_underlined()
    {
        var (window, vm, view) = Render();
        try
        {
            Application.Current!.TryGetResource("AccentBrush", window.ActualThemeVariant, out var a);
            var accent = (IBrush)a!;

            Assert.Equal(accent, Tab(view, "COLORIZE").BorderBrush);
            Assert.Equal(Brushes.Transparent, Tab(view, "REFERENCES").BorderBrush);

            vm.ShowReferencesTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Brushes.Transparent, Tab(view, "COLORIZE").BorderBrush);
            Assert.Equal(accent, Tab(view, "REFERENCES").BorderBrush);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The badge counts the same thing the panel's own header does — it is the one fact
    /// about the other half worth knowing without going there.</summary>
    [AvaloniaFact]
    public void The_badge_counts_what_the_reference_header_counts()
    {
        var (window, vm, view) = Render();
        try
        {
            int total = vm.Siblings.Count + vm.KitchenLayers.Count;
            Assert.True(total > 0, "the fixture stacks nothing, so this would pass vacuously");

            Assert.Equal($"0/{total}", vm.ReferenceBadgeText);
            Assert.Equal($"0 / {total} on", vm.ReferenceCountText);

            vm.ToggleReferenceCommand.Execute(vm.Siblings[0]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal($"1/{total}", vm.ReferenceBadgeText);
            Assert.StartsWith($"1 / {total}", vm.ReferenceCountText);
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
