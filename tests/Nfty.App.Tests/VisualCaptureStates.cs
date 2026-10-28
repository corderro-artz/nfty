using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The frames the audits found missing: states no capture had ever drawn. Each helper verifies the
/// state it claims before saving (the <c>CaptureReferences</c> rule), because a capture that
/// arranges a state and asserts nothing can keep picturing the wrong thing for months.
/// </summary>
public partial class VisualCapture
{
    private static readonly ThemeVariant[] Themes = { ThemeVariant.Light, ThemeVariant.Dark };

    private static string KeyOf(ThemeVariant v) => v.Key.ToString()!.ToLowerInvariant();

    /// <summary>Shows the view, pumps until <paramref name="until"/> holds, saves the frame.</summary>
    private static void Shoot(Control view, ThemeVariant variant, string file, Func<bool>? until = null,
        Action? verify = null, double width = 1180, double height = 720)
    {
        var window = new Window { RequestedThemeVariant = variant, Content = view, Width = width, Height = height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        if (until is not null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!until() && sw.ElapsedMilliseconds < 5000)
            {
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(until(), $"{file}: the state the frame claims never arrived");
        }
        verify?.Invoke();
        window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, file), PngBitmapEncoderOptions.Default);
    }

    /// <summary>
    /// The pop-out viewer. It is neither a page nor a modal: it is its OWN window, which renders at
    /// <see cref="ShellViewModel.BaseScale"/> inside the operating system's frame, so the area its
    /// content really has is the window size over the scale. Frames per theme, each a size the
    /// viewer can really be: the side-by-side default (1280x760 over 1.2), the 2D default (900x700),
    /// and the smallest it may be (640x480). The 1180x720 and 1067x526 frames are there too, as the
    /// page and smallest-page references every other screen is shot at; the last is sealed.
    /// </summary>
    [AvaloniaFact]
    public void Capture_viewer()
    {
        if (Dir is null) return;
        var scale = ShellViewModel.BaseScale;
        var sizes = new (string Tag, double W, double H, bool Model, bool Sealed)[]
        {
            ("both-default", 1280 / scale, 760 / scale, true, false),
            ("2d-default", 900 / scale, 700 / scale, false, false),
            ("min", 640 / scale, 480 / scale, true, false),
            ("page", 1180, 720, true, false),
            ("smallest-page", 1067, 526, true, true),
        };
        foreach (var variant in Themes)
        foreach (var (tag, w, h, withModel, isSealed) in sizes)
        {
            using var art = new Image<Rgba32>(32, 32);
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double d = ((x - 15.5) * (x - 15.5)) + ((y - 15.5) * (y - 15.5));
                if (d > 196) continue;
                art[x, y] = (x < 16, y < 16) switch
                {
                    (true, true) => new Rgba32(220, 70, 60, 255),
                    (false, true) => new Rgba32(70, 170, 90, 255),
                    (true, false) => new Rgba32(70, 110, 220, 255),
                    _ => new Rgba32(230, 200, 70, 255),
                };
            }
            var mesh = withModel ? Nfty.Model.Riser.TryBuild(art, null, new Nfty.Model.RiseOptions()) : null;
            using var subject = new ViewerViewTests.TrackedSubject("Vapor Pets #42", art,
                mesh is null ? null : new Nfty.App.Viewer.ViewerModel(mesh, Nfty.Model.FaceAtlas.Build(art), null))
            { AllowExport = !isSealed };
            var vm = ViewerViewTests.Vm(subject);
            // The outline display is on in the two frames that picture the 3D pane at its smallest and
            // its usual size; the rest show the solid shaded.
            vm.Preview.Wireframe = tag is "both-default" or "min";
            var view = new Views.ViewerView { DataContext = vm };
            var solid = view.FindControl<Views.ModelPreviewView>("ModelView")!;
            // The solid paints on a posted repaint once the pane has a size, so the frame waits for
            // one: a viewer captured before it lands pictures an empty 3D pane that is not the app's.
            Shoot(view, variant, $"viewer-{tag}-{KeyOf(variant)}.png",
                until: withModel ? () => solid.RepaintCount > 0 && !solid.RepaintPending : null,
                width: w, height: h);
            vm.Dispose();
        }
    }

    /// <summary>V-M1: the two CLI reports a CookBook card opens.</summary>
    [AvaloniaFact]
    public void Capture_reports()
    {
        if (Dir is null) return;
        var bookDir = Directory.CreateTempSubdirectory("nfty-cap-rep-").FullName;
        try
        {
            using var book = CookBookArchive.Read(DemoCookBook.WriteTo(bookDir));
            foreach (var variant in Themes)
            {
                var vm = new ReportDialogViewModel(book, new FakeDialogs(), new NoopClipboardService());
                Assert.False(vm.ShowingIdentity);
                Assert.True(vm.Text.Length > 200, "the stats report is empty");
                Shoot(new Views.ReportDialogView { DataContext = vm }, variant, $"report-collection-{KeyOf(variant)}.png");
                vm.ShowIdentityCommand.Execute(null);
                Assert.True(vm.ShowingIdentity);
                Assert.Equal(Nfty.Core.Stats.IdentityReport.Render(book), vm.Text);
                Shoot(new Views.ReportDialogView { DataContext = vm }, variant, $"report-identity-{KeyOf(variant)}.png");
            }
        }
        finally { Directory.Delete(bookDir, recursive: true); }
    }

    private static Nfty.Core.Publish.SealManifest Header(Nfty.Core.Publish.SealPolicy policy, string? note) => new(
        SealId: new string('a', 32), Collection: "Chest Demo", Count: 500, Note: note, Policy: policy,
        PolicyMac: new string('b', 64),
        Kdf: new Nfty.Core.Publish.SealKdf("pbkdf2-sha256", 600000, new string('c', 32)),
        Cipher: new Nfty.Core.Publish.SealCipher("aes-256-gcm", 1048576, 1, 100, new string('d', 8)));

    /// <summary>V-M2: the passphrase prompt in its three states.</summary>
    [AvaloniaFact]
    public void Capture_passphrase_prompt()
    {
        if (Dir is null) return;
        foreach (var variant in Themes)
        {
            var key = KeyOf(variant);
            var dialogs = new FakeDialogs();
            var prompt = new PassphraseDialogViewModel(Header(new Nfty.Core.Publish.SealPolicy(true, false), null), dialogs);
            Assert.True(prompt.AllowsExport);
            Capture(new Views.PassphraseDialogView { DataContext = prompt }, variant, $"passphrase-prompt-{key}.png");

            var wrong = new PassphraseDialogViewModel(Header(new Nfty.Core.Publish.SealPolicy(true, false), "Review copy"), dialogs)
            {
                Passphrase = "not-the-passphrase",
                Error = "Either the passphrase is wrong, or the file was changed after it was sealed.",
            };
            Assert.NotEmpty(wrong.Error);
            Capture(new Views.PassphraseDialogView { DataContext = wrong }, variant, $"passphrase-wrong-{key}.png");

            var viewOnly = new PassphraseDialogViewModel(
                Header(Nfty.Core.Publish.SealPolicy.ViewOnly, "Draft for review - please don't redistribute"), dialogs);
            Assert.False(viewOnly.AllowsExport);
            Capture(new Views.PassphraseDialogView { DataContext = viewOnly }, variant, $"passphrase-viewonly-{key}.png");
        }
    }

    /// <summary>
    /// V-M3: the Kitchen workspace on Landing. There is no Kitchen wizard in the app (New Kitchen is a
    /// save-file picker), so there is no <c>wizard-kitchen</c> frame to take; see the task report.
    /// </summary>
    [AvaloniaFact]
    public void Capture_kitchen_workspace()
    {
        if (Dir is null) return;
        foreach (var variant in Themes)
        {
            var (dir, ktn) = ShelfWorkspace();
            try
            {
                var kitchen = new KitchenSession();
                kitchen.Open(ktn);
                var vm = new LandingViewModel(new FakeNav(), new FakeDialogs(), new FilePickerService(),
                    new RecentsService(Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
                    book => null!, set => null!, (_, _, _) => null!, kitchen, rise: _ => null!);
                Assert.True(vm.KitchenShelf.HasCards, "the shelf shows no cards");
                Capture(new Views.LandingView { DataContext = vm }, variant, $"kitchen-workspace-{KeyOf(variant)}.png");
            }
            finally { Directory.Delete(dir, true); }
        }
    }

    /// <summary>V-M4: a half-built book, whose report leads with an explanation and names the empty recipe.</summary>
    [AvaloniaFact]
    public void Capture_validity_halfbuilt()
    {
        if (Dir is null) return;
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "New book", new Dimensions(8, 8),
                new Collection("New book", "", "NB"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "Cat", Array.Empty<string>(), Array.Empty<IncompatibilityRule>()),
                    Ingredients = Array.Empty<LoadedIngredient>(),
                },
            },
        };
        var problems = Validator.Validate(book);
        Assert.NotEmpty(problems);
        foreach (var variant in Themes)
        {
            var vm = new ValidityDialogViewModel(new FakeDialogs(), book, problems);
            Capture(new Views.ValidityDialogView { DataContext = vm }, variant,
                $"zz-dialog-validity-halfbuilt-{KeyOf(variant)}.png");
        }
    }

    /// <summary>V-M5: the Set browser's art, missing, loading, hover and exact-odds states.</summary>
    [AvaloniaFact]
    public void Capture_set_browser_states()
    {
        if (Dir is null) return;
        var root = Directory.CreateTempSubdirectory("nfty-cap-sb-").FullName;
        try
        {
            using (var book = CookBookArchive.Read(DemoCookBook.WriteTo(root)))
            using (var cooked = Generator.Generate(book, new GenerateOptions(12, "capture")))
                SetWriter.Write(cooked, Path.Combine(root, "set"), pack: false);
            var setDir = Path.Combine(root, "set");

            foreach (var variant in Themes)
            {
                var key = KeyOf(variant);

                // Art: real thumbnails landed, not placeholders.
                var vm = new SetBrowserViewModel(SetReader.Read(setDir), nav: new FakeNav(), bridge: new ImageBridge());
                Shoot(new Views.SetBrowserView { DataContext = vm }, variant, $"set-browser-art-{key}.png",
                    until: () => vm.Items.Take(6).All(i => i.IsThumbnailDecoded),
                    verify: () => Assert.All(vm.Items.Take(6), i => Assert.False(i.ImageMissing)));
                vm.Dispose();

                // Loading: the visible tiles carry a picture big enough that its decode is still running
                // when the frame is taken (small art decodes inside the first layout pass).
                var heavy = Path.Combine(root, "heavy.png");
                if (!File.Exists(heavy))
                {
                    using var big = new Image<Rgba32>(5000, 5000, new Rgba32(40, 90, 140, 255));
                    big.Save(heavy);
                }
                var slow = SetReader.Read(setDir);
                var originals = slow.Items.Take(6).Select(i => (i.ImagePath, Bytes: File.ReadAllBytes(i.ImagePath))).ToList();
                foreach (var (p, _) in originals) File.Copy(heavy, p, overwrite: true);
                var loading = new SetBrowserViewModel(slow, nav: new FakeNav(), bridge: new ImageBridge());
                try
                {
                    Shoot(new Views.SetBrowserView { DataContext = loading }, variant, $"set-browser-loading-{key}.png",
                        verify: () => Assert.True(loading.Items.Take(6).Any(i => i.IsLoading), "every visible tile already decoded"));
                    Task.WaitAll(loading.Items.Take(6).Select(i => i.PendingDecode).ToArray());
                    Dispatcher.UIThread.RunJobs();
                }
                finally { foreach (var (p, bytes) in originals) File.WriteAllBytes(p, bytes); }
                loading.Dispose();

                // Hover: the rail follows the pointer's tile while another stays selected.
                var hover = new SetBrowserViewModel(SetReader.Read(setDir), nav: new FakeNav(), bridge: new ImageBridge());
                hover.Hover(hover.Items[3]);
                Shoot(new Views.SetBrowserView { DataContext = hover }, variant, $"set-browser-hover-{key}.png",
                    until: () => hover.Items.Take(6).All(i => i.IsThumbnailDecoded),
                    verify: () =>
                    {
                        Assert.True(hover.IsShowingHover);
                        Assert.Same(hover.Items[3], hover.Shown);
                    });
                hover.Dispose();

                // Odds: the book sits beside the Set, so the figure is exact (no "~").
                var odds = new SetBrowserViewModel(SetReader.Read(setDir), nav: new FakeNav(), bridge: new ImageBridge());
                odds.ShowRarityOddsCommand.Execute(null);
                Shoot(new Views.SetBrowserView { DataContext = odds }, variant, $"set-browser-odds-{key}.png",
                    until: () => odds.CombinedText.StartsWith("this combo 1 in", StringComparison.Ordinal)
                        && !odds.CombinedText.Contains('~', StringComparison.Ordinal)
                        && odds.Items.Take(6).All(i => i.IsThumbnailDecoded));
                odds.Dispose();

                // Missing: a tile whose file is gone, drawn as damage and not as a slow decode.
                var gone = SetReader.Read(setDir);
                var path = gone.Items[2].ImagePath;
                var keep = File.ReadAllBytes(path);
                File.Delete(path);
                try
                {
                    var missing = new SetBrowserViewModel(gone, nav: new FakeNav(), bridge: new ImageBridge());
                    Shoot(new Views.SetBrowserView { DataContext = missing }, variant, $"set-browser-missing-{key}.png",
                        until: () => missing.Items.Take(6).All(i => i.IsThumbnailDecoded),
                        verify: () => Assert.True(missing.Items[2].ImageMissing));
                    missing.Dispose();
                }
                finally { File.WriteAllBytes(path, keep); }
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    internal static LoadedCookBook NamedBook(string bookName, string recipeName, string ingredientName, int variants)
    {
        var ids = Enumerable.Range(1, variants).Select(n => $"v{n}").ToArray();
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("layer", ingredientName, LayerKind.Custom, null,
                ids.Select((v, i) => new Variant(v, $"Variant number {i + 1}", i + 1)).ToArray()),
            VariantImages = ids.ToDictionary(v => v, _ => new Image<Rgba32>(8, 8, new Rgba32(90, 60, 120, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", bookName, new Dimensions(8, 8),
                new Collection(bookName, "", "LN"), new Dictionary<string, double> { ["rec"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("rec", recipeName, new[] { "layer" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
    }

    /// <summary>V-M6: twelve variants, long names, a filtered tree, a locked editor.</summary>
    [AvaloniaFact]
    public void Capture_pane_states()
    {
        if (Dir is null) return;
        foreach (var variant in Themes)
        {
            var key = KeyOf(variant);

            using (var twelve = NamedBook("Twelve", "Cat", "Aura", 12))
            {
                var recipe = twelve.Recipes[0];
                using var vm = new IngredientDetailViewModel(recipe.Ingredients[0], recipe, twelve, new ImageBridge(),
                    () => { }, () => false, null, null, new FilePickerService(), new FakeDialogs(), null, _ => { });
                Assert.Equal(12, recipe.Ingredients[0].Manifest.Variants.Count);
                Capture(new Views.IngredientDetailView { DataContext = vm }, variant, $"ingredient-12-variants-{key}.png");
            }

            using (var longBook = NamedBook("The Extraordinarily Long Collection Name Of Vapor Pets, Volume Two",
                       "The Remarkably Verbose Recipe Called Sleepy Cat Of The North",
                       "An Ingredient Whose Name Goes On And On And On", 3))
            {
                var nav = new FakeNav();
                var dialogs = new FakeDialogs();
                using var vm = new ExplorerViewModel(longBook, nav, dialogs, new ImageBridge(),
                    ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
                    new CookBookSession(), new FilePickerService(),
                    ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService());
                vm.SelectNodeCommand.Execute(vm.Root);
                Capture(new Views.ExplorerView { DataContext = vm }, variant, $"explorer-long-names-{key}.png");
            }

            using (var book = ExplorerViewModelTests.TwoRecipeBook())
            {
                var nav = new FakeNav();
                var dialogs = new FakeDialogs();
                using var vm = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                    ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
                    new CookBookSession(), new FilePickerService(),
                    ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs), new StatusService());
                vm.SearchQuery = "aura";
                Assert.False(vm.SearchFoundNothing);
                Capture(new Views.ExplorerView { DataContext = vm }, variant, $"explorer-filtered-{key}.png");

                // The editor on a book whose edit lock is closed: painting works, Save refuses and says why.
                var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
                using var editor = new IngredientEditorViewModel(cat.Ingredients[0], cat, book, new ImageBridge(),
                    new FakeNav(), new CookBookSession(), dialogs, new FilePickerService(), isEditing: () => false);
                editor.ActiveTool = EditorTool.Fill;
                editor.BrushValue = 200;
                editor.ApplyToolStroke(new[] { (0, 0) });
                Assert.True(editor.IsReadOnly);
                Assert.True(editor.IsDirty);
                Assert.False(editor.CanSave);
                Capture(new Views.IngredientEditorView { DataContext = editor }, variant, $"editor-locked-{key}.png");
            }
        }
    }

    private static ExportDialogViewModel ExportVm(Action<ExportDialogViewModel>? tweak = null)
    {
        var vm = new ExportDialogViewModel(ExportCaptureSet(withModels: true), new FilePickerService(),
            new NoopFolderRevealer(), new FakeDialogs());
        tweak?.Invoke(vm);
        return vm;
    }

    /// <summary>V-M7: the export card's folder shape and three refusals.</summary>
    [AvaloniaFact]
    public void Capture_export_states()
    {
        if (Dir is null) return;
        foreach (var variant in Themes)
        {
            var key = KeyOf(variant);

            var folder = ExportVm(v => v.Packed = false);
            Assert.False(folder.Packed);
            Capture(new Views.ExportDialogView { DataContext = folder }, variant, $"export-folder-{key}.png", 1180, 780);

            var mismatch = ExportVm(v =>
            {
                v.Tab = ExportTab.Seal;
                v.IsSealed = true;
                v.Passphrase = "correct-horse-battery";
                v.PassphraseConfirm = "correct-horse-batterx";
            });
            Assert.Equal("The two passphrases do not match.", mismatch.Problem);
            Capture(new Views.ExportDialogView { DataContext = mismatch }, variant, $"export-seal-mismatch-{key}.png", 1180, 780);

            var disarmed = ExportVm(v =>
            {
                v.Tab = ExportTab.Images;
                v.MakeSpriteSheet = true;
                v.NumberWatermark = false;
            });
            Assert.False(disarmed.NumberWatermark);
            Capture(new Views.ExportDialogView { DataContext = disarmed }, variant, $"export-corner-disarmed-{key}.png", 1180, 780);

            var refused = ExportVm(v =>
            {
                v.Tab = ExportTab.Images;
                v.MakeSpriteSheet = true;
                v.SheetColumns = 1;
                v.SheetRows = 1;
            });
            Assert.NotEmpty(refused.Problem);
            Capture(new Views.ExportDialogView { DataContext = refused }, variant, $"export-sheet-refused-{key}.png", 1180, 780);
        }
    }

    /// <summary>V-M9: a cook that failed, as the app shows it (the error card with the engine's own sentence).</summary>
    [AvaloniaFact]
    public void Capture_cook_failed()
    {
        if (Dir is null) return;
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var ex = Assert.ThrowsAny<Exception>(() => Generator.Generate(book, new GenerateOptions(100, "x")));
        foreach (var variant in Themes)
            Capture(new Views.ErrorDialogView
            { DataContext = new ErrorDialogViewModel(new FakeDialogs(), "Cook failed", ex.Message) },
                variant, $"cook-failed-{KeyOf(variant)}.png");
    }
}
