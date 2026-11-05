using Nfty.Core.Model;

namespace Nfty.Core.Formats;

// Nullable annotations do not protect collection elements, or graphs constructed directly in C#.
// Establish their structural shape once at the reader/Validator boundary. Semantic legality stays
// in Validator; malformed graphs return these problems before semantic checks dereference them.
internal static class ManifestStructure
{
    internal static List<string> OfManifest(object? manifest)
    {
        var problems = new List<string>();
        CheckManifest(manifest, "manifest", problems);
        return problems;
    }

    internal static List<string> OfLoaded(object? loaded)
    {
        var problems = new List<string>();
        CheckLoaded(loaded, "loaded", problems);
        return problems;
    }

    private static void CheckLoaded(object? loaded, string path, List<string> problems)
    {
        if (!Present(loaded, path, problems)) return;
        switch (loaded)
        {
            case LoadedCookBook book:
                CheckManifest(book.Manifest, path + ".manifest", problems);
                foreach (var (recipe, at) in Items(book.Recipes, path + ".recipes", problems))
                    CheckLoaded(recipe, at, problems);
                break;
            case LoadedRecipe recipe:
                CheckManifest(recipe.Manifest, path + ".manifest", problems);
                foreach (var (ingredient, at) in Items(recipe.Ingredients, path + ".ingredients", problems))
                    CheckLoaded(ingredient, at, problems);
                break;
            case LoadedIngredient ingredient:
                CheckManifest(ingredient.Manifest, path + ".manifest", problems);
                if (Present(ingredient.VariantImages, path + ".variantImages", problems))
                    foreach (var (id, image) in ingredient.VariantImages)
                        Present(image, path + $".variantImages[{id}]", problems);
                if (Present(ingredient.Depths, path + ".depths", problems))
                    foreach (var (id, depth) in ingredient.Depths)
                        Present(depth, path + $".depths[{id}]", problems);
                break;
        }
    }

    private static void CheckManifest(object? manifest, string path, List<string> problems)
    {
        if (!Present(manifest, path, problems)) return;
        switch (manifest)
        {
            case CookBookManifest book:
                Identity(book.Id, book.Name, path, problems);
                Present(book.Canvas, path + ".canvas", problems);
                if (Present(book.Collection, path + ".collection", problems))
                {
                    Present(book.Collection.Name, path + ".collection.name", problems);
                    Present(book.Collection.Description, path + ".collection.description", problems);
                    Present(book.Collection.Symbol, path + ".collection.symbol", problems);
                }
                Present(book.RecipeWeights, path + ".recipeWeights", problems);
                if (book.RecipeOrder is not null) CheckStrings(book.RecipeOrder, path + ".recipeOrder", problems);
                if (book.Palette is not null) CheckStrings(book.Palette, path + ".palette", problems);
                break;
            case RecipeManifest recipe:
                Identity(recipe.Id, recipe.Name, path, problems);
                CheckStrings(recipe.LayerOrder, path + ".layerOrder", problems);
                foreach (var (rule, at) in Items(recipe.Rules, path + ".rules", problems))
                {
                    Target(rule.When, at + ".when", problems);
                    foreach (var (target, targetAt) in Items(rule.Targets, at + ".targets", problems))
                        Target(target, targetAt, problems);
                }
                break;
            case IngredientManifest ingredient:
                Identity(ingredient.Id, ingredient.Name, path, problems);
                foreach (var (variant, at) in Items(ingredient.Variants, path + ".variants", problems))
                    Identity(variant.Id, variant.Name, at, problems);
                if (ingredient.Colorization is { } colorization)
                    foreach (var _ in Items(colorization.Entries, path + ".colorization.entries", problems)) { }
                break;
            case KitchenManifest kitchen:
                Identity(kitchen.Id, kitchen.Name, path, problems);
                Present(kitchen.Description, path + ".description", problems);
                break;
        }
    }

    private static void Identity(string? id, string? name, string path, List<string> problems)
    {
        Present(id, path + ".id", problems);
        Present(name, path + ".name", problems);
    }

    private static void Target(RuleTarget? target, string path, List<string> problems)
    {
        if (!Present(target, path, problems)) return;
        Present(target.IngredientId, path + ".ingredientId", problems);
        Present(target.VariantId, path + ".variantId", problems);
    }

    private static void CheckStrings(IReadOnlyList<string>? values, string path, List<string> problems)
    {
        foreach (var _ in Items(values, path, problems)) { }
    }

    private static IEnumerable<(T Value, string Path)> Items<T>(IReadOnlyList<T>? values,
        string path, List<string> problems) where T : class
    {
        if (!Present(values, path, problems)) yield break;
        for (int i = 0; i < values.Count; i++)
        {
            string at = path + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            if (Present(values[i], at, problems)) yield return (values[i], at);
        }
    }

    private static bool Present([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? value,
        string path, List<string> problems)
    {
        if (value is not null) return true;
        problems.Add($"{path} must not be null.");
        return false;
    }
}
