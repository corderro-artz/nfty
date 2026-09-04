using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>Which of the three things a Kitchen holds a shelf card stands for.</summary>
public enum KitchenItemKind
{
    /// <summary>A <c>.cbk</c> — opens the Explorer.</summary>
    CookBook,

    /// <summary>A loose <c>.rcp</c>.</summary>
    Recipe,

    /// <summary>A loose <c>.igt</c> — opens the Ingredient Editor.</summary>
    Ingredient,
}

/// <summary>One card on the Kitchen shelf.</summary>
/// <remarks>
/// <see cref="Meta"/> comes from a manifest <em>peek</em> — the outer manifest only, no image decoded
/// — which is what lets a card say "3 recipes · 1000×1000" without the listing pulling the whole
/// workspace into memory. A file that will not peek keeps its row and loses only its subtitle: one
/// unreadable archive must not blank the shelf it is sitting on.
/// </remarks>
/// <param name="Path">Full path to the archive.</param>
/// <param name="Name">Display name — the manifest's, or the file name when it would not peek.</param>
/// <param name="Meta">The subtitle line, or a short reason when the peek failed.</param>
/// <param name="Kind">Which of the three kinds this is.</param>
/// <param name="Readable">False when the peek failed; the card still opens, and the open reports properly.</param>
public sealed record KitchenCard(string Path, string Name, string Meta, KitchenItemKind Kind, bool Readable = true)
{
    /// <summary>The file name, for the tooltip — the shelf shows names, but a workspace can hold two
    /// archives whose manifests agree on a name.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Whether this is a loose part rather than a CookBook. Drives the muted tile treatment
    /// the Recent list already gives a loose item.</summary>
    public bool IsLoose => Kind != KitchenItemKind.CookBook;

    /// <summary>Whether this card is a CookBook — one of three predicates off the single stored
    /// <see cref="Kind"/>, the same shape <c>ExplorerNode</c> uses, so the tile's glyph is chosen
    /// without a converter and without a second field to keep in agreement.</summary>
    public bool IsCookBook => Kind == KitchenItemKind.CookBook;
    /// <summary>Whether this card is a loose Recipe.</summary>
    public bool IsRecipe => Kind == KitchenItemKind.Recipe;
    /// <summary>Whether this card is a loose Ingredient.</summary>
    public bool IsIngredient => Kind == KitchenItemKind.Ingredient;
}

/// <summary>One page of the shelf: the cards, and where it sits in its kind.</summary>
/// <param name="Kind">The heading this page is filed under.</param>
/// <param name="KindLabel">That heading as the shelf prints it.</param>
/// <param name="PageOfKind">1-based page number within the kind.</param>
/// <param name="PagesInKind">How many pages that kind has.</param>
/// <param name="Cards">The cards on this page.</param>
public sealed record KitchenPage(KitchenItemKind Kind, string KindLabel, int PageOfKind, int PagesInKind,
    IReadOnlyList<KitchenCard> Cards);

/// <summary>
/// The Landing screen's Kitchen shelf: one row of cards, paged by kind.
/// </summary>
/// <remarks>
/// <para><b>One flat page sequence.</b> The pages of every kind are concatenated in scan order —
/// CookBooks, then Recipes, then Ingredients — so moving from the last CookBook page into the first
/// Recipe page is the same gesture as moving within a kind. There is no second control for "change
/// kind", because there is no second thing to do.</para>
///
/// <para><b>Page size is the view's to say.</b> Cards fill the row, so how many fit is a function of
/// the rendered width, which only the view knows. It sets <see cref="PageSize"/>; this repaginates
/// and keeps the reader roughly where they were rather than snapping to the start — resizing a
/// window should not lose your place.</para>
///
/// <para><b>The band never changes size.</b> Which of <see cref="HasCards"/> /
/// <see cref="ShowNoKitchen"/> / <see cref="ShowEmptyKitchen"/> is true swaps the ink inside a box of
/// one fixed height. It is also why the shelf carries no Open-Kitchen button of its own: those live
/// in the Create and Open groups a few inches to the left, and two controls for one action a few
/// inches apart is worse than one.</para>
/// </remarks>
public partial class KitchenShelfViewModel : ObservableObject
{
    private IReadOnlyList<KitchenCard> _cards = Array.Empty<KitchenCard>();
    private IReadOnlyList<KitchenPage> _pages = Array.Empty<KitchenPage>();

