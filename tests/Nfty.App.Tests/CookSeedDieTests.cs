using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The cook dialog's seed: random by default, rerolled by the die beside it, or typed by hand.
/// </summary>
public class CookSeedDieTests
{
    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }
    private sealed class NoReveal : IFolderRevealer { public void Reveal(string p) { } }

    private static CookDialogViewModel Vm() =>
        new(ExplorerViewModelTests.TwoRecipeBook(), new NoPicker(), new NoReveal(), new FakeDialogs());

    [AvaloniaFact]
    public void Clicking_the_die_rolls_a_new_seed_and_a_new_face()
    {
        var vm = Vm();
        var view = new Views.CookDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var die = view.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "SeedDie");
            for (int i = 0; i < 20; i++)
            {
                string seed = vm.Seed;
                int face = vm.SeedDieFace;
                var centre = die.TranslatePoint(new Avalonia.Point(die.Bounds.Width / 2, die.Bounds.Height / 2), window)!.Value;
                window.MouseDown(centre, MouseButton.Left);
                window.MouseUp(centre, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.NotEqual(seed, vm.Seed);
                Assert.NotEqual(face, vm.SeedDieFace);
                Assert.InRange(vm.SeedDieFace, 1, 6);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_typed_seed_is_the_seed()
    {
        var vm = Vm();
        var view = new Views.CookDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var box = view.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "SeedBox");
            box.Text = "vapor";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("vapor", vm.Seed);
        }
        finally { window.Close(); }
    }
}
