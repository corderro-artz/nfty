using System.CommandLine;
using System.Globalization;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using static Nfty.Cli.CliGuards;

namespace Nfty.Cli;

/// <summary>
/// Authoring parity: the edits the app makes to a book that the command line could not. Every one is
/// a thin wrapper over the Core call the GUI uses (<see cref="CookBookEdits"/>, <see cref="VoxelReady"/>),
/// validates with <see cref="Validator"/> before it writes, and writes through <c>AtomicFile</c>.
/// </summary>
public static partial class CommandFactory
{
    private static Option<string?> OptionalText(string name, string description) =>
        new(name) { Description = description };

    /// <summary>Weights are numbers a person types; NaN or infinity would pass a bare range check.</summary>
    private static Option<double> WeightOption(string description, bool allowZero)
    {
        var weight = new Option<double>("--weight") { Description = description, Required = true };
        weight.Validators.Add(r =>
        {
            double v = r.GetValueOrDefault<double>();
            if (!double.IsFinite(v) || v < 0 || (!allowZero && v == 0))
                r.AddError(allowZero
                    ? "--weight must be zero or greater (0 shelves it)."
                    : "--weight must be greater than zero.");
        });
        return weight;
    }

    /// <summary>Which of the allowed kinds <paramref name="path"/> is; anything else is refused by name.</summary>
    private static ArchiveKind ExpectKind(string path, params ArchiveKind[] allowed)
    {
        var kind = Archives.KindOf(path);
        if (!allowed.Contains(kind))
            throw new InvalidOperationException(
                $"Expected {string.Join(" or ", allowed.Select(k => KindWord(k)))}, got '{path}'.");
        return kind;
    }

    private static string KindWord(ArchiveKind kind) => kind switch
    {
        ArchiveKind.CookBook => ".cbk",
        ArchiveKind.Recipe => ".rcp",
        ArchiveKind.Ingredient => ".igt",
        _ => kind.ToString(),
    };

    /// <summary>Reads a book, applies <paramref name="edit"/>, validates, writes it back and prints
    /// the confirmation. Returns 1 (problems printed) rather than writing an illegal book.</summary>
    private static int EditBook(string path, Func<LoadedCookBook, LoadedCookBook> edit,
        Func<LoadedCookBook, string> confirmation)
    {
        using var book = CookBookArchive.Read(path);
        var edited = edit(book);
        var problems = Validator.Validate(edited);
        if (problems.Count > 0) { Report(problems); return 1; }
        WriteReplacing(path, p => CookBookArchive.Write(p, edited.Manifest, edited.Recipes));
        Console.WriteLine(confirmation(edited));
        return 0;
    }

