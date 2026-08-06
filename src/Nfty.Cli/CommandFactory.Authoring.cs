using System.CommandLine;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>The `new` command group: create an archive from a manifest JSON plus the artifacts
    /// one level down, resolved by the {id} filename convention.</summary>
    public static Command NewGroup()
    {
        var group = new Command("new", "Create a new .igt / .rcp / .cbk / .ktn from a manifest and its parts.");
        group.Subcommands.Add(NewIngredient());
        group.Subcommands.Add(NewRecipe());
        group.Subcommands.Add(NewCookbook());
        group.Subcommands.Add(NewKitchen());
        return group;
    }

    /// <summary>
    /// <c>new kitchen</c>. The odd one out in this group and deliberately so: a Kitchen has no parts
    /// to assemble, because membership is discovered by scanning the folder rather than recorded —
    /// so there is no manifest option and no <c>--force</c>, only a name and a place to put it.
    ///
    /// <para>Added because the Kitchen was a GUI-only concept: one of the six domain terms, with its
    /// own file extension already in <c>Archives.KindOf</c>, and no way to create or look at one
    /// from the command line at all.</para>
    /// </summary>
    private static Command NewKitchen()
    {
        var outPath = new Argument<string>("out")
        { Description = "Output .ktn path to create. Its FOLDER becomes the workspace." };
        var name = new Option<string?>("--name")
        {
            Description = "Display name for the Kitchen. Defaults to the file name without its extension.",
        };

        var cmd = new Command("kitchen",
            "Create a .ktn workspace. The folder the file sits in IS the workspace: anything saved "
                + "beside it is discovered by scanning, never recorded, so moving a file in or out "
                + "needs no bookkeeping.")
        { outPath, name };

        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(outPath)!;
            if (!string.Equals(Path.GetExtension(path), Kitchen.Extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"A Kitchen must be written to a '{Kitchen.Extension}' path, but '{path}' was given.");

            string display = parse.GetValue(name) is { Length: > 0 } n
                ? n
                : Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(display))
                throw new ArgumentException("The Kitchen needs a name; pass --name or use a named file.");

            // Same id rule the GUI's wizard uses, so a Kitchen made either way looks the same.
            string id = string.Join('-', display.ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

            Kitchen.Create(path, new KitchenManifest(id, display));
            Console.WriteLine($"Created Kitchen '{display}' [{id}] at {path}");
            Console.Write(Nfty.Core.Stats.KitchenReport.Render(Kitchen.Open(path)));
            return 0;
        });
        return cmd;
    }

    private static Command NewIngredient()
    {
        var outPath = new Argument<string>("out") { Description = "Output .igt path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to an IngredientManifest JSON (id, name, kind, colorization, variants).",
            Required = true,
        };
        var images = new Option<string>("--images")
        {
            Description = "Directory of variant PNGs; each variant's image is <dir>/{variantId}.png.",
            Required = true,
        };
        var cmd = new Command("ingredient",
            "Build an .igt from an ingredient manifest and one PNG per variant (named {variantId}.png).")
            { outPath, manifest, images };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<IngredientManifest>(parse.GetValue(manifest)!);
            string imagesDir = parse.GetValue(images)!;
            var loaded = LoadVariantImages(m, imagesDir);
            try
            {
                var ing = new LoadedIngredient { Manifest = m, VariantImages = loaded };
                var problems = Validator.ValidateIngredient(ing);
                if (problems.Count > 0) { Report(problems); return 1; }

                IngredientArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} variants)");
                return 0;
            }
            finally
            {
                // These images have no other owner: the LoadedIngredient above only wraps them.
                foreach (var img in loaded.Values) img.Dispose();
            }
        });
        return cmd;
    }

    private static Command NewRecipe()
    {
        var outPath = new Argument<string>("out") { Description = "Output .rcp path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to a RecipeManifest JSON (id, name, layerOrder, rules).",
            Required = true,
        };
        var ingredients = new Option<string>("--ingredients")
        {
            Description = "Directory of .igt files; each layerOrder id resolves to <dir>/{id}.igt.",
            Required = true,
        };
        var cmd = new Command("recipe",
            "Build a .rcp from a recipe manifest and one .igt per layerOrder id (named {id}.igt).")
            { outPath, manifest, ingredients };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<RecipeManifest>(parse.GetValue(manifest)!);
            string dir = parse.GetValue(ingredients)!;
            var loaded = new List<LoadedIngredient>();
            try
            {
                foreach (var id in m.LayerOrder.Distinct(StringComparer.Ordinal))
                {
                    string igt = Path.Combine(dir, $"{id}.igt");
                    if (!File.Exists(igt))
                        throw new FileNotFoundException($"No ingredient for layer '{id}': expected {igt}", igt);
                    loaded.Add(IngredientArchive.Read(igt));
                }

                var recipe = new LoadedRecipe { Manifest = m, Ingredients = loaded };
                var problems = Validator.ValidateRecipe(recipe);
                if (problems.Count > 0) { Report(problems); return 1; }

                RecipeArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} ingredients)");
                return 0;
            }
            finally
            {
                foreach (var ing in loaded) ing.Dispose();
            }
        });
        return cmd;
    }

    private static Command NewCookbook()
    {
        var outPath = new Argument<string>("out") { Description = "Output .cbk path to create." };
        var manifest = new Option<string>("--manifest")
        {
            Description = "Path to a CookBookManifest JSON (id, name, canvas, collection, recipeWeights).",
            Required = true,
        };
        var recipes = new Option<string>("--recipes")
        {
            Description = "Directory of .rcp files; each recipeWeights key resolves to <dir>/{id}.rcp.",
            Required = true,
        };
        var force = new Option<bool>("--force")
        {
            Description = "Write even if validation reports problems (they are printed as warnings). "
                + "Use only for deliberate work-in-progress; generate will still refuse the book.",
        };
        var cmd = new Command("cookbook",
            "Assemble a .cbk from a cookbook manifest and one .rcp per recipeWeights key (named {id}.rcp).")
            { outPath, manifest, recipes, force };
        cmd.SetAction(parse =>
        {
            var m = ManifestFile.Read<CookBookManifest>(parse.GetValue(manifest)!);
            string dir = parse.GetValue(recipes)!;
            var loaded = new List<LoadedRecipe>();
            try
            {
                foreach (var id in m.RecipeWeights.Keys.Distinct(StringComparer.Ordinal))
                {
                    string rcp = Path.Combine(dir, $"{id}.rcp");
                    if (!File.Exists(rcp))
                        throw new FileNotFoundException($"No recipe '{id}': expected {rcp}", rcp);
                    loaded.Add(RecipeArchive.Read(rcp));
                }

                var book = new LoadedCookBook { Manifest = m, Recipes = loaded, SourceSha256 = null };
                var problems = Validator.Validate(book);
                if (problems.Count > 0)
                {
                    Report(problems);
                    if (!parse.GetValue(force)) return 1;
                    Console.Error.WriteLine("--force: writing despite the problems above.");
                }

                CookBookArchive.Write(parse.GetValue(outPath)!, m, loaded);
                Console.WriteLine($"Wrote {parse.GetValue(outPath)} ({loaded.Count} recipes)");
                return 0;
            }
            finally
            {
                foreach (var r in loaded) r.Dispose();
            }
        });
        return cmd;
    }

    /// <summary>The `add` command group: append a single item into an existing archive.</summary>
    public static Command AddGroup()
    {
        var group = new Command("add", "Append a variant / ingredient / recipe to an existing archive.");
        group.Subcommands.Add(AddVariant());
        group.Subcommands.Add(AddIngredient());
        group.Subcommands.Add(AddRecipe());
        return group;
    }

    private static Command AddVariant()
    {
        var igtPath = new Argument<string>("igt") { Description = "Path to the .igt to modify in place." };
        var id = new Option<string>("--id") { Description = "New variant id (must be unique in the ingredient).", Required = true };
        var name = new Option<string?>("--name") { Description = "Display name (defaults to the id)." };
        var weight = new Option<double>("--weight") { Description = "Variant weight (zero or greater).", Required = true };
        var image = new Option<string>("--image") { Description = "PNG for this variant.", Required = true };
        var cmd = new Command("variant", "Add one variant (id, weight, image) to an existing .igt.")
            { igtPath, id, name, weight, image };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(igtPath)!;
            string vid = parse.GetValue(id)!;
            using var existing = IngredientArchive.Read(path);
            if (existing.Manifest.Variants.Any(v => string.Equals(v.Id, vid, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Ingredient '{existing.Manifest.Id}' already has a variant '{vid}'.");

            var newImg = Image.Load<Rgba32>(parse.GetValue(image)!);
            try
            {
                var images = new Dictionary<string, Image<Rgba32>>(existing.VariantImages) { [vid] = newImg };
                var variants = existing.Manifest.Variants
                    .Append(new Variant(vid, parse.GetValue(name) ?? vid, parse.GetValue(weight)))
                    .ToList();
                var manifest = existing.Manifest with { Variants = variants };

                var merged = new LoadedIngredient { Manifest = manifest, VariantImages = images };
                var problems = Validator.ValidateIngredient(merged);
                if (problems.Count > 0) { Report(problems); return 1; }

                // Read(path) closed its file handle before returning, so it's safe to replace the
                // file now. IngredientArchive.Write opens in ZipArchiveMode.Create, which throws
                // if the target already exists, so we write to a sibling temp file and move it
                // into place rather than deleting the original first.
                WriteReplacing(path, p => IngredientArchive.Write(p, manifest, images));
                Console.WriteLine($"Added variant '{vid}' to {path}");
                return 0;
            }
            finally { newImg.Dispose(); }
        });
        return cmd;
    }

    private static Command AddIngredient()
    {
        var rcpPath = new Argument<string>("rcp") { Description = "Path to the .rcp to modify in place." };
        var igt = new Option<string>("--igt") { Description = "Path to the .igt to add as a layer.", Required = true };
        var index = new Option<int?>("--index")
        {
            Description = "0-based position in layerOrder to insert at (default: end).",
        };
        index.Validators.Add(r =>
        {
            var v = r.GetValueOrDefault<int?>();
            if (v is < 0) r.AddError("--index must be zero or greater.");
        });
        var cmd = new Command("ingredient", "Add an .igt as a layer of an existing .rcp.")
            { rcpPath, igt, index };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(rcpPath)!;
            using var recipe = RecipeArchive.Read(path);
            using var newIng = IngredientArchive.Read(parse.GetValue(igt)!);
            string id = newIng.Manifest.Id;

            if (recipe.Manifest.LayerOrder.Contains(id, StringComparer.Ordinal)
                || recipe.Ingredients.Any(i => string.Equals(i.Manifest.Id, id, StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Recipe '{recipe.Manifest.Id}' already has an ingredient '{id}'.");

            var order = recipe.Manifest.LayerOrder.ToList();
            int at = parse.GetValue(index) ?? order.Count;
            if (at > order.Count)
                throw new InvalidOperationException(
                    $"--index {at} is past the end; layerOrder has {order.Count} layer(s).");
            order.Insert(at, id);

            var ingredients = recipe.Ingredients.Append(newIng).ToList();
            var manifest = recipe.Manifest with { LayerOrder = order };
            var merged = new LoadedRecipe { Manifest = manifest, Ingredients = ingredients };
            var problems = Validator.ValidateRecipe(merged);
            if (problems.Count > 0) { Report(problems); return 1; }

            // Read(path) closed its file handle before returning, so it's safe to replace the
            // file now. RecipeArchive.Write opens in ZipArchiveMode.Create, which throws if the
            // target already exists, so we write to a sibling temp file and move it into place
            // rather than deleting the original first.
            WriteReplacing(path, p => RecipeArchive.Write(p, manifest, ingredients));
            Console.WriteLine($"Added ingredient '{id}' to {path} at index {at}");
            return 0;
        });
        return cmd;
    }

    private static Command AddRecipe()
    {
        var cbkPath = new Argument<string>("cbk") { Description = "Path to the .cbk to modify in place." };
        var rcp = new Option<string>("--rcp") { Description = "Path to the .rcp to add.", Required = true };
        var weight = new Option<double>("--weight") { Description = "Recipe roll weight (zero or greater).", Required = true };
        var force = new Option<bool>("--force")
        {
            Description = "Write even if validation reports problems (printed as warnings).",
        };
        var cmd = new Command("recipe", "Add a .rcp to an existing .cbk with a roll weight.")
            { cbkPath, rcp, weight, force };
        cmd.SetAction(parse =>
        {
            string path = parse.GetValue(cbkPath)!;
            using var book = CookBookArchive.Read(path);
            using var newRcp = RecipeArchive.Read(parse.GetValue(rcp)!);
            string id = newRcp.Manifest.Id;

            if (book.Recipes.Any(r => string.Equals(r.Manifest.Id, id, StringComparison.Ordinal))
                || book.Manifest.RecipeWeights.ContainsKey(id))
                throw new InvalidOperationException(
                    $"CookBook '{book.Manifest.Id}' already has a recipe '{id}'.");

            var weights = new Dictionary<string, double>(book.Manifest.RecipeWeights) { [id] = parse.GetValue(weight) };
            var recipes = book.Recipes.Append(newRcp).ToList();
            var manifest = book.Manifest with { RecipeWeights = weights };
            var merged = new LoadedCookBook { Manifest = manifest, Recipes = recipes, SourceSha256 = null };

            var problems = Validator.Validate(merged);
            if (problems.Count > 0)
            {
                Report(problems);
                if (!parse.GetValue(force)) return 1;
                Console.Error.WriteLine("--force: writing despite the problems above.");
            }

            // Read(path) closed its file handle before returning, so it's safe to replace the
            // file now. CookBookArchive.Write opens in ZipArchiveMode.Create, which throws if the
            // target already exists, so we write to a sibling temp file and move it into place
            // rather than deleting the original first.
            WriteReplacing(path, p => CookBookArchive.Write(p, manifest, recipes));
            Console.WriteLine($"Added recipe '{id}' (weight {parse.GetValue(weight)}) to {path}");
            return 0;
        });
        return cmd;
    }

    /// <summary>
    /// Overwrites <paramref name="path"/> atomically: writes via <paramref name="write"/> to a
    /// sibling temp file, then replaces the target with a single move. If the write fails, the
    /// original file is left untouched — the Core archive writers open with ZipArchiveMode.Create
    /// (which refuses an existing file), so a plain in-place overwrite would otherwise have to
    /// delete first and lose the original on any mid-write failure. Reused by every `add` command.
    /// </summary>
    private static void WriteReplacing(string path, Action<string> write)
    {
        string tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        try
        {
            write(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }

    /// <summary>Loads one PNG per distinct variant id, by the {id}.png convention.</summary>
    private static Dictionary<string, Image<Rgba32>> LoadVariantImages(IngredientManifest m, string imagesDir)
    {
        var loaded = new Dictionary<string, Image<Rgba32>>();
        try
        {
            foreach (var v in m.Variants.DistinctBy(v => v.Id, StringComparer.Ordinal))
            {
                string png = Path.Combine(imagesDir, $"{v.Id}.png");
                if (!File.Exists(png))
                    throw new FileNotFoundException(
                        $"No image for variant '{v.Id}': expected {png}", png);
                loaded[v.Id] = Image.Load<Rgba32>(png);
            }
        }
        catch
        {
            foreach (var img in loaded.Values) img.Dispose();
            throw;
        }
        return loaded;
    }

    /// <summary>Prints each validation problem to stderr, as `validate` does.</summary>
    private static void Report(IReadOnlyList<string> problems)
    {
        foreach (var p in problems) Console.Error.WriteLine(p);
    }
}