    private readonly Action<KitchenCard>? _open;

    /// <summary>Creates the shelf.</summary>
    /// <param name="open">Invoked when a card is activated; null in a fixture that only inspects paging.</param>
    public KitchenShelfViewModel(Action<KitchenCard>? open = null) => _open = open;

    /// <summary>The open workspace's name, or null when none is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoKitchen))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyKitchen))]
    [NotifyPropertyChangedFor(nameof(WhereText))]
    private string? _kitchenName;

    /// <summary>How many cards fit one row. The view measures and sets this; below one is treated as
    /// one, so a pathologically narrow window still shows something rather than dividing by zero.</summary>
    [ObservableProperty] private int _pageSize = 5;

    /// <summary>The cards on the current page, padded to <see cref="PageSize"/> with empty slots.</summary>
    public ObservableCollection<KitchenCard?> Row { get; } = new();

    /// <summary>Index of the page on screen.</summary>
    [ObservableProperty] private int _pageIndex;

    /// <summary>Whether there is anything to page through.</summary>
    public bool HasCards => _pages.Count > 0;

    /// <summary>No workspace is open at all.</summary>
    public bool ShowNoKitchen => KitchenName is null;

    /// <summary>A workspace is open and holds nothing — a fresh one, not a broken one.</summary>
    public bool ShowEmptyKitchen => KitchenName is not null && !HasCards;

    /// <summary>The head's middle line: which workspace, and which kind is on screen.</summary>
    public string WhereText => KitchenName is null
        ? "· no workspace open"
        : HasCards
            ? $"· {KitchenName} · {Current?.KindLabel}"
            : $"· {KitchenName} · empty";

    /// <summary>"page 1 of 2" for the kind on screen, or empty when there is nothing to page.</summary>
    public string PageText => Current is { } p ? $"page {p.PageOfKind} of {p.PagesInKind}" : "";

    /// <summary>Whether paging back is possible — the chevron keeps its box either way and loses only
    /// its ink, so nothing beside it moves at the ends.</summary>
    public bool CanPrev => PageIndex > 0;

    /// <summary>Whether paging forward is possible.</summary>
    public bool CanNext => PageIndex < _pages.Count - 1;

    private KitchenPage? Current => PageIndex >= 0 && PageIndex < _pages.Count ? _pages[PageIndex] : null;

    /// <summary>Replaces what the shelf holds and returns to the first page.</summary>
    /// <param name="kitchenName">The workspace's name, or null when none is open.</param>
    /// <param name="cards">Its contents, already in the order they should read.</param>
    public void Load(string? kitchenName, IReadOnlyList<KitchenCard> cards)
    {
        _cards = cards;
        KitchenName = kitchenName;
        Repaginate(keepFirstCard: null);
        PageIndex = 0;
        Refresh();
    }

    partial void OnPageSizeChanged(int value)
    {
        // Keep the reader on the card they were looking at rather than snapping to the start: a
        // window resize repaginates, and losing your place on a resize is its own small bug.
        var anchor = Current?.Cards.FirstOrDefault();
        Repaginate(keepFirstCard: anchor);
        Refresh();
    }

    partial void OnPageIndexChanged(int value) => Refresh();

    /// <summary>Moves by whole pages, clamped. Clamping rather than wrapping: a shelf that jumped from
    /// the last Ingredient back to the first CookBook would make "am I at the end?" unanswerable.</summary>
    /// <param name="delta">How many pages to move, signed.</param>
    public void Page(int delta)
    {
        if (_pages.Count == 0) return;
        PageIndex = Math.Clamp(PageIndex + delta, 0, _pages.Count - 1);
    }

    [RelayCommand] private void Next() => Page(1);
    [RelayCommand] private void Prev() => Page(-1);

    /// <summary>Opens a card. Null-tolerant because the row is padded with empty slots.</summary>
    /// <param name="card">The card activated, or null for a padding slot.</param>
    [RelayCommand]
    private void OpenCard(KitchenCard? card)
    {
        if (card is not null) _open?.Invoke(card);
    }

