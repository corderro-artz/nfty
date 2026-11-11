using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Recipe pane's rules rail and the Ingredient pane's colorways rail stop at ONE maximum width,
/// and the Ingredient hero's share bars start and end together whatever the window. At a wide
/// window the colorways rail used to take a third of the pane while the rules rail stayed at 260,
/// and every share bar sat at its own x behind a right-aligned name.
/// </summary>
public class SideRailWidthTests
{
    private static Border Rail(Control view, string cls) =>
        view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains(cls));

    [AvaloniaFact]
    public void Both_rails_stop_at_one_width_and_the_share_bars_line_up()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            using var demo = CookBookArchive.Read(DemoCookBook.WriteTo(dir.FullName));
            var chest = demo.Recipes.First(r => r.Manifest.Id == "chest");
            var body = chest.Ingredients.First(i => i.Manifest.Name == "Body");
            foreach (var width in new[] { 1600.0, 2400.0 })
            {
                using var rvm = new RecipeDetailViewModel(chest, demo, new ImageBridge(), _ => { });
                var recipe = new Views.RecipeDetailView { DataContext = rvm };
                var w1 = new Window { Width = width, Height = 800, Content = recipe };
                w1.Show(); Dispatcher.UIThread.RunJobs();
                double rules = Rail(recipe, "rules-panel").Bounds.Width;
                w1.Close();

                using var ivm = new IngredientDetailViewModel(body, chest, demo, new ImageBridge(),
                    () => { }, () => false, null, null, new FilePickerService(), new FakeDialogs(), null, _ => { });
                var ing = new Views.IngredientDetailView { DataContext = ivm };
                var w2 = new Window { Width = width, Height = 800, Content = ing };
                w2.Show(); Dispatcher.UIThread.RunJobs();
                double colorways = Rail(ing, "cw-panel").Bounds.Width;

                Assert.Equal(300, colorways, 0.5);
                Assert.Equal(colorways, rules, 0.5);

                // Every share track in one column starts at one x and is one width.
                var tracks = ing.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("rt")).ToArray();
                Assert.Equal(4, tracks.Length);
                var lefts = tracks.Select(t => t.TranslatePoint(default(Avalonia.Point), ing)!.Value.X).OrderBy(x => x).ToArray();
                Assert.Equal(lefts[0], lefts[1], 0.5);
                Assert.Equal(lefts[2], lefts[3], 0.5);
                Assert.All(tracks, t => Assert.Equal(tracks[0].Bounds.Width, t.Bounds.Width, 0.5));
                Assert.True(tracks[0].Bounds.Width > 200, $"track {tracks[0].Bounds.Width} does not grow with the pane");
                w2.Close();
            }
        }
        finally { dir.Delete(recursive: true); }
    }
}
