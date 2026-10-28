using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Ingredient hero's share strip printed "15.38%" in a 34px cell and drew "15.38": a 12-variant
/// layer put four-digit percentages in a column sized for two. A clipped control still reports its
/// asked width, so the assertion compares what the text WANTS against what the cell GOT.
/// </summary>
public class HeroShareFigureTests
{
    [AvaloniaFact]
    public void Every_percentage_in_the_hero_strip_fits_its_cell()
    {
        using var book = VisualCapture.NamedBook("Twelve", "Cat", "Aura", 12);
        var recipe = book.Recipes[0];
        using var vm = new IngredientDetailViewModel(recipe.Ingredients[0], recipe, book, new ImageBridge(),
            () => { }, () => false, null, null, new FilePickerService(), new FakeDialogs(), null, _ => { });
        var window = new Window
        {
            Content = new Views.IngredientDetailView { DataContext = vm }, Width = 1180, Height = 720,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var figures = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("cshare") && t.Text is { } s && s.EndsWith('%')).ToList();
        Assert.True(figures.Count >= 4, "the hero strip drew fewer than four figures");
        foreach (var t in figures)
        {
            t.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(t.Bounds.Width >= t.DesiredSize.Width - 0.5,
                $"'{t.Text}' wants {t.DesiredSize.Width:0.#}px and its cell gave {t.Bounds.Width:0.#}px");
        }
    }
}