    private void Repaginate(KitchenCard? keepFirstCard)
    {
        int per = Math.Max(1, PageSize);
        var pages = new List<KitchenPage>();

        foreach (var kind in new[] { KitchenItemKind.CookBook, KitchenItemKind.Recipe, KitchenItemKind.Ingredient })
        {
            var ofKind = _cards.Where(c => c.Kind == kind).ToList();
            if (ofKind.Count == 0) continue;                       // a kind with nothing in it is not a page
            int total = (ofKind.Count + per - 1) / per;
            for (int i = 0, n = 1; i < ofKind.Count; i += per, n++)
                pages.Add(new KitchenPage(kind, Label(kind), n, total,
                    ofKind.Skip(i).Take(per).ToList()));
        }

        _pages = pages;

        if (keepFirstCard is not null)
        {
            int found = pages.FindIndex(p => p.Cards.Contains(keepFirstCard));
            PageIndex = found >= 0 ? found : Math.Clamp(PageIndex, 0, Math.Max(0, pages.Count - 1));
        }
        else PageIndex = Math.Clamp(PageIndex, 0, Math.Max(0, pages.Count - 1));
    }

    private static string Label(KitchenItemKind kind) => kind switch
    {
        KitchenItemKind.CookBook => "CookBooks",
        KitchenItemKind.Recipe => "Recipes",
        _ => "Ingredients",
    };

    private void Refresh()
    {
        Row.Clear();
        // Pad to a full row. A short last page keeping its empty slots is what stops the cards
        // re-spacing themselves mid-sequence — the row is one shape however few cards are on it.
        var cards = Current?.Cards ?? Array.Empty<KitchenCard>();
        foreach (var c in cards) Row.Add(c);
        for (int i = cards.Count; i < Math.Max(1, PageSize); i++) Row.Add(null);

        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(ShowNoKitchen));
        OnPropertyChanged(nameof(ShowEmptyKitchen));
        OnPropertyChanged(nameof(WhereText));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
        NextCommand.NotifyCanExecuteChanged();
        PrevCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Turns a scanned workspace into cards, peeking each archive for its subtitle.
    /// </summary>
    /// <remarks>
    /// Order is the scan's own — <c>KitchenContents</c> sorts every list ordinally — grouped
    /// CookBooks, Recipes, Ingredients. A peek that throws costs that one card its subtitle and
    /// nothing else: an unreadable file in a folder is a thing to see in the listing, not a reason
    /// for the listing to fail.
    /// </remarks>
    /// <param name="contents">The scanned workspace.</param>
    /// <returns>Its cards, ready to page.</returns>
    public static IReadOnlyList<KitchenCard> CardsFor(KitchenContents contents)
    {
        var cards = new List<KitchenCard>();
        foreach (var p in contents.CookBooks) cards.Add(Peek(p, KitchenItemKind.CookBook));
        foreach (var p in contents.Recipes) cards.Add(Peek(p, KitchenItemKind.Recipe));
        foreach (var p in contents.Ingredients) cards.Add(Peek(p, KitchenItemKind.Ingredient));
        return cards;
    }

    private static KitchenCard Peek(string path, KitchenItemKind kind)
    {
        try
        {
            return kind switch
            {
                KitchenItemKind.CookBook => FromBook(path, ArchivePeek.CookBook(path)),
                KitchenItemKind.Recipe => FromRecipe(path, ArchivePeek.Recipe(path)),
                _ => FromIngredient(path, ArchivePeek.Ingredient(path)),
            };
        }
        catch (Exception)
        {
            // Unreadable, out of date, or not really an archive. The card STAYS so the file is
            // visible in its own workspace, and says so rather than pretending to a subtitle it does
            // not have. Catch-all on purpose: a listing has no business deciding which kinds of
            // broken file are allowed to take it down.
            return new KitchenCard(path, Path.GetFileNameWithoutExtension(path),
                "could not be read", kind, Readable: false);
        }
    }

    private static KitchenCard FromBook(string path, CookBookManifest m) =>
        new(path, m.Name,
            $"{Count(m.RecipeWeights.Count, "recipe")} · {m.Canvas.Width}×{m.Canvas.Height}",
            KitchenItemKind.CookBook);

    private static KitchenCard FromRecipe(string path, RecipeManifest m) =>
        new(path, m.Name, Count(m.LayerOrder.Count, "layer"), KitchenItemKind.Recipe);

    private static KitchenCard FromIngredient(string path, IngredientManifest m) =>
        new(path, m.Name,
            $"{m.Kind.ToString().ToLowerInvariant()} · {Count(m.Variants.Count, "variant")}",
            KitchenItemKind.Ingredient);

    private static string Count(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";
}
