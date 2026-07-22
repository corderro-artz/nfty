namespace Nfty.App.Models;

public enum ExplorerNodeKind { CookBook, Recipe, Ingredient }

public record ExplorerNode(string Id, string Name, ExplorerNodeKind Kind, IReadOnlyList<ExplorerNode> Children);
