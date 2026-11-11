using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public partial class VisualCapture
{
    /// <summary>The Recipe and Ingredient panes over the demo book, at the page width they get at
    /// the smallest window and at a wide one: the side rails stop at one shared maximum, and the
    /// share strip's bars line up in their columns at every width.</summary>
    [AvaloniaFact]
    public void Capture_detail_rails_wide()
    {
        if (Dir is null) return;   // inert unless explicitly capturing
        var demoDir = Directory.CreateTempSubdirectory();
        try
        {
            using var demo = CookBookArchive.Read(DemoCookBook.WriteTo(demoDir.FullName));
            var chest = demo.Recipes.First(r => r.Manifest.Id == "chest");
            var body = chest.Ingredients.First(i => i.Manifest.Name == "Body");
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                var key = variant.Key.ToString()!.ToLowerInvariant();
                foreach (var (w, h, tag) in new[] { (900.0, 620.0, "min"), (1600.0, 900.0, "wide") })
                {
                    using (var vm = new RecipeDetailViewModel(chest, demo, new ImageBridge(), _ => { }))
                        Capture(new Views.RecipeDetailView { DataContext = vm }, variant, $"rails-recipe-{tag}-{key}.png", w, h);
                    using (var vm = new IngredientDetailViewModel(body, chest, demo, new ImageBridge(),
                        () => { }, () => false, null, null, new FilePickerService(), new FakeDialogs(), null, _ => { }))
                        Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"rails-ingredient-{tag}-{key}.png", w, h);
                }
            }
        }
        finally { demoDir.Delete(recursive: true); }
    }
}
