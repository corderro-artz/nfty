using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Viewer;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Three measured costs that were all the same mistake: work redone per pointer move or per resize
/// that only had to be done once. Each test pins the property that removes the cost (instances kept,
/// a walk counted), never a timing.
/// </summary>
public class RowsAndDragPerfTests
{
    private readonly Xunit.ITestOutputHelper _out;
    public RowsAndDragPerfTests(Xunit.ITestOutputHelper output) => _out = output;

    // ---- Set browser rarity table ------------------------------------------------------------------

    private sealed class NoDialogs : IDialogService
    {
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d) => Task.FromResult<TResult?>(default);
        public void Close(object? result) { }
    }

    private static (Window Window, SetBrowserViewModel Vm, Views.SetBrowserView View, string Dir) RenderSet()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using (var cooked = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(6, "rows")))
            SetWriter.Write(cooked, dir, pack: false);
        var vm = new SetBrowserViewModel(SetReader.Read(dir), new FilePickerService(), new NoDialogs(),
            new StatusService(), new NoopFolderRevealer());
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view, dir);
    }

    private static string[] Flat(IEnumerable<RarityRowViewModel> rows) =>
        rows.Select(r => $"{r.Trait_type}|{r.Value}|{r.RarityPct:R}").ToArray();

    private static string[] Flat(IEnumerable<RarityAttribute> rows) =>
        rows.Select(r => $"{r.Trait_type}|{r.Value}|{r.RarityPct:R}").ToArray();

    [AvaloniaFact]
    public void Hovering_between_assets_with_the_same_trait_count_prepares_no_new_containers()
    {
        var (window, vm, view, dir) = RenderSet();
        try
        {
            // Two assets whose tables are the same length: the rows then have to be reused as rows.
            var pair = vm.Items.GroupBy(i => i.Item.Rarity.Count).First(g => g.Count() >= 2).Take(2).ToArray();
            vm.HoveredItem = pair[0];
            Dispatcher.UIThread.RunJobs();

            var list = view.GetVisualDescendants().OfType<ItemsControl>()
                .Single(c => ReferenceEquals(c.ItemsSource, vm.ShownRarity));
            var prepared = 0;
            list.ContainerPrepared += (_, _) => prepared++;
            var first = vm.ShownRarity;

            vm.HoveredItem = pair[1];
            Dispatcher.UIThread.RunJobs();
            vm.HoveredItem = pair[0];
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, prepared);
            Assert.Same(first, vm.ShownRarity);
        }
        finally { window.Close(); vm.Dispose(); try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public void Reading_the_rarity_table_twice_is_the_same_instance_and_rows_are_stable()
    {
        var (window, vm, _, dir) = RenderSet();
        try
        {
            var a = vm.ShownRarity;
            var rows = a.ToArray();
            Assert.Same(a, vm.ShownRarity);
            vm.HoveredItem = vm.Items[1];
            Assert.Same(a, vm.ShownRarity);
            Assert.All(a.Take(rows.Length), r => Assert.Contains(r, rows));   // same row objects, new values
        }
        finally { window.Close(); vm.Dispose(); try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public void The_sorted_rarity_rows_match_the_table_sort_for_every_column_and_direction()
    {
        var (window, vm, _, dir) = RenderSet();
        try
        {
            foreach (var item in vm.Items)
            {
                vm.HoveredItem = item;
                foreach (var column in new[] { "Trait", "Value", "Pct", "Trait" })
                    foreach (var twice in new[] { false, true })
                    {
                        vm.RaritySort.By(column);
                        if (twice) vm.RaritySort.By(column);
                        var expected = vm.RaritySort.Order(item.Item.Rarity, static (r, col) => col switch
                        {
                            "Value" => r.Value,
                            "Pct" => (object)r.RarityPct,
                            _ => r.Trait_type,
                        });
                        Assert.Equal(Flat(expected), Flat(vm.ShownRarity));
                    }
            }
        }
        finally { window.Close(); vm.Dispose(); try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>Prints what forty hovers cost on the rendered browser (no assertion: a timing is not a
    /// test; the container test above is). Run with <c>-l "console;verbosity=detailed"</c>.</summary>
    [AvaloniaFact]
    public void Forty_hovers_cost_is_reported()
    {
        var (window, vm, _, dir) = RenderSet();
        try
        {
            vm.HoveredItem = vm.Items[0];
            Dispatcher.UIThread.RunJobs();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 40; i++)
            {
                vm.HoveredItem = vm.Items[(i + 1) % vm.Items.Count];
                Dispatcher.UIThread.RunJobs();
            }
            sw.Stop();
            _out.WriteLine($"40 hovers: {sw.ElapsedMilliseconds} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 1024} KB");
        }
        finally { window.Close(); vm.Dispose(); try { Directory.Delete(dir, true); } catch { } }
    }

    // ---- row chunkers ----------------------------------------------------------------------------

    private static Views.ViewerView Gallery(out ViewerViewModel vm)
    {
        vm = ViewerViewTests.Vm(new GallerySubject(ArchiveGalleryTests.MixedGallery()));
        return new Views.ViewerView { DataContext = vm };
    }

    [AvaloniaFact]
    public void Two_viewer_galleries_resizing_alternately_never_replace_each_others_rows()
    {
        var viewA = Gallery(out var vmA);
        var viewB = Gallery(out var vmB);
        var a = new Window { Content = viewA, Width = 1000, Height = 700 };
        var b = new Window { Content = viewB, Width = 1000, Height = 700 };
        try
        {
            a.Show(); b.Show();
            Dispatcher.UIThread.RunJobs();
            var listA = viewA.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "GalleryList");
            var listB = viewB.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "GalleryList");
            var changes = 0;
            void Watch(object? _, AvaloniaPropertyChangedEventArgs e)
            { if (e.Property == ItemsControl.ItemsSourceProperty) changes++; }
            listA.PropertyChanged += Watch;
            listB.PropertyChanged += Watch;

            for (var i = 0; i < 10; i++)
            {
                a.Width = 1000 + (i % 2) * 4; Dispatcher.UIThread.RunJobs();
                b.Width = 1000 + (i % 2) * 4; Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(0, changes);
        }
        finally { a.Close(); b.Close(); vmA.Dispose(); vmB.Dispose(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ChunkAndDrop()
    {
        var gate = new ThumbnailGate(1);
        IReadOnlyList<GalleryItem> items = Enumerable.Range(0, 20)
            .Select(i => new GalleryItem("t" + i, null, () => Task.FromResult<ViewerSubject>(null!), gate)).ToList();
        var conv = new Nfty.App.Converters.GalleryRowChunkConverter();
        conv.Convert([items, 600.0], typeof(object), null, System.Globalization.CultureInfo.InvariantCulture);
        return new WeakReference(items);
    }

    [Fact]
    public void A_row_chunker_holds_no_static_state_and_its_items_collect_with_it()
    {
        // Deterministic stand-ins for "the last closed gallery stays alive": no static field on any
        // chunker (a window left alive by some other test would make a view-level weak reference flaky).
        foreach (var t in new[] { typeof(Nfty.App.Converters.GalleryRowChunkConverter),
                                  typeof(Nfty.App.Converters.RowChunkConverter),
                                  typeof(Nfty.App.Converters.RiseTileRowConverter) })
            Assert.DoesNotContain(t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic).Where(f => !f.IsLiteral), _ => true);

        var weak = ChunkAndDrop();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(weak.IsAlive);
    }

    // ---- explorer tree drag ----------------------------------------------------------------------

    private static LoadedRecipe Rec(string id, int layers) => new()
    {
        Manifest = new RecipeManifest(id, id.ToUpperInvariant(),
            Enumerable.Range(0, layers).Select(n => $"{id}{n}").ToArray(), Array.Empty<IncompatibilityRule>()),
        Ingredients = Enumerable.Range(0, layers).Select(n => new LoadedIngredient
        {
            Manifest = new IngredientManifest($"{id}{n}", $"L{n}", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(8, 8) },
        }).ToArray(),
    };

    [AvaloniaFact]
    public void A_drag_walks_the_tree_for_sibling_rows_once_not_once_per_move()
    {
        var recipes = Enumerable.Range(0, 6).Select(n => Rec($"r{n}", 12)).ToArray();
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8), new Collection("Book", "", "B"),
                recipes.ToDictionary(r => r.Manifest.Id, _ => 1.0)),
            Recipes = recipes,
        };
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        CookBookArchive.Write(path, book.Manifest, book.Recipes);
        book.Dispose();
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        using var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        if (!vm.CanReorderTree) vm.ToggleLockCommand.Execute(null);
        foreach (var r in vm.Root.Children) r.IsExpanded = true;

        var view = new Views.ExplorerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 1400 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Border Row(Nfty.App.Models.ExplorerNode n) => view.GetVisualDescendants().OfType<Border>()
                .First(x => x.Classes.Contains("node") && ReferenceEquals(x.DataContext, n));
            Avalonia.Point Center(Border r) => r.TranslatePoint(new Avalonia.Point(r.Bounds.Width / 2, r.Bounds.Height / 2), window)!.Value;

            var layers = vm.Root.Children[0].Children;
            Assert.True(vm.CanMove(layers[8]));
            var from = Center(Row(layers[8]));
            window.MouseDown(from, MouseButton.Left);
            for (var i = 1; i <= 8; i++)
            {
                window.MouseMove(from - new Avalonia.Point(0, i * 6));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(1, view.RowWalks);

            // Abandon: nothing moved, and the next drag walks afresh.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.MouseUp(from, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        finally { window.Close(); session.Dispose(); try { Directory.Delete(dir, true); } catch { } }
    }
}
