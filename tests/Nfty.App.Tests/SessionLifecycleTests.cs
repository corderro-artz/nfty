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

namespace Nfty.App.Tests;

/// <summary>
/// Opening was one-way. Navigation only ever pushed, <c>ICookBookSession.Close</c> and
/// <c>IKitchenSession.Close</c> had no callers anywhere in the app, and <c>IKitchenSession.Open</c>
/// was reached only from the create flow. So a second CookBook meant restarting the process, a
/// cooked Set could be entered and not left, and a Kitchen made in one session was unreachable in
/// the next.
///
/// <para>These pin the lifecycle rather than the buttons: what must be disposed, in what order, and
/// what must survive.</para>
/// </summary>
public class SessionLifecycleTests
{
    private static LoadedCookBook Book(string id = "cb")
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                new[] { new Variant("day", "Day", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["day"] = new(4, 4) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest(id, "VaporPets", new Dimensions(4, 4),
                new Collection("VaporPets", "", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
    }

    private static ShellViewModel Shell(INavigationService nav, ICookBookSession? session = null,
        IKitchenSession? kitchen = null) =>
        new(nav, new FakeDialogs(), new FakeNotYetWired(), new StubTheme(), new StatusService(),
            kitchen, session);

    private sealed class StubTheme : IThemeService
    { public bool IsDark { get; private set; } public void Toggle() => IsDark = !IsDark; }

    private static ExplorerViewModel Explorer(LoadedCookBook book, INavigationService nav)
    {
        var dialogs = new FakeDialogs();
        return new ExplorerViewModel(book, nav, dialogs, new FakeNotYetWired(), new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            new CookBookSession(), new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService());
    }

    [AvaloniaFact]
    public void Nothing_is_open_on_landing_so_there_is_nothing_to_close()
    {
        var nav = new NavigationService();
        var shell = Shell(nav);
        nav.To(new HelpViewModel(new FakeDialogs()));   // stand-in for a non-document page

        Assert.False(shell.HasOpenDocument);
        Assert.False(shell.CloseDocumentCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Closing_a_cookbook_returns_to_the_previous_page_and_frees_the_book()
    {
        var nav = new NavigationService();
        var session = new CookBookSession();
        var shell = Shell(nav, session);

        var landing = new HelpViewModel(new FakeDialogs());
        nav.To(landing);

        var book = Book();
        session.Open(book, "C:/vp.cbk");
        nav.To(Explorer(book, nav));

        Assert.True(shell.HasOpenDocument);
        Assert.True(shell.CloseDocumentCommand.CanExecute(null));

        shell.CloseDocumentCommand.Execute(null);

        Assert.Same(landing, nav.Current);
        Assert.Null(session.Current);
        Assert.Null(session.SourcePath);
        Assert.False(shell.HasOpenDocument);
    }

    /// <summary>
    /// The images actually go. Asserted with ImageSharp's own leak counter rather than by poking a
    /// disposed Image: <c>Image.Width</c> keeps returning its cached value after Dispose, so the
    /// obvious "it should throw now" test passes whether or not anything was freed.
    /// </summary>
    [AvaloniaFact]
    public void Closing_a_cookbook_frees_every_decoded_variant_image()
    {
        var nav = new NavigationService();
        var session = new CookBookSession();
        var shell = Shell(nav, session);
        nav.To(new HelpViewModel(new FakeDialogs()));

        int before = SixLabors.ImageSharp.Diagnostics.MemoryDiagnostics.TotalUndisposedAllocationCount;

        var book = Book();
        session.Open(book, "C:/vp.cbk");
        nav.To(Explorer(book, nav));
        Assert.True(SixLabors.ImageSharp.Diagnostics.MemoryDiagnostics.TotalUndisposedAllocationCount > before,
            "the fixture decoded no images, so this could not detect a leak");

        shell.CloseDocumentCommand.Execute(null);

        Assert.Equal(before, SixLabors.ImageSharp.Diagnostics.MemoryDiagnostics.TotalUndisposedAllocationCount);
    }

    /// <summary>
    /// Order, not just outcome. Back() disposes the popped Explorer, which releases the detail views
    /// holding the book's decoded images; only then may the session dispose the images themselves.
    /// Closing the session first would leave a live Explorer pointing at disposed bitmaps — a crash
    /// on the next render rather than a clean exit.
    /// </summary>
    [AvaloniaFact]
    public void The_page_is_popped_before_the_session_that_owns_its_images_is_closed()
    {
        var nav = new NavigationService();
        ViewModelBase? pageWhenSessionClosed = null;

        // Samples what navigation is showing at the instant Close is called. If the Explorer were
        // still current, it would be holding images the very next statement disposes.
        var session = new RecordingSession(() => pageWhenSessionClosed = nav.Current);
        var shell = Shell(nav, session);

        var landing = new HelpViewModel(new FakeDialogs());
        nav.To(landing);
        using var book = Book();
        nav.To(Explorer(book, nav));

        shell.CloseDocumentCommand.Execute(null);

        Assert.Same(landing, pageWhenSessionClosed);
    }

    private sealed class RecordingSession(Action onClose) : ICookBookSession
    {
        public LoadedCookBook? Current => null;
        public string? SourcePath => null;
        public event Action? Changed { add { } remove { } }
        public void Open(LoadedCookBook book, string? sourcePath = null) { }
        public void Replace(LoadedCookBook book) { }
        public void Close() => onClose();
        public void Dispose() { }
    }

    /// <summary>A Set browser holds no CookBook, so closing it must pop the page and leave the
    /// session alone — closing a book the user never opened would dispose images still in use if a
    /// CookBook happened to be open underneath.</summary>
    [AvaloniaFact]
    public void Closing_a_set_browser_does_not_touch_the_cookbook_session()
    {
        var nav = new NavigationService();
        var session = new CookBookSession();
        var shell = Shell(nav, session);

        var book = Book();
        session.Open(book, "C:/vp.cbk");
        var setDir = Directory.CreateTempSubdirectory().FullName;
        using (var cooked = Nfty.Core.Generation.Generator.Generate(
            CoreTestBook.Tiny(), new Nfty.Core.Generation.GenerateOptions(1, "seed1")))
            Nfty.Core.Output.SetWriter.Write(cooked, setDir, pack: false);

        nav.To(new HelpViewModel(new FakeDialogs()));
        nav.To(new SetBrowserViewModel(Nfty.Core.Output.SetReader.Read(setDir)));

        Assert.True(shell.HasOpenDocument);
        shell.CloseDocumentCommand.Execute(null);

        Assert.NotNull(session.Current);   // still open, still usable
        Assert.Equal(4, book.Recipes[0].Ingredients[0].VariantImages["day"].Width);
    }

    /// <summary>The Kitchen is a workspace that outlives any one CookBook — explorer.html calls it
    /// "fixed for every item below it" — so closing a book must not evict it.</summary>
    [AvaloniaFact]
    public void Closing_a_cookbook_leaves_the_kitchen_open()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        string ktn = Path.Combine(dir, "Studio.ktn");
        Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

        var nav = new NavigationService();
        var session = new CookBookSession();
        var kitchen = new KitchenSession();
        kitchen.Open(ktn);
        var shell = Shell(nav, session, kitchen);

        var book = Book();
        session.Open(book, "C:/vp.cbk");
        nav.To(new HelpViewModel(new FakeDialogs()));
        nav.To(Explorer(book, nav));

        shell.CloseDocumentCommand.Execute(null);

        Assert.Equal("Studio", kitchen.Current?.Manifest.Name);
        Assert.True(shell.HasKitchen);
    }

    // ---- the Kitchen, which could be created and then never re-entered or left ------------------

    private sealed class OpenPicker(string? path) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult(path);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    private static LandingViewModel Landing(IFilePickerService picker, IKitchenSession kitchen)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        return new LandingViewModel(nav, dialogs, new FakeNotYetWired(), picker,
            new RecentsService(Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
            _ => null!, _ => null!, (_, _, _) => null!, kitchen);
    }

    [AvaloniaFact]
    public async Task An_existing_kitchen_can_be_reopened_in_a_later_session()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        string ktn = Path.Combine(dir, "Studio.ktn");
        Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

        // A fresh session: nothing is open, exactly as after a restart.
        var kitchen = new KitchenSession();
        var landing = Landing(new OpenPicker(ktn), kitchen);
        Assert.False(landing.HasKitchen);

        await landing.OpenKitchenCommand.ExecuteAsync(null);

        Assert.True(landing.HasKitchen);
        Assert.Equal("Studio", landing.KitchenName);
        Assert.Equal(ktn, kitchen.Path);
    }

    [AvaloniaFact]
    public async Task A_kitchen_can_be_left_without_opening_another()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        string ktn = Path.Combine(dir, "Studio.ktn");
        Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

        var kitchen = new KitchenSession();
        var landing = Landing(new OpenPicker(ktn), kitchen);

        Assert.False(landing.CloseKitchenCommand.CanExecute(null));   // nothing to leave yet
        await landing.OpenKitchenCommand.ExecuteAsync(null);
        Assert.True(landing.CloseKitchenCommand.CanExecute(null));

        landing.CloseKitchenCommand.Execute(null);

        Assert.False(landing.HasKitchen);
        Assert.Null(kitchen.Current);
        Assert.False(landing.CloseKitchenCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Cancelling_the_picker_leaves_the_open_kitchen_alone()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        string ktn = Path.Combine(dir, "Studio.ktn");
        Kitchen.Create(ktn, new KitchenManifest("studio", "Studio"));

        var kitchen = new KitchenSession();
        kitchen.Open(ktn);
        var landing = Landing(new OpenPicker(null), kitchen);

        await landing.OpenKitchenCommand.ExecuteAsync(null);

        Assert.Equal("Studio", landing.KitchenName);
    }

    // ---- crumbs, which reported the path and could not walk it ---------------------------------

    /// <summary>
    /// explorer.html's breadcrumb segments are <c>role="link" tabindex="0"</c> and select the node
    /// they name. Ours were plain TextBlocks: the trail said where you were and offered no way back
    /// up a level, so the tree was the only route.
    /// </summary>
    [AvaloniaFact]
    public void Every_crumb_but_the_last_carries_the_node_it_names()
    {
        var nav = new NavigationService();
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = Explorer(book, nav);

        // Drill to an ingredient, which is the deepest trail: CookBook › Recipe › Ingredient.
        var recipeNode = vm.Root.Children[0];
        vm.SelectNodeCommand.Execute(recipeNode.Children[0]);

        Assert.Equal(3, vm.Crumbs.Count);
        Assert.All(vm.Crumbs, c => Assert.NotNull(c.Target));
        Assert.Same(vm.Root, vm.Crumbs[0].Target);
        Assert.Same(recipeNode, vm.Crumbs[1].Target);

        // ...and walking back up actually moves the selection.
        vm.SelectNodeCommand.Execute(vm.Crumbs[0].Target);
        Assert.Same(vm.Root, vm.SelectedNode);
        Assert.Single(vm.Crumbs);
    }
}
