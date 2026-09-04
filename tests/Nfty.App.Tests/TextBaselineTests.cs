using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Every run of text that shares a visual row must share a baseline.
///
/// <para>The mockups say so directly — explorer.html's <c>.rchip</c>, <c>.rules-h</c> and
/// <c>.cwaxis</c>, help.html's <c>.cs</c>, ingredient-editor.html's <c>.ctl-h</c> are all
/// <c>align-items: baseline</c> — and Avalonia has no baseline alignment at all. A TextBlock in a
/// horizontal StackPanel defaults to <c>Stretch</c>, which draws its line at the TOP of the row, so
/// a 9.5px caption beside a 12.5px name missed by 4.7px and the rule chips read as broken. Center
/// alignment is no better across two font sizes; it only moves where the error lands.</para>
///
/// <para>The rows are found from a laid-out frame rather than from the markup — anything whose
/// vertical extents overlap counts, so a Grid cell is checked as well as a StackPanel child — which
/// is how this caught the ingredient card's kind line, a row nobody had thought of as a row.</para>
///
/// <para>The threshold is 1.25px because about 1px is irreducible: the app pairs a mono face with a
/// sans one in every data table, the two have different descents, and <c>Bottom</c> — the closest
/// Avalonia has to baseline alignment — flushes the boxes, not the baselines. A shared
/// <c>LineHeight</c> was tried and bought 0.1px, which is not worth a size stated twice.</para>
/// </summary>
public class TextBaselineTests
{
    private static readonly List<string> Log = new();

    private static double BaselineY(TextBlock tb, Visual root)
    {
        var y = tb.Padding.Top + tb.TextLayout.Baseline;
        var p = tb.TranslatePoint(new Point(0, y), root);
        return p?.Y ?? double.NaN;
    }

    private static void Sweep(string screen, Control view, double w = 1180, double h = 720)
    {
        var window = new Window { RequestedThemeVariant = ThemeVariant.Dark, Content = view, Width = w, Height = h };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Any container whose direct TextBlock children share a visual row: a horizontal
        // StackPanel, a Grid row, a WrapPanel item. Rows are found from the laid-out frame
        // (overlapping vertical extents) rather than from the markup, so a Grid cell counts too.
        foreach (var panel in view.GetVisualDescendants().OfType<Panel>())
        {
            var kids = panel.Children.OfType<TextBlock>()
                .Where(t => !string.IsNullOrWhiteSpace(t.Text) && t.Bounds.Height > 0).ToList();
            if (kids.Count < 2) continue;

            foreach (var row in GroupIntoRows(kids))
            {
                if (row.Count < 2) continue;
                var ys = row.Select(k => BaselineY(k, window)).ToList();
                double spread = ys.Max() - ys.Min();
                if (spread > Tolerance)
                    Log.Add($"{screen}  BASELINE {spread:0.0}px  " + string.Join(" | ",
                        row.Zip(ys, (k, y) => $"\"{Trunc(k.Text)}\"@{y:0.0} {k.FontSize}px {k.VerticalAlignment}")));
            }
        }

        window.Close();
    }

    /// <summary>Groups controls that share a visual row: their vertical extents overlap by more
    /// than half the shorter one.</summary>
    private static List<List<TextBlock>> GroupIntoRows(List<TextBlock> kids)
    {
        var rows = new List<List<TextBlock>>();
        foreach (var k in kids.OrderBy(k => k.Bounds.Y))
        {
            var row = rows.FirstOrDefault(r => Overlaps(r[0], k));
            if (row is null) rows.Add(new List<TextBlock> { k });
            else row.Add(k);
        }
        return rows;
    }

    private static bool Overlaps(TextBlock a, TextBlock b)
    {
        double top = Math.Max(a.Bounds.Y, b.Bounds.Y);
        double bottom = Math.Min(a.Bounds.Bottom, b.Bounds.Bottom);
        double shorter = Math.Min(a.Bounds.Height, b.Bounds.Height);
        return shorter > 0 && (bottom - top) > shorter / 2;
    }

    private static string Trunc(string? s) =>
        s is null ? "" : s.Length <= 18 ? s : s[..18] + "…";

    /// <summary>Threshold: see the class remarks. Below this is the mono/sans descent difference,
    /// which no alignment mode in Avalonia removes.</summary>
    private const double Tolerance = 1.25;

    [AvaloniaFact]
    public void Text_sharing_a_row_shares_a_baseline()
    {
        Log.Clear();

        var book = ExplorerViewModelTests.TwoRecipeBook();
        Sweep("cookbook-detail", new Views.CookBookDetailView
        { DataContext = new CookBookDetailViewModel(book, () => { }, () => { }) });

        var (ruleBook, ruleRecipe) = VisualCapture.RecipeWithRules();
        using (var vm = new RecipeDetailViewModel(ruleRecipe, ruleBook, new ImageBridge(), _ => { }))
            Sweep("recipe-detail", new Views.RecipeDetailView { DataContext = vm });

        var (dynBook, dynRecipe, dynIng) = VisualCapture.DynamicIngredient();
        using (var vm = new IngredientDetailViewModel(dynIng, dynRecipe, dynBook, new ImageBridge(),
                   () => { }, () => false, null, null, new FilePickerService(), new FakeDialogs()))
            Sweep("ingredient-detail", new Views.IngredientDetailView { DataContext = vm });

        var dialogs = new FakeDialogs();
        Sweep("help", new Views.HelpView { DataContext = new HelpViewModel(dialogs) });

        var nav = new FakeNav();
        var session = new CookBookSession();
        using (var explorer = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
                   new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                   ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
                   ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService()))
            Sweep("explorer", new Views.ExplorerView { DataContext = explorer });

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var generated = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(6, "seed1"));
            SetWriter.Write(generated, dir, pack: false);
            var loaded = SetReader.Read(dir);
            var setVm = new SetBrowserViewModel(loaded);
            setVm.SelectedItem = setVm.Items[0];
            Sweep("set-browser", new Views.SetBrowserView { DataContext = setVm });
            setVm.Dispose();
        }
        finally { Directory.Delete(dir, recursive: true); }

        Assert.True(Log.Count == 0,
            "Text on one row with mismatched baselines:" + Environment.NewLine +
            string.Join(Environment.NewLine, Log));
    }
}
