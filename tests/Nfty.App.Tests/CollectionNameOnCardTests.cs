using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The CookBook card names the collection its assets will be published under, and that name is the
/// one the writer actually uses.
/// </summary>
/// <remarks>
/// <para>A book carries TWO names. <c>Manifest.Name</c> names the <c>.cbk</c> and is what the
/// titlebar, the tree and the recents list show; <c>Manifest.Collection.Name</c> is what
/// <c>SetWriter</c> puts in every <c>metadata/NNNN.json</c> as <c>"{name} #{n}"</c> and what an
/// export is named after. The card showed the first as its title while taking its symbol and its
/// description from the second — three of four values from one object and the title from another —
/// and the collection name appeared on no screen in the app.</para>
///
/// <para>They are equal for anything the GUI makes, because the New CookBook wizard writes one typed
/// name into both, which is exactly why this went unnoticed: every fixture and every real book had
/// them the same. These tests set them DIFFERENTLY, which is the only state that can tell the two
/// apart.</para>
/// </remarks>
public class CollectionNameOnCardTests
{
    /// <summary>A book whose file name and published collection name differ.</summary>
    private static LoadedCookBook SplitNameBook()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                [new Variant("a", "A", 1), new Variant("b", "B", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(8, 8),
                ["b"] = new Image<Rgba32>(8, 8),
            },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "working-draft-v3", new Dimensions(8, 8),
                new Collection("Vapor Pets", "a description", "VP"),
                new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = [new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", ["bg"], []),
                Ingredients = [ing],
            }],
        };
    }

    [AvaloniaFact]
    public void The_card_shows_the_book_name_and_the_published_name_separately()
    {
        using var book = SplitNameBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        // The title is still the BOOK. Changing that would put the card at odds with the titlebar,
        // the tree and the recents row, which all name the file.
        Assert.Equal("working-draft-v3", vm.Name);

        // And the published name is stated rather than left to be inferred from the title.
        Assert.Contains("Vapor Pets", vm.MintName);
        Assert.DoesNotContain("working-draft-v3", vm.MintName);
    }

    [AvaloniaFact]
    public void What_the_card_promises_is_what_the_writer_publishes()
    {
        // THE ASSERTION THAT MAKES THIS MORE THAN A LABEL. A figure on screen must come from the
        // code that owns it: the card's string is compared against a metadata file a real cook
        // wrote, so the two cannot drift into disagreeing about what the assets are called.
        using var book = SplitNameBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using (var set = Generator.Generate(SplitNameBook(), new GenerateOptions(1, "seed1")))
                SetWriter.Write(set, dir, pack: false);

            string json = File.ReadAllText(Path.Combine(dir, "metadata", "0001.json"));
            string published = JsonDocument.Parse(json).RootElement.GetProperty("name").GetString()!;

            Assert.Equal("Vapor Pets #1", published);
            Assert.EndsWith(published, vm.MintName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public void The_suffix_is_on_the_card_and_fits_beside_the_title()
    {
        // On a frame, at the smallest page the app gives this card - the title line was chosen for
        // this because the chip row below is already ~471px of ~660 and a fifth chip would either
        // overrun it or wrap and eat the DNA table's one remaining row.
        using var book = SplitNameBook();
        var explorer = ExplorerFor(book);
        var view = new Views.ExplorerView { DataContext = explorer };
        var window = new Window
        {
            Content = view,
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            var suffix = card.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Classes.Contains("mintname"));
            var title = card.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Classes.Contains("idname"));
            var idcard = card.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("cbk-id"));

            Assert.True(suffix.Bounds.Width > 0, "the published name was arranged at zero width");

            // Both live inside the card, and the suffix starts after the title rather than over it.
            double titleRight = title.TranslatePoint(new Avalonia.Point(title.Bounds.Width, 0), idcard)!.Value.X;
            double suffixLeft = suffix.TranslatePoint(default, idcard)!.Value.X;
            double suffixRight = suffixLeft + suffix.Bounds.Width;

            Assert.True(suffixLeft >= titleRight, "the published name overlaps the title");
            Assert.True(suffixRight <= idcard.Bounds.Width + 0.5,
                $"the published name runs to {suffixRight:F0} in a {idcard.Bounds.Width:F0}px card");
        }
        finally { window.Close(); }
    }

    private static ExplorerViewModel ExplorerFor(LoadedCookBook book)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        return new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
    }
}
