using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A SCREEN THAT REPORTS A COUNT OWES THE READER THE THING IT COUNTED.
/// </summary>
/// <remarks>
/// <para>The status bar said "1 problem" and put the problems on a TOOLTIP, where a list of
/// sentences cannot be read, copied or scrolled. The CookBook card listed four and then said "…and
/// N more", which is right for a card — an unbounded list pushes the mint bar and Cook off it — and
/// is a truncation with nowhere to go. Past that, the only way to see the rest was to run the CLI's
/// <c>validate</c>.</para>
///
/// <para>Both counts are buttons now and both open the same report. It opens on a VALID book too
/// and says what was checked: a green light nobody can interrogate is a green light nobody should
/// trust, and a dialog that opened empty would read as a broken control rather than a clean book.
/// </para>
/// </remarks>
public class ValidityReportTests
{
    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(8, 8) },
    };

    /// <summary>A book whose recipe stacks two layers it does not carry — two Validator problems,
    /// so "problems" is plural and the list has something to number.</summary>
    private static LoadedCookBook BrokenBook()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "missing-one", "missing-two" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing("bg") },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(8, 8),
                new Collection("VaporPets", "", "VP"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
    }

    private static ExplorerViewModel Explorer(LoadedCookBook book, FakeDialogs dialogs)
    {
        var nav = new FakeNav();
        var session = new CookBookSession();
        return new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
    }

    // ---- the report itself -------------------------------------------------------------------

    [AvaloniaFact]
    public void The_report_numbers_every_problem_the_validator_found()
    {
        var book = BrokenBook();
        var problems = Validator.Validate(book);
        Assert.True(problems.Count >= 2, "the fixture stopped being broken in more than one way");

        var vm = new ValidityDialogViewModel(new FakeDialogs(), book, problems);

        Assert.False(vm.IsValid);
        Assert.True(vm.HasProblems);
        Assert.Equal($"{problems.Count} problems", vm.Title);
        Assert.Equal("VaporPets", vm.BookName);

        // EVERY problem, not the card's four — and numbered as `validate` lists them.
        Assert.Equal(problems.Count, vm.Problems.Count);
        Assert.Equal(Enumerable.Range(1, problems.Count), vm.Problems.Select(p => p.Number));
        Assert.Equal(problems, vm.Problems.Select(p => p.Text));
        Assert.Contains("Cooking is refused", vm.Summary, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void One_problem_is_singular()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new ValidityDialogViewModel(new FakeDialogs(), book, new[] { "just the one" });
        Assert.Equal("1 problem", vm.Title);
    }

    /// <summary>
    /// A VALID BOOK STILL HAS SOMETHING TO SAY. An empty dialog reads as a broken control rather
    /// than a clean book, so the valid branch names what was checked and how much of it.
    /// </summary>
    [AvaloniaFact]
    public void A_valid_book_reports_what_was_checked()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        Assert.Empty(Validator.Validate(book));

        var vm = new ValidityDialogViewModel(new FakeDialogs(), book, Array.Empty<string>());

        Assert.True(vm.IsValid);
        Assert.False(vm.HasProblems);
        Assert.Equal("Valid", vm.Title);
        Assert.Empty(vm.Problems);
        Assert.Contains("Checked", vm.Summary, StringComparison.Ordinal);
        Assert.Contains("3 layers", vm.Summary, StringComparison.Ordinal);      // bg, aura, body
        Assert.Contains("Nothing to report", vm.Summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// A BRAND-NEW BOOK IS NOT A BROKEN ONE, AND THE DIALOG SAYS SO FIRST.
    /// </summary>
    /// <remarks>
    /// An empty CookBook validates as "CookBook has zero total recipe weight" — true, and what the
    /// CLI prints, and exactly what reads to a first-time author as an error they caused on a book
    /// where they have not done anything yet. The guidance is derived from the GRAPH rather than
    /// matched against Validator's wording, so a reworded message cannot silently stop being
    /// recognised.
    /// </remarks>
    [AvaloniaFact]
    public void A_cookbook_with_no_recipes_is_told_to_add_one()
    {
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Fresh", new Dimensions(8, 8),
                new Collection("Fresh", "", "F"), new Dictionary<string, double>()),
            Recipes = Array.Empty<LoadedRecipe>(),
        };
        var problems = Validator.Validate(book);
        Assert.NotEmpty(problems);          // it really does report something

        var vm = new ValidityDialogViewModel(new FakeDialogs(), book, problems);

        Assert.True(vm.IsUnstarted);
        Assert.False(vm.IsBroken);          // no warning ink on a book nobody has filled in
        Assert.NotNull(vm.Guidance);
        Assert.Contains("no Recipes yet", vm.Guidance!, StringComparison.Ordinal);
        Assert.Contains("Nothing below is a mistake you made", vm.Guidance!, StringComparison.Ordinal);
        // And the footer stops accusing: "cooking is refused until these are fixed" is true and
        // reads as a telling-off on a book nobody has started.
        Assert.Contains("nothing to cook yet", vm.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", vm.Summary, StringComparison.Ordinal);
    }

    /// <summary>The same for a Recipe nobody has added a layer to — and it NAMES the recipe, because
    /// "add an Ingredient" without saying where is barely better than the raw problem.</summary>
    [AvaloniaFact]
    public void A_recipe_with_no_layers_is_named_and_explained()
    {
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", Array.Empty<string>(),
                Array.Empty<IncompatibilityRule>()),
            Ingredients = Array.Empty<LoadedIngredient>(),
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Fresh", new Dimensions(8, 8),
                new Collection("Fresh", "", "F"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
        var vm = new ValidityDialogViewModel(new FakeDialogs(), book, Validator.Validate(book));

        Assert.True(vm.IsUnstarted);
        Assert.Contains("“Cat”", vm.Guidance!, StringComparison.Ordinal);
        Assert.Contains("Add an Ingredient", vm.Guidance!, StringComparison.Ordinal);
    }

    /// <summary>A book that is broken for a REAL reason gets no reassurance and keeps the warning
    /// ink — the guidance must not become a blanket excuse for every problem.</summary>
    [AvaloniaFact]
    public void A_genuinely_broken_book_is_not_excused()
    {
        var book = BrokenBook();        // stacks two layers it does not carry
        var vm = new ValidityDialogViewModel(new FakeDialogs(), book, Validator.Validate(book));

        Assert.False(vm.IsUnstarted);
        Assert.True(vm.IsBroken);
        Assert.Null(vm.Guidance);
    }

    // ---- the two ways in ---------------------------------------------------------------------

    [AvaloniaFact]
    public async System.Threading.Tasks.Task The_status_bar_chip_opens_the_report()
    {
        var dialogs = new FakeDialogs();
        using var vm = Explorer(BrokenBook(), dialogs);

        Assert.False(vm.IsValid);
        await vm.ShowValidityCommand.ExecuteAsync(null);

        var shown = Assert.IsType<ValidityDialogViewModel>(dialogs.Active);
        Assert.Equal(vm.ValidityText, shown.Title);
    }

    /// <summary>The card's own chip opens the SAME report — the two state the same count, and a
    /// reader who clicks either is asking the same question.</summary>
    [AvaloniaFact]
    public void The_cookbook_cards_chip_opens_the_same_report()
    {
        int opened = 0;
        var card = new CookBookDetailViewModel(BrokenBook(), () => { }, null, () => opened++);

        Assert.True(card.ShowValidityCommand.CanExecute(null));
        card.ShowValidityCommand.Execute(null);
        Assert.Equal(1, opened);
    }

    /// <summary>A card built with no dialog layer — every fixture — leaves the chip inert rather
    /// than offering a control that does nothing.</summary>
    [AvaloniaFact]
    public void A_card_with_nowhere_to_open_leaves_the_chip_disabled()
    {
        var card = new CookBookDetailViewModel(BrokenBook(), () => { });
        Assert.False(card.ShowValidityCommand.CanExecute(null));
    }

    /// <summary>It is live on a VALID book too. The question a green light invites is "what did it
    /// actually check?", and until now nothing answered it.</summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task The_report_opens_on_a_healthy_book_as_well()
    {
        var dialogs = new FakeDialogs();
        using var vm = Explorer(ExplorerViewModelTests.TwoRecipeBook(), dialogs);

        Assert.True(vm.IsValid);
        await vm.ShowValidityCommand.ExecuteAsync(null);

        var shown = Assert.IsType<ValidityDialogViewModel>(dialogs.Active);
        Assert.True(shown.IsValid);
        Assert.Contains("Checked", shown.Summary, StringComparison.Ordinal);
    }

    // ---- the chip is really a button ----------------------------------------------------------

    /// <summary>The card's status chip is a Button on a rendered frame, not a Border that merely has
    /// a command bound somewhere — the defect the rules panel's unreachable rows already were.</summary>
    [AvaloniaFact]
    public void The_cards_status_chip_renders_as_a_pressable_control()
    {
        var card = new CookBookDetailViewModel(BrokenBook(), () => { }, null, () => { });
        var view = new Views.CookBookDetailView { DataContext = card };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var chip = view.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => b.Classes.Contains("idchip"));

            Assert.NotNull(chip);
            Assert.True(chip!.IsEffectivelyEnabled);
            Assert.True(chip.Bounds.Width > 0 && chip.Bounds.Height > 0);
        }
        finally { window.Close(); }
    }
}
