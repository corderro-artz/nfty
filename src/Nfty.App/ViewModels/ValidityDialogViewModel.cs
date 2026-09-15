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
        // Named rather than counted-and-left: "checked 2 recipes" says the check ran AND how much of
        // the book it covered, which is what makes a green light mean anything.
        Summary = IsValid
            ? $"Checked {Plural(recipes, "recipe")}, {Plural(layers, "layer")} and "
              + $"{Plural(variants, "variant")} — ids, layer order, canvas sizes, colorization kinds, "
              + "rules and trait names. Nothing to report."
            : $"Across {Plural(recipes, "recipe")}, {Plural(layers, "layer")} and "
              + $"{Plural(variants, "variant")}. Cooking is refused until these are fixed.";
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Dismisses the dialog.</summary>
    [RelayCommand] private void Close() => _dialogs.Close(null);
}

/// <summary>One reported problem, with the 1-based position <c>validate</c> lists it at.</summary>
/// <param name="Number">Its position in the report.</param>
/// <param name="Text">Validator's own message, verbatim.</param>
public record ValidityProblem(int Number, string Text);
