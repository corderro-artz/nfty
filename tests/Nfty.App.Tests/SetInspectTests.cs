using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Set browser's click-to-inspect path, the inspector's own rules, and the Save that has to act
/// on whatever is selected right now.
/// </summary>
public class SetInspectTests
{
    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(3, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    private sealed class Saver : IFilePickerService
    {
        public string? Target;
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult(Target);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Clicking a tile opens the inspector on that asset — the whole point of the feature, and the
    /// half a Button.Click handler can silently fail to do.
    /// </summary>
    /// <remarks>
    /// The grid is an ItemsControl of rows of tiles, so the click is caught by one bubbled handler
    /// on the view rather than a command per tile. That handler pattern-matches the Button's
    /// DataContext, which means a change to the tile template can stop it matching without breaking
    /// the build or the layout. Driving the real app is how that was found the first time; this is
    /// how it stays found.
    /// </remarks>
    [AvaloniaFact]
    public void Clicking_a_tile_selects_it_and_opens_the_inspector_on_it()
    {
        var loaded = CookedSet(out var dir);
        var dialogs = new DialogService();
        using var vm = new SetBrowserViewModel(loaded, new FilePickerService(), dialogs, new StatusService());
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var target = vm.Items[^1];
            var button = view.GetVisualDescendants().OfType<Button>()
                .First(b => ReferenceEquals(b.DataContext, target));

            // The real gesture: the bubbled Click handler, not the command called directly. Calling
            // the command would pass with the handler deleted.
            button.Command?.Execute(button.CommandParameter);
            var e = new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent) { Source = button };
            button.RaiseEvent(e);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(target, vm.SelectedItem);
            var inspector = Assert.IsType<SetInspectViewModel>(dialogs.Active);
            Assert.Equal($"#{target.Number:D4}", inspector.Number);
        }
        finally { window.Close(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The inspector opens at Fit, and Fit is where panning is off.</summary>
    /// <remarks>Opening at 1:1 would show a small asset as a postage stamp and a large one cropped;
    /// and a drag at Fit could only make a centered image drift, which is the exact sloppiness the
    /// design rules out.</remarks>
    [AvaloniaFact]
    public void The_inspector_opens_fitted_and_cannot_be_panned_there()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(vm.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());

            Assert.Equal(1.0, ins.Scale);
            Assert.False(ins.CanPan);

            ins.ZoomInCommand.Execute(null);
            Assert.True(ins.Scale > 1.0);
            Assert.True(ins.CanPan);

            // ...and coming back to Fit re-centers, rather than leaving the last offset behind.
            ins.PanX = 40;
            ins.FitCommand.Execute(null);
            Assert.Equal(1.0, ins.Scale);
            Assert.Equal(0, ins.PanX);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Zoom cannot leave its range however many times the button is pressed.</summary>
    [AvaloniaFact]
    public void Zoom_is_clamped_to_its_own_range()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(vm.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());

            for (var i = 0; i < 40; i++) ins.ZoomInCommand.Execute(null);
            Assert.Equal(SetInspectViewModel.MaxScale, ins.Scale);

            for (var i = 0; i < 40; i++) ins.ZoomOutCommand.Execute(null);
            Assert.Equal(SetInspectViewModel.MinScale, ins.Scale);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Stepping moves to the neighbouring asset and stops at both ends.</summary>
    [AvaloniaFact]
    public void Stepping_walks_the_set_and_stops_at_its_ends()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(vm.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());

            Assert.False(ins.PreviousCommand.CanExecute(null));
            ins.NextCommand.Execute(null);
            Assert.Equal($"#{vm.Items[1].Number:D4}", ins.Number);
            Assert.True(ins.PreviousCommand.CanExecute(null));

            while (ins.NextCommand.CanExecute(null)) ins.NextCommand.Execute(null);
            Assert.Equal($"#{vm.Items[^1].Number:D4}", ins.Number);
            Assert.False(ins.NextCommand.CanExecute(null));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>A new asset arrives fitted, not at the previous one's zoom.</summary>
    /// <remarks>Carrying the zoom would land the next image off-center at 800% with no clue where
    /// it went.</remarks>
    [AvaloniaFact]
    public void Stepping_resets_the_view()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(vm.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());
            ins.ZoomInCommand.Execute(null);
            ins.PanX = 25;

            ins.NextCommand.Execute(null);

            Assert.Equal(1.0, ins.Scale);
            Assert.Equal(0, ins.PanX);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Save writes the asset that is selected NOW, not the one that was selected when the rail was
    /// first drawn.
    /// </summary>
    /// <remarks>The button is fixed at the rail's foot and never changes, so the one thing that must
    /// change under it is which file it writes.</remarks>
    [AvaloniaFact]
    public async Task Save_writes_the_currently_selected_asset()
    {
        var loaded = CookedSet(out var dir);
        var saver = new Saver();
        var status = new StatusService();
        using var vm = new SetBrowserViewModel(loaded, saver, new DialogService(), status);
        try
        {
            var target = Path.Combine(dir, "out.png");
            saver.Target = target;

            vm.SelectedItem = vm.Items[^1];
            await vm.SaveImageCommand.ExecuteAsync(null);

            Assert.True(File.Exists(target));
            Assert.Equal(File.ReadAllBytes(vm.Items[^1].ImagePath), File.ReadAllBytes(target));
            Assert.Contains(vm.SelectedNumber, status.Last!, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The DNA splits into two rows that are the same length and lose nothing.</summary>
    /// <remarks>A SHA-256 is 64 hex characters, so the halves are 32 apiece — which is what lets the
    /// rail center them and have both edges line up. The split is computed rather than hard-coded,
    /// so this also pins that a hash of some other length still round-trips.</remarks>
    [AvaloniaFact]
    public void The_dna_splits_into_two_equal_rows_that_rejoin()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            vm.SelectedItem = vm.Items[0];

            Assert.Equal(64, vm.SelectedDna.Length);
            Assert.Equal(vm.SelectedDnaTop.Length, vm.SelectedDnaBottom.Length);
            Assert.Equal(vm.SelectedDna, vm.SelectedDnaTop + vm.SelectedDnaBottom);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The inspector's VIEW loads and lays out.</summary>
    /// <remarks>
    /// Every other test here drives the ViewModel, which is exactly why the first cut passed them
    /// all and then killed the running app the moment a tile was clicked: a bad StaticResource key
    /// or an unbindable property throws at XAML load, which no ViewModel test can see. Rendering the
    /// control is the only thing that does.
    /// </remarks>
    [AvaloniaFact]
    public void The_inspector_view_loads_and_lays_out()
    {
        var loaded = CookedSet(out var dir);
        using var vm = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(vm.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());
            var view = new Views.SetInspectView { DataContext = ins };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Assert.True(view.Bounds.Width > 0, "the inspector did not lay out");
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Text == ins.Number);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>Stepping inside the inspector moves the browser's own selection with it.</summary>
    /// <remarks>
    /// What the user last looked at is what they expect to find selected when the modal closes.
    /// Without this you could arrow from the asset you opened to one forty along, close, and the
    /// rail would still be describing the first one.
    /// </remarks>
    [AvaloniaFact]
    public async Task Stepping_in_the_inspector_moves_the_browsers_selection()
    {
        var loaded = CookedSet(out var dir);
        var dialogs = new DialogService();
        using var vm = new SetBrowserViewModel(loaded, new FilePickerService(), dialogs, new StatusService());
        try
        {
            var opening = vm.Items[0];
            var task = vm.InspectCommand.ExecuteAsync(opening);
            var ins = Assert.IsType<SetInspectViewModel>(dialogs.Active);
            Assert.Same(opening, vm.SelectedItem);

            ins.NextCommand.Execute(null);
            Assert.Same(vm.Items[1], vm.SelectedItem);

            ins.CloseCommand.Execute(null);
            await task;
            Assert.Same(vm.Items[1], vm.SelectedItem);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
