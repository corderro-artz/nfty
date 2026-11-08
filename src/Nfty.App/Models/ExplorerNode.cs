using Nfty.Core.Model;

namespace Nfty.App.Models;

/// <summary>What a tree node stands for.</summary>
public enum ExplorerNodeKind
{
    /// <summary>The single root node.</summary>
    CookBook,

    /// <summary>A Recipe under the root.</summary>
    Recipe,

    /// <summary>An Ingredient under a Recipe.</summary>
    Ingredient,
}

/// <summary>A tree projection borrowing its domain graph; expansion is mutable presentation state.</summary>
/// <param name="Id">The stable domain id, used to restore selection and expansion after a rebuild.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">Which domain level this node represents.</param>
/// <param name="Children">The next domain level in presentation order.</param>
/// <param name="Domain">A LoadedCookBook, LoadedRecipe, or (LoadedRecipe, LoadedIngredient) pair.</param>
/// <param name="LayerKind">The ingredient kind, or null for a non-ingredient node.</param>
/// <param name="IsTwoDOnly">A listing projection recomputed from decoded partial-alpha pixels, never an authored setting.</param>
public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind,
    IReadOnlyList<ExplorerNode> Children, object? Domain, LayerKind? LayerKind = null,
    bool IsTwoDOnly = false)
{
    // IsTwoDOnly: true on the book and on each ingredient whose DECODED pixels carry partial alpha —
    // recomputed on every tree rebuild, never read from the stored manifest flag. The tree draws it
    // as a mark in a reserved slot.

    /// <summary>
    /// Whether this branch is open in the tree. Mutable and NOT part of the node's identity: a
    /// rebuild (a save, a reorder) constructs an entirely new tree, so the Explorer carries the open
    /// branches across by id the same way it carries the selection. Without it, saving a layer
    /// dropped the author back to a fully collapsed root three levels from where they were working.
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>Whether this ingredient rolls its color per asset.</summary>
    public bool IsDynamic => LayerKind == Nfty.Core.Model.LayerKind.Dynamic;
    /// <summary>Whether this ingredient applies one fixed color.</summary>
    public bool IsStatic => LayerKind == Nfty.Core.Model.LayerKind.Static;
    /// <summary>Whether this ingredient composites as-is.</summary>
    public bool IsCustom => LayerKind == Nfty.Core.Model.LayerKind.Custom;

    /// <summary>The single-letter kind mark the tree draws, or null on a non-ingredient node.</summary>
    public string? KindMark => LayerKindMark.For(LayerKind);

    /// <summary>True for the single top-level CookBook node. Drives the mono/SemiBold root label
    /// style and hides the branch guide line (a root has no parent branch to hang a guide off).</summary>
    public bool IsRoot => Kind == ExplorerNodeKind.CookBook;

    /// <summary>Kind predicates for the tree's 18px type mark, which every node carries.</summary>
    public bool IsRecipe => Kind == ExplorerNodeKind.Recipe;
    /// <summary>Whether this node is an Ingredient.</summary>
    public bool IsIngredient => Kind == ExplorerNodeKind.Ingredient;
}
