using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>
/// What <see cref="Validator"/> found in the open CookBook, in full.
/// </summary>
/// <remarks>
/// <para>The status bar said "3 problems" and the card listed four of them; beyond that the only way
/// to read the rest was to run the CLI's <c>validate</c> command. A screen that reports a COUNT owes
/// the reader the thing it counted — the card already learned that, and this is the other half: the
/// count is a button now, on both surfaces, and it opens the whole list.</para>
///
/// <para><b>It says something in the valid case too.</b> A dialog that opens empty on a healthy book
/// teaches the reader that the control is broken rather than that the book is fine, so the valid
/// branch names what was actually checked and how big the thing checked was. That is also the
/// answer to "is this even running?", which is the question a green light always invites.</para>
///
/// <para>The problems are <see cref="Validator"/>'s own strings, verbatim and selectable. They are
/// written to be read by a person — it is the same text <c>validate</c> prints — and re-typing one
/// off a screenshot is how detail gets lost.</para>
/// </remarks>
public partial class ValidityDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    /// <summary>Whether the book validates.</summary>
    public bool IsValid { get; }

    /// <summary>Whether there is a list to draw — the inverse of <see cref="IsValid"/>, for markup
    /// that cannot negate.</summary>
    public bool HasProblems => !IsValid;

    /// <summary>The dialog's heading: "Valid" or the problem count.</summary>
    public string Title { get; }

    /// <summary>The book this is about, so the dialog stands on its own.</summary>
    public string BookName { get; }

    /// <summary>The problems, numbered as <c>validate</c> lists them.</summary>
    public IReadOnlyList<ValidityProblem> Problems { get; }

    /// <summary>What was checked, and how much of it — the valid case's content, and context for the
    /// invalid one.</summary>
    public string Summary { get; }

    /// <summary>
    /// The sentence a HALF-BUILT book gets, or null for one that is genuinely broken.
    /// </summary>
    /// <remarks>
    /// <para>A brand-new CookBook validates as "CookBook has zero total recipe weight", and a
    /// brand-new Recipe as "has an empty layerOrder, so it would generate a fully-transparent
    /// asset". Both are true, both are what the CLI prints, and both read to a first-time author as
    /// an ERROR THEY CAUSED - on a book where they have not done anything yet and the only actual
    /// news is "add something". Validator's own strings are not the place to fix that: they are the
    /// CLI's too, and Core is the single source for how a problem is worded.</para>
    ///
    /// <para>So the dialog leads with the explanation, and it is derived from the BOOK rather than
    /// matched against those strings - a message that changes wording must not silently stop being
    /// recognised. An unstarted book also gets the neutral dot instead of the warning one: nothing
    /// is wrong with it, it is just not finished.</para>
    /// </remarks>
    public string? Guidance { get; }

    /// <summary>Whether there is a guidance line to draw.</summary>
    public bool HasGuidance => Guidance is not null;

    /// <summary>Whether the only thing wrong is that the book has not been filled in yet — which is
    /// not a fault, and should not wear the fault's colour.</summary>
    public bool IsUnstarted => Guidance is not null;

    /// <summary>Whether this is a genuinely broken book rather than an unfinished one — the state
    /// that earns the warning ink.</summary>
    public bool IsBroken => HasProblems && !IsUnstarted;

    /// <summary>Creates the dialog over a book's problems.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="book">The open book, for its name and its size.</param>
    /// <param name="problems">What <see cref="Validator"/> reported; empty means valid.</param>
    public ValidityDialogViewModel(IDialogService dialogs, LoadedCookBook book,
        IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(problems);
        _dialogs = dialogs;

        IsValid = problems.Count == 0;
        BookName = book.Manifest.Name;
        Title = IsValid ? "Valid"
            : problems.Count == 1 ? "1 problem" : $"{problems.Count} problems";
        Problems = problems.Select((p, i) => new ValidityProblem(i + 1, p)).ToArray();

        int recipes = book.Recipes.Count;
        int layers = book.Recipes.Sum(r => r.Ingredients.Count);
        int variants = book.Recipes.Sum(r => r.Ingredients.Sum(i => i.Manifest.Variants.Count));
        // Derived from the graph, never from Validator's wording. Empty recipes are named so the
        // reader knows WHICH one to go and fill in: on a book with three recipes and one empty,
        // "add an Ingredient" without saying where is barely better than the raw problem.
        var empty = book.Recipes.Where(r => r.Manifest.LayerOrder.Count == 0)
            .Select(r => r.Manifest.Name).ToArray();
        Guidance = IsValid ? null
            : book.Recipes.Count == 0
                ? "This CookBook has no Recipes yet. Add one, then add Ingredients to it — until "
                  + "there is something to stack, there is nothing to cook. Nothing below is a "
                  + "mistake you made."
            : empty.Length == book.Recipes.Count
                ? $"{Plural(empty.Length, "Recipe")} with no layers. Add an Ingredient to "
                  + $"{Join(empty)} and there is something to composite — until then every asset "
                  + "would be fully transparent. Nothing below is a mistake you made."
            : empty.Length > 0
                ? $"{Join(empty)} has no layers yet, so it would generate a fully-transparent asset. "
                  + "Add an Ingredient to it. Anything else listed below is a real problem."
                : null;

        // Named rather than counted-and-left: "checked 2 recipes" says the check ran AND how much of
        // the book it covered, which is what makes a green light mean anything.
        Summary = IsValid
            ? $"Checked {Plural(recipes, "recipe")}, {Plural(layers, "layer")} and "
              + $"{Plural(variants, "variant")} — ids, layer order, canvas sizes, colorization kinds, "
              + "rules and trait names. Nothing to report."
            : $"Across {Plural(recipes, "recipe")}, {Plural(layers, "layer")} and "
              + $"{Plural(variants, "variant")}. "
              // Both sentences are TRUE - you genuinely cannot cook an empty book. The split is
              // about what the line is FOR. On an unstarted book the reader has just been told
              // exactly what to do one line above, so a refusal underneath adds no information and
              // only lands as a telling-off; on a broken one the refusal IS the news.
              + (Guidance is null ? "Cooking is refused until these are fixed."
                                  : "There is nothing to cook yet.");
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Names in quotes, as a list a person would say out loud.</summary>
    private static string Join(IReadOnlyList<string> names) =>
        names.Count == 1 ? $"“{names[0]}”"
        : names.Count == 2 ? $"“{names[0]}” and “{names[1]}”"
        : string.Join(", ", names.Take(names.Count - 1).Select(n => $"“{n}”"))
          + $" and “{names[^1]}”";

    /// <summary>Dismisses the dialog.</summary>
    [RelayCommand] private void Close() => _dialogs.Close(null);
}

/// <summary>One reported problem, with the 1-based position <c>validate</c> lists it at.</summary>
/// <param name="Number">Its position in the report.</param>
/// <param name="Text">Validator's own message, verbatim.</param>
public record ValidityProblem(int Number, string Text);
