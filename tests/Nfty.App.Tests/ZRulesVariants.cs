using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Throwaway: renders the three RULES-panel variants for a look.</summary>
public class ZRulesVariants
{
    private static string? Dir => System.Environment.GetEnvironmentVariable("NFTY_VARIANT_DIR");

    private static (LoadedCookBook book, LoadedRecipe recipe) Fixture(int ruleCount)
    {
        var all = new[]
        {
            new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "day"),
                new[] { new RuleTarget("aura", "none") }),
            new IncompatibilityRule(RuleType.Require, new RuleTarget("bg", "night"),
                new[] { new RuleTarget("aura", "glow") }),
        };
        LoadedIngredient Ing(string id, params string[] vs) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                vs.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = vs.ToDictionary(v => v, _ => new Image<Rgba32>(4, 4)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "aura" }, all.Take(ruleCount).ToArray()),
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

    private static void Shot(int ruleCount, string variant, ThemeVariant theme, string file)
    {
        var (book, recipe) = Fixture(ruleCount);
        using var vm = new RecipeDetailViewModel(recipe, book, new ImageBridge(), _ => { });
        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window { RequestedThemeVariant = theme, Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("rules-panel"));

        switch (variant)
        {
            case "A":   // shrink to content
                panel.VerticalAlignment = VerticalAlignment.Top;
                break;
            case "B":   // shrink to content, with a floor
                panel.VerticalAlignment = VerticalAlignment.Top;
                panel.MinHeight = 220;
                break;
            case "C":   // full height, ground only, no outline
                panel.BorderThickness = new Thickness(0);
                break;
        }

        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, file), PngBitmapEncoderOptions.Default);
        window.Close();
        book.Dispose();
    }

    /// <summary>Throwaway.</summary>
    [AvaloniaFact]
    public void Render()
    {
        if (Dir is null) return;
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var k = theme.Key.ToString()!.ToLowerInvariant();
            foreach (var v in new[] { "current", "A", "B", "C" })
            {
                Shot(2, v, theme, $"two-{v}-{k}.png");
                Shot(1, v, theme, $"one-{v}-{k}.png");
            }
        }
    }
}
