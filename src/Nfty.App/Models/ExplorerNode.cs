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

/// <summary>One tree node. <see cref="Domain"/> carries the Core object this node stands for
/// (LoadedCookBook / LoadedRecipe / LoadedIngredient). <see cref="LayerKind"/> is the ingredient's
/// kind on Ingredient nodes (null otherwise), used to colour the tree kind mark.</summary>
public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind,
    IReadOnlyList<ExplorerNode> Children, object? Domain, LayerKind? LayerKind = null)
{
    /// <summary>Whether this ingredient rolls its colour per asset.</summary>
    public bool IsDynamic => LayerKind == Nfty.Core.Model.LayerKind.Dynamic;
    /// <summary>Whether this ingredient applies one fixed colour.</summary>
    public bool IsStatic => LayerKind == Nfty.Core.Model.LayerKind.Static;
    /// <summary>Whether this ingredient composites as-is.</summary>
    public bool IsCustom => LayerKind == Nfty.Core.Model.LayerKind.Custom;

    /// <summary>The single-letter kind mark the tree draws, or null on a non-ingredient node.</summary>
    public string? KindMark => LayerKind switch
    {
        Nfty.Core.Model.LayerKind.Dynamic => "D",
        Nfty.Core.Model.LayerKind.Static => "S",
        Nfty.Core.Model.LayerKind.Custom => "C",
        _ => null
    };

    /// <summary>True for the single top-level CookBook node. Drives the mono/SemiBold root label
    /// style and hides the branch guide line (a root has no parent branch to hang a guide off).</summary>
    public bool IsRoot => Kind == ExplorerNodeKind.CookBook;

    /// <summary>Kind predicates for the tree's 18px type mark, which every node carries.</summary>
    public bool IsRecipe => Kind == ExplorerNodeKind.Recipe;
    /// <summary>Whether this node is an Ingredient.</summary>
    public bool IsIngredient => Kind == ExplorerNodeKind.Ingredient;
}
