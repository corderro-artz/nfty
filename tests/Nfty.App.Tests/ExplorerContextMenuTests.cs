using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Right-click the real tree, then activate its actual popup items.</summary>
public class ExplorerContextMenuTests
{
    private sealed class Dialogs : IDialogService
    {
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed;
        public Task<T?> ShowAsync<T>(ViewModelBase dialog)
        {
            Active = dialog; Changed?.Invoke();
            return Task.FromResult(dialog is ConfirmDialogViewModel ? (T?)(object)true : default);
        }
        public void Close(object? result) { Active = null; Changed?.Invoke(); }
    }

    private static Point Center(Visual control, Visual relativeTo) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

    [AvaloniaTheory]
    [InlineData("Edit")]
    [InlineData("Delete")]
    [InlineData("Open in viewer")]
    public async Task Tree_menu_actions_reach_the_right_clicked_ingredient(string action)
    {
        var (path, session, _, _) = IngredientEditorSaveTests.OnDisk();
        var host = new ArchiveGalleryTests.Host();
        var nav = new FakeNav();
        var dialogs = new Dialogs();
        using var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService())
            { Viewer = host };
        vm.ToggleLockCommand.Execute(null);
        var recipe = vm.Root.Children[0];
        recipe.IsExpanded = true;
        var target = recipe.Children[0];
        var view = new Views.ExplorerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1067, Height = 526 };
        var tree = view.FindControl<TreeView>("Tree")!;
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var row = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("node") && ReferenceEquals(b.DataContext, target));
            var at = Center(row, window);
            var menu = tree.ContextMenu!;
            MenuItem Item(string header) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, header));
            void OpenAt(Point point)
            {
                window.MouseDown(point, MouseButton.Right);
                window.MouseUp(point, MouseButton.Right);
                Dispatcher.UIThread.RunJobs();
            }
            var rootRow = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("node") && ReferenceEquals(b.DataContext, vm.Root));
            OpenAt(Center(rootRow, window));
            Assert.True(menu.IsOpen);
            Assert.False(Item("Delete").IsEffectivelyEnabled);
            Assert.False(Item("Open in viewer").IsEffectivelyEnabled);
            menu.Close();

            vm.ToggleLockCommand.Execute(null);
            OpenAt(at);
            Assert.True(menu.IsOpen);
            Assert.False(Item("Edit").IsEffectivelyEnabled);
            Assert.False(Item("Delete").IsEffectivelyEnabled);
            Assert.True(Item("Open in viewer").IsEffectivelyEnabled);
            menu.Close();
            vm.ToggleLockCommand.Execute(null);

            var blank = tree.TranslatePoint(new Point(tree.Bounds.Width - 2, tree.Bounds.Height - 2), window)!.Value;
            OpenAt(blank);
            Assert.False(menu.IsOpen); // A blank-area request must never reuse the previous target.
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            menu.Close();
            tree.RaiseEvent(new ContextRequestedEventArgs()); // The keyboard route has no pointer position.
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            Assert.Same(target, vm.SelectedNode);
            var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, action));
            Assert.True(item.IsEffectivelyEnabled);
            var popup = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(menu));
            var centre = Center(item, popup);
            popup.MouseDown(centre, MouseButton.Left);
            popup.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            if (vm.DeleteSelectedCommand.ExecutionTask is { } writing) await writing;

            if (action == "Edit")
                Assert.Equal(target.Name, Assert.IsType<ItemSettingsDialogViewModel>(dialogs.Active).Name);
            else if (action == "Delete")
            {
                Assert.IsType<ConfirmDialogViewModel>(dialogs.Active);
                using var read = CookBookArchive.Read(path);
                Assert.DoesNotContain(read.Recipes[0].Ingredients, i => i.Manifest.Id == target.Id);
            }
            else
                Assert.Single(host.Opened);
            menu.Close();
        }
        finally
        {
            tree.ContextMenu?.Close();
            window.Close();
            foreach (var subject in host.Opened) subject.Dispose();
            vm.Dispose(); session.Dispose();
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
