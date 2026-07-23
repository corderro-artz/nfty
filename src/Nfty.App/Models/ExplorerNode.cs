namespace Nfty.App.Models;

public enum ExplorerNodeKind { CookBook, Recipe, Ingredient }

/// <summary>One tree node. <see cref="Domain"/> carries the Core object this node stands for
/// (LoadedCookBook / LoadedRecipe / LoadedIngredient) so the detail views can bind real data.</summary>
public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind,
    IReadOnlyList<ExplorerNode> Children, object? Domain);
