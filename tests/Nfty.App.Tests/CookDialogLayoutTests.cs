using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// V-10: the cook card is one height in every state of its 3D-models row. The note under the box
/// wrapped to two lines for a 2D-only book and one for a clean one, so the card grew and shrank by
/// ~15px as the book's scan answered or the box was ticked. The note's two lines are reserved.
/// </summary>
public class CookDialogLayoutTests
{
    [AvaloniaFact]
    public void The_card_is_the_same_height_in_every_models_state()
    {
        var dialogs = new FakeDialogs();
        using var clean = CookDialogModelsTests.Book();
        using var flat = CookDialogModelsTests.Book(variants: 3, alpha: 128);

        CookDialogViewModel Vm(Nfty.Core.Formats.LoadedCookBook b) =>
            new(b, new FilePickerService(), new NoopFolderRevealer(), dialogs) { Count = 500, Seed = "vapor" };

        var states = new List<(string Name, CookDialogViewModel Vm)>
        {
            ("collapsed", Vm(clean)),
            ("2d-only", Vm(flat)),
        };
        var ticked = Vm(clean);
        ticked.IncludeModels = true;
        states.Add(("ticked, options closed", ticked));

        var heights = new List<(string Name, double Height)>();
        foreach (var (name, vm) in states)
        {
            var view = new Views.CookDialogView { DataContext = vm };
            var area = RisePages.MinArea;
            var window = new Window { Content = view, Width = area.Width, Height = area.Height };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            heights.Add((name, card.Bounds.Height));
            window.Close();
        }

        Assert.True(heights.All(h => System.Math.Abs(h.Height - heights[0].Height) < 0.5),
            "the cook card changes height with the models state: "
            + string.Join(", ", heights.Select(h => $"{h.Name} {h.Height:F1}")));
    }
}
