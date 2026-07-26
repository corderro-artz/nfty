using Nfty.Core.Model;

namespace Nfty.App.Models;

public enum ExplorerNodeKind { CookBook, Recipe, Ingredient }

/// <summary>One tree node. <see cref="Domain"/> carries the Core object this node stands for
/// (LoadedCookBook / LoadedRecipe / LoadedIngredient). <see cref="LayerKind"/> is the ingredient's
/// kind on Ingredient nodes (null otherwise), used to colour the tree kind mark.</summary>
public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind,
    IReadOnlyList<ExplorerNode> Children, object? Domain, LayerKind? LayerKind = null)
{
    public bool IsDynamic => LayerKind == Nfty.Core.Model.LayerKind.Dynamic;
    public bool IsStatic => LayerKind == Nfty.Core.Model.LayerKind.Static;
    public bool IsCustom => LayerKind == Nfty.Core.Model.LayerKind.Custom;

    /// <summary>True for the single top-level CookBook node. Drives the mono/SemiBold root label
    /// style and hides the branch guide line (a root has no parent branch to hang a guide off).</summary>
    public bool IsRoot => Kind == ExplorerNodeKind.CookBook;
}
