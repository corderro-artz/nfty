namespace Nfty.App.Models;

/// <summary>One row in the Landing screen's Recent list.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Meta">The subtitle line, e.g. "cookbook · 2 recipes".</param>
/// <param name="Path">Where it lives on disk.</param>
/// <param name="Loose">Whether it is a loose Recipe or Ingredient rather than a CookBook.</param>
public record RecentItem(string Name, string Meta, string Path, bool Loose);