    private static Command RemoveIngredient()
    {
        var file = new Argument<string>("file") { Description = "Path to the .cbk or .rcp to modify in place." };
        var recipe = OptionalText("--recipe", "Recipe id holding the layer. Required on a .cbk, rejected on a .rcp.");
        var id = new Option<string>("--id") { Description = "Ingredient id of the layer to remove.", Required = true };
        var cmd = new Command("ingredient", "Remove a layer from a .cbk's recipe or from a .rcp.")
            { file, recipe, id };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string ingredientId = parse.GetValue(id)!;
            string? recipeId = parse.GetValue(recipe);
            if (ExpectKind(path, ArchiveKind.CookBook, ArchiveKind.Recipe) == ArchiveKind.CookBook)
            {
                RequireGiven(recipeId, "--recipe", "when removing from a .cbk: it names the recipe that holds the layer.");
                return EditBook(path, b => CookBookEdits.RemoveIngredient(b, recipeId!, ingredientId),
                    _ => $"Removed ingredient '{ingredientId}' from recipe '{recipeId}' in {path}");
            }

            RejectGiven(recipeId, "--recipe", "applies to a .cbk; a .rcp is the recipe.");
            using var rcp = RecipeArchive.Read(path);
            if (rcp.Ingredients.All(i => i.Manifest.Id != ingredientId))
                throw new KeyNotFoundException($"No ingredient '{ingredientId}' in recipe '{rcp.Manifest.Id}'.");
            var manifest = CookBookEdits.WithoutLayer(rcp.Manifest, ingredientId);
            var kept = rcp.Ingredients.Where(i => i.Manifest.Id != ingredientId).ToList();
            var problems = Validator.ValidateRecipe(new LoadedRecipe { Manifest = manifest, Ingredients = kept });
            if (problems.Count > 0) { Report(problems); return 1; }
            WriteReplacing(path, p => RecipeArchive.Write(p, manifest, kept));
            Console.WriteLine($"Removed ingredient '{ingredientId}' from {path}");
            return 0;
        });
        return cmd;
    }

    private static Command RemoveVariant()
    {
        var file = new Argument<string>("file") { Description = "Path to the .cbk or .igt to modify in place." };
        var recipe = OptionalText("--recipe", "Recipe id (a .cbk only).");
        var ingredient = OptionalText("--ingredient", "Ingredient id (a .cbk only).");
        var id = new Option<string>("--id") { Description = "Variant id to remove.", Required = true };
        var cmd = new Command("variant", "Remove one variant from an .igt, or from a layer of a .cbk.")
            { file, recipe, ingredient, id };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string variantId = parse.GetValue(id)!;
            string? recipeId = parse.GetValue(recipe), ingredientId = parse.GetValue(ingredient);
            if (ExpectKind(path, ArchiveKind.CookBook, ArchiveKind.Ingredient) == ArchiveKind.CookBook)
            {
                RequireGiven(recipeId, "--recipe", "when removing from a .cbk.");
                RequireGiven(ingredientId, "--ingredient", "when removing from a .cbk.");
                return EditBook(path, b => CookBookEdits.RemoveVariant(b, recipeId!, ingredientId!, variantId),
                    _ => $"Removed variant '{variantId}' from {path}");
            }

            RejectGiven(recipeId, "--recipe", "applies to a .cbk; an .igt is the ingredient.");
            RejectGiven(ingredientId, "--ingredient", "applies to a .cbk; an .igt is the ingredient.");
            using var ing = IngredientArchive.Read(path);
            if (ing.Manifest.Variants.All(v => v.Id != variantId))
                throw new KeyNotFoundException($"No variant '{variantId}' in ingredient '{ing.Manifest.Id}'.");
            if (ing.Manifest.Variants.Count <= 1)
                throw new InvalidOperationException(
                    $"'{ing.Manifest.Name}' has one variant left. A layer with none has nothing "
                    + "to roll; delete the layer instead.");
            var manifest = ing.Manifest with { Variants = ing.Manifest.Variants.Where(v => v.Id != variantId).ToList() };
            var images = ing.VariantImages.Where(kv => kv.Key != variantId)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var depths = ing.Depths.Where(kv => kv.Key != variantId)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var problems = Validator.ValidateIngredient(new LoadedIngredient { Manifest = manifest, VariantImages = images });
            if (problems.Count > 0) { Report(problems); return 1; }
            WriteReplacing(path, p => IngredientArchive.Write(p, manifest, images, depths));
            Console.WriteLine($"Removed variant '{variantId}' from {path}");
            return 0;
        });
        return cmd;
    }

    private static Command RemoveRecipe()
    {
        var file = new Argument<string>("cbk") { Description = "Path to the .cbk to modify in place." };
        var id = new Option<string>("--id") { Description = "Recipe id to remove.", Required = true };
        var cmd = new Command("recipe", "Remove a recipe, and its roll weight, from a .cbk.") { file, id };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string recipeId = parse.GetValue(id)!;
            ExpectKind(path, ArchiveKind.CookBook);
            return EditBook(path, b => CookBookEdits.RemoveRecipe(b, recipeId),
                _ => $"Removed recipe '{recipeId}' from {path}");
        });
        return cmd;
    }

    private static Command MoveRecipe()
    {
        var file = new Argument<string>("cbk") { Description = "Path to the .cbk to modify in place." };
        var id = new Option<string>("--id") { Description = "Recipe id to move.", Required = true };
        var to = new Option<int>("--to")
        {
            Description = "Position to move it to, 1-based like `remove rule --at`. Past either end "
                + "it stops at the end. Changes the listing only: the same seed cooks the same Set.",
            Required = true,
        };
        to.Validators.Add(r =>
        {
            if (r.GetValueOrDefault<int>() < 1) r.AddError("--to is 1-based: the first recipe is 1.");
        });
        var cmd = new Command("recipe", "Move a recipe within a .cbk's listing.") { file, id, to };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string recipeId = parse.GetValue(id)!;
            int position = parse.GetValue(to);
            ExpectKind(path, ArchiveKind.CookBook);
            return EditBook(path, b => CookBookEdits.MoveRecipe(b, recipeId, position - 1),
                b => $"Moved recipe '{recipeId}' to position "
                    + $"{Num(b.Manifest.RecipeOrder!.ToList().IndexOf(recipeId) + 1)} of {Num(b.Recipes.Count)} in {path}");
        });
        return cmd;
    }

    private static IngredientManifest Reweigh(IngredientManifest m, string variantId, double w)
    {
        if (m.Variants.All(v => v.Id != variantId))
            throw new KeyNotFoundException($"No variant '{variantId}' in ingredient '{m.Id}'.");
        return m with { Variants = m.Variants.Select(v => v.Id == variantId ? v with { Weight = w } : v).ToList() };
    }

    private static Command SetVariant()
    {
        var file = new Argument<string>("file") { Description = "Path to the .igt or .cbk to modify in place." };
        var recipe = OptionalText("--recipe", "Recipe id (a .cbk only).");
        var ingredient = OptionalText("--ingredient", "Ingredient id (a .cbk only).");
        var id = new Option<string>("--id") { Description = "Variant id.", Required = true };
        var weight = WeightOption("New variant weight, greater than zero.", allowZero: false);
        var cmd = new Command("variant", "Change one variant's weight.") { file, recipe, ingredient, id, weight };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string variantId = parse.GetValue(id)!;
            double w = parse.GetValue(weight);
            string? recipeId = parse.GetValue(recipe), ingredientId = parse.GetValue(ingredient);
            string Msg() => $"Variant '{variantId}' now has weight {w.ToString(CultureInfo.InvariantCulture)} in {path}";

            if (ExpectKind(path, ArchiveKind.CookBook, ArchiveKind.Ingredient) == ArchiveKind.CookBook)
            {
                RequireGiven(recipeId, "--recipe", "when editing a .cbk.");
                RequireGiven(ingredientId, "--ingredient", "when editing a .cbk.");
                return EditBook(path, b =>
                {
                    var rcp = b.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId)
                        ?? throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{b.Manifest.Id}'.");
                    var ing = rcp.Ingredients.FirstOrDefault(i => i.Manifest.Id == ingredientId)
                        ?? throw new KeyNotFoundException($"No ingredient '{ingredientId}' in recipe '{recipeId}'.");
                    var changed = new LoadedIngredient
                    {
                        Manifest = Reweigh(ing.Manifest, variantId, w),
                        VariantImages = ing.VariantImages,
                        Depths = ing.Depths,
                    };
                    var newRecipe = new LoadedRecipe
                    {
                        Manifest = rcp.Manifest,
                        Ingredients = rcp.Ingredients.Select(i => ReferenceEquals(i, ing) ? changed : i).ToList(),
                    };
                    return new LoadedCookBook
                    {
                        Manifest = b.Manifest,
                        Recipes = b.Recipes.Select(r => ReferenceEquals(r, rcp) ? newRecipe : r).ToList(),
                        SourceSha256 = b.SourceSha256,
                    };
                }, _ => Msg());
            }

            RejectGiven(recipeId, "--recipe", "applies to a .cbk; an .igt is the ingredient.");
            RejectGiven(ingredientId, "--ingredient", "applies to a .cbk; an .igt is the ingredient.");
            using var igt = IngredientArchive.Read(path);
            var manifest = Reweigh(igt.Manifest, variantId, w);
            var problems = Validator.ValidateIngredient(new LoadedIngredient { Manifest = manifest, VariantImages = igt.VariantImages });
            if (problems.Count > 0) { Report(problems); return 1; }
            WriteReplacing(path, p => IngredientArchive.Write(p, manifest, igt.VariantImages, igt.Depths));
            Console.WriteLine(Msg());
            return 0;
        });
        return cmd;
    }

    private static Command SetRecipe()
    {
        var file = new Argument<string>("cbk") { Description = "Path to the .cbk to modify in place." };
        var id = new Option<string>("--id") { Description = "Recipe id.", Required = true };
        var weight = WeightOption("New roll weight. Zero shelves the recipe without deleting it.", allowZero: true);
        var cmd = new Command("recipe", "Change a recipe's roll weight in a .cbk.") { file, id, weight };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string recipeId = parse.GetValue(id)!;
            double w = parse.GetValue(weight);
            ExpectKind(path, ArchiveKind.CookBook);
            return EditBook(path, b =>
            {
                var rcp = b.Recipes.FirstOrDefault(r => r.Manifest.Id == recipeId)
                    ?? throw new KeyNotFoundException($"No recipe '{recipeId}' in cookbook '{b.Manifest.Id}'.");
                var edited = CookBookEdits.UpsertRecipe(b, rcp, w);
                // Upsert appends; the listing is the author's, so put the recipe back where it was.
                return new LoadedCookBook
                {
                    Manifest = edited.Manifest,
                    Recipes = b.Recipes.Select(r => edited.Recipes.First(e => e.Manifest.Id == r.Manifest.Id)).ToList(),
                    SourceSha256 = b.SourceSha256,
                };
            }, _ => $"Recipe '{recipeId}' now has weight {w.ToString(CultureInfo.InvariantCulture)}"
                + (w == 0 ? " (shelved)" : string.Empty) + $" in {path}");
        });
        return cmd;
    }

    private static Command SetCookbook()
    {
        var file = new Argument<string>("cbk") { Description = "Path to the .cbk to modify in place." };
        var name = OptionalText("--name", "The book's own name (titlebar, tree, recents).");
        var collection = OptionalText("--collection-name",
            "The collection name every asset is published under (\"<name> #1\").");
        var symbol = OptionalText("--symbol", "The collection symbol.");
        var description = OptionalText("--description", "The collection description.");
        var supply = OptionalText("--target-supply", "Intended supply: a whole number above zero, or `none` to clear.");
        var cmd = new Command("cookbook", "Change a .cbk's name, collection metadata or target supply.")
            { file, name, collection, symbol, description, supply };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            string? n = parse.GetValue(name), cn = parse.GetValue(collection),
                sy = parse.GetValue(symbol), de = parse.GetValue(description), ts = parse.GetValue(supply);
            if (n is null && cn is null && sy is null && de is null && ts is null)
                throw new InvalidOperationException(
                    "Nothing to set: give at least one of --name, --collection-name, --symbol, "
                    + "--description or --target-supply.");
            int? target = null;
            if (ts is not null && !string.Equals(ts, "none", StringComparison.Ordinal))
            {
                if (!int.TryParse(ts, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed < 1)
                    throw new InvalidOperationException(
                        $"--target-supply '{ts}' is not a whole number above zero. Use `none` to clear it.");
                target = parsed;
            }
            ExpectKind(path, ArchiveKind.CookBook);
            return EditBook(path, b => new LoadedCookBook
            {
                Manifest = b.Manifest with
                {
                    Name = n ?? b.Manifest.Name,
                    Collection = b.Manifest.Collection with
                    {
                        Name = cn ?? b.Manifest.Collection.Name,
                        Symbol = sy ?? b.Manifest.Collection.Symbol,
                        Description = de ?? b.Manifest.Collection.Description,
                    },
                    TargetSupply = ts is null ? b.Manifest.TargetSupply : target,
                },
                Recipes = b.Recipes,
                SourceSha256 = b.SourceSha256,
            }, _ => $"Updated {path}");
        });
        return cmd;
    }

    private static Command SetVoxelReady()
    {
        var file = new Argument<string>("cbk") { Description = "Path to the .cbk to modify in place." };
        var dry = new Option<bool>("--dry-run") { Description = "List what would change and write nothing." };
        var cmd = new Command("voxel-ready",
            "Snap every partially transparent pixel to solid or clear (at 50% alpha), so the book can be risen.")
            { file, dry };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(file)!;
            ExpectKind(path, ArchiveKind.CookBook);
            using var book = CookBookArchive.Read(path);
            var plan = VoxelReady.Plan(book);
            if (plan.Count == 0)
            {
                Console.WriteLine($"Nothing to change: {path} has no partially transparent pixels.");
                return 0;
            }
            foreach (var c in plan)
                Console.WriteLine($"  {c.RecipeId} / {c.IngredientName} / {c.VariantName}: "
                    + $"{Num(c.ToSolid)} to solid, {Num(c.ToClear)} to clear");
            if (parse.GetValue(dry))
            {
                Console.WriteLine($"Dry run: {Num(plan.Count)} variant(s) would change; nothing written.");
                return 0;
            }
            using var result = VoxelReady.Apply(book);
            var problems = Validator.Validate(result.Book);
            if (problems.Count > 0) { Report(problems); return 1; }
            WriteReplacing(path, p => CookBookArchive.Write(p, result.Book.Manifest, result.Book.Recipes));
            Console.WriteLine($"Snapped {Num(plan.Count)} variant(s) in {path}");
            return 0;
        });
        return cmd;
    }
}
