using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia;
using Point = Avalonia.Point;

namespace Nfty.App.Tests;

/// <summary>
/// Delete variant, from the pane the user presses it on to the bytes on disk.
/// </summary>
/// <remarks>
/// <para>The button has been wrong twice, and a ViewModel test passed both times. It first reported
/// the action as unbuilt while sitting there enabled; then it said "delete variants in the editor"
/// and NAVIGATED there — so the button deleted nothing and moved the reader to another screen. Its
/// test asserted exactly that, which is how a shipped defect keeps a green suite.</para>
///
/// <para>So this one names no command that stops at a boundary. It goes through the real Explorer,
/// the real <c>CookBookEdits</c>/<c>CookBookPersistence</c> seams every other structural edit uses,
/// into a real <c>.cbk</c> in a temp directory — and then REOPENS that file and asks what is in it.
/// A delete that reached a ViewModel and no archive passes every other kind of test there is.</para>
/// </remarks>
public class VariantDeleteJourneyTests
{
    [AvaloniaFact]
    public async Task Rendered_delete_enables_after_unlock_and_real_confirmation_persists()
    {
        var (path, session) = OnDisk();
        var dialogs = new DialogService();
        using var vm = Explorer(session, dialogs);
        var pane = Pane(vm);
        var view = new Nfty.App.Views.IngredientDetailView { DataContext = pane };
        var window = new Window { Content = view, Width = 900, Height = 650 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, "Delete variant"));
            Assert.False(button.IsEffectivelyEnabled);
            vm.ToggleLockCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsEffectivelyEnabled);
            pane.SelectVariantCommand.Execute("glow");
            Assert.NotNull(pane.Selected);
            pane.SelectVariantCommand.Execute("glow");      // clicking the selected row keeps it:
            Assert.Equal("glow", pane.Selected!.Id);        // a variant table never selects nothing
            var at = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Assert.IsType<ConfirmDialogViewModel>(dialogs.Active).ConfirmCommand.Execute(null);
            await pane.DeleteVariantCommand.ExecutionTask!;
            using var reread = CookBookArchive.Read(path);
            Assert.Equal(new[] { "spark", "haze" }, reread.Recipes[0].Ingredients[0].Manifest.Variants.Select(v => v.Id));
        }
        finally { window.Close(); session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [AvaloniaFact]
    public async Task Variant_reorder_and_names_survive_reopening_the_archive()
    {
        var (path, session) = OnDisk();
        var dialogs = new DialogService();
        using var vm = Explorer(session, dialogs);
        try
        {
            vm.ToggleLockCommand.Execute(null);
            var pane = Pane(vm);
            var view = new Nfty.App.Views.IngredientDetailView { DataContext = pane };
            var window = new Window { Content = view, Width = 900, Height = 650 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var rows = view.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is VariantRow).ToArray();
                var from = rows.Last().TranslatePoint(new Point(20, rows.Last().Bounds.Height / 2), window)!.Value;
                var to = rows.First().TranslatePoint(new Point(20, 2), window)!.Value;
                Assert.True(pane.CanReorder);
                Assert.True(from.Y < window.Bounds.Height, $"Row outside window: {from}, window {window.Bounds}");
                Assert.Contains((window.InputHitTest(from) as Visual)!.GetSelfAndVisualAncestors(), v => ReferenceEquals(v, rows.Last()));
                window.MouseDown(from, MouseButton.Left);
                window.MouseMove(to);
                window.MouseUp(to, MouseButton.Left);
                await view.PendingReorder;
            }
            finally { window.Close(); }
            using (var reread = CookBookArchive.Read(path))
                Assert.Equal(new[] { "haze", "glow", "spark" }, reread.Recipes[0].Ingredients[0].Manifest.Variants.Select(v => v.Id));
            foreach (var node in new[] { vm.Root.Children[0], vm.Root.Children[0].Children[0] })
            {
                vm.SelectNodeCommand.Execute(node);
                var editing = vm.EditSelectedCommand.ExecuteAsync(null);
                var form = Assert.IsType<ItemSettingsDialogViewModel>(dialogs.Active);
                form.Name = node.Name + " edited";
                await form.SaveCommand.ExecuteAsync(null);
                await editing;
            }
            using var saved = CookBookArchive.Read(path);
            Assert.Equal("Cat edited", saved.Recipes[0].Manifest.Name);
            Assert.Equal("Aura edited", saved.Recipes[0].Ingredients[0].Manifest.Name);
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
    private sealed class Answering(bool answer) : IDialogService
    {
        public ViewModelBase? Active { get; private set; }
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Active = d;
            return Task.FromResult((TResult?)(object?)answer);
        }
        public void Close(object? result) { }
    }

    /// <summary>A one-layer book on disk whose layer has three variants.</summary>
    private static (string Path, CookBookSession Session) OnDisk()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        string[] variants = ["glow", "spark", "haze"];
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                variants.Select(v => new Variant(v, v, 1)).ToArray()),
            VariantImages = variants.ToDictionary(v => v, _ => new Image<Rgba32>(8, 8),
                StringComparer.Ordinal),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        CookBookArchive.Write(path, new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }), new[] { recipe });

        var book = CookBookArchive.Read(path);
        var session = new CookBookSession();
        session.Open(book, path);
        return (path, session);
    }

    private static ExplorerViewModel Explorer(CookBookSession session, IDialogService dialogs)
    {
        var nav = new FakeNav();
        return new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
    }

    /// <summary>Selects the layer node and returns the pane the user is looking at.</summary>
    private static IngredientDetailViewModel Pane(ExplorerViewModel vm)
    {
        vm.SelectNodeCommand.Execute(vm.Root.Children[0].Children[0]);
        return Assert.IsType<IngredientDetailViewModel>(vm.CurrentDetail);
    }

    [AvaloniaFact]
    public async Task Deleting_a_variant_removes_it_from_the_cbk()
    {
        var (path, session) = OnDisk();
        var vm = Explorer(session, new Answering(true));
        try
        {
            vm.ToggleLockCommand.Execute(null);                  // structure needs the lock off
            var pane = Pane(vm);
            pane.SelectVariantCommand.Execute("spark");

            Assert.True(pane.DeleteVariantCommand.CanExecute(null));
            await pane.DeleteVariantCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            var ing = reread.Recipes[0].Ingredients[0];
            Assert.Equal(new[] { "glow", "haze" }, ing.Manifest.Variants.Select(v => v.Id));
            Assert.Equal(2, ing.VariantImages.Count);            // the image went with it

            // And the book is still a book: the pane reloaded onto the same layer.
            Assert.Empty(Validator.Validate(reread));
            Assert.Equal("aura", vm.SelectedNode!.Id);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>The lock governs structure, and a variant is structure.</summary>
    [AvaloniaFact]
    public void A_locked_book_cannot_have_a_variant_deleted()
    {
        var (path, session) = OnDisk();
        var vm = Explorer(session, new Answering(true));
        try
        {
            var pane = Pane(vm);                                  // lock still on
            Assert.False(pane.DeleteVariantCommand.CanExecute(null));
            Assert.Contains("locked", pane.DeleteVariantTip);

            vm.ToggleLockCommand.Execute(null);
            Assert.True(Pane(vm).DeleteVariantCommand.CanExecute(null));
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>Declining the confirm writes nothing.</summary>
    [AvaloniaFact]
    public async Task Declining_the_confirm_leaves_the_archive_alone()
    {
        var (path, session) = OnDisk();
        var vm = Explorer(session, new Answering(false));
        try
        {
            vm.ToggleLockCommand.Execute(null);
            await Pane(vm).DeleteVariantCommand.ExecuteAsync(null);

            using var reread = CookBookArchive.Read(path);
            Assert.Equal(3, reread.Recipes[0].Ingredients[0].Manifest.Variants.Count);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>
    /// Deleting down to one leaves the button unavailable rather than letting the last one go.
    /// </summary>
    [AvaloniaFact]
    public async Task The_layers_last_variant_survives()
    {
        var (path, session) = OnDisk();
        var vm = Explorer(session, new Answering(true));
        try
        {
            vm.ToggleLockCommand.Execute(null);
            for (int i = 0; i < 2; i++)
            {
                var pane = Pane(vm);
                Assert.True(pane.DeleteVariantCommand.CanExecute(null));
                await pane.DeleteVariantCommand.ExecuteAsync(null);
            }

            var last = Pane(vm);
            Assert.Single(last.Variants);
            Assert.False(last.DeleteVariantCommand.CanExecute(null));

            using var reread = CookBookArchive.Read(path);
            Assert.Single(reread.Recipes[0].Ingredients[0].Manifest.Variants);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
