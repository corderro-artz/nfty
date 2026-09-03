using System.CommandLine;
using System.Globalization;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Nfty.Cli;

/// <summary>
/// The whole command surface, built in one place so <c>Program</c> stays four lines and the
/// authoring commands can live in their own partial. Handlers here catch nothing: they throw, and
/// <c>Program</c> turns the exception into a message through <see cref="ErrorReport"/>.
/// </summary>
public static partial class CommandFactory
{
    /// <summary>
    /// Recursive, so it works on any subcommand rather than only before one. Read by
    /// <c>Program</c> when a command throws, to decide whether the trace is wanted.
    /// </summary>
    public static Option<bool> VerboseOption { get; } = new("--verbose")
    {
        Description = "On error, print the full stack trace as well as the message.",
        Recursive = true,
    };

    /// <summary>Builds the root command with every subcommand attached.</summary>
    /// <returns>A parser-ready root command.</returns>
    public static RootCommand Build()
    {
        var root = new RootCommand("nfty — layered NFT asset generator");
        root.Options.Add(VerboseOption);
        root.Subcommands.Add(Inspect());
        root.Subcommands.Add(Validate());
        root.Subcommands.Add(Stats());
        root.Subcommands.Add(Preview());
        root.Subcommands.Add(Generate());
        root.Subcommands.Add(Extend());
        root.Subcommands.Add(NewGroup());
        root.Subcommands.Add(AddGroup());
        root.Subcommands.Add(MoveGroup());
        return root;
    }

    private static Command Inspect()
    {
        var path = new Argument<string>("file") { Description = "Path to a .cbk, .rcp, .igt or .ktn file." };
        var voxel = new Option<bool>("--voxel")
        {
            Description = "Also report voxel readiness: which variants carry PARTIAL alpha, which a "
                + "voxel converter cannot resolve (it must drop the pixel or make it solid). Partial "
                + "alpha is legal — this is a report, not a validation — so it is opt-in, and it "
                + "costs a full scan of every variant image. Not available for a Kitchen, which "
                + "lists paths without opening them.",
        };
        var cmd = new Command("inspect",
            "Print the tree of a CookBook, Recipe or Ingredient, showing each Recipe's and "
                + "Variant's [id] alongside its name. Those ids — not the display names — are "
                + "what --recipe and --variant expect elsewhere on this command line, so inspect "
                + "is how you find them. Given a Kitchen, lists what that workspace holds.")
        { path, voxel };
        cmd.SetAction(parse =>
        {
            string file = parse.GetValue(path)!;
            bool wantVoxel = parse.GetValue(voxel);
            var kind = Archives.KindOf(file);
            switch (kind)
            {
                case ArchiveKind.CookBook:
                {
                    using var cb = CookBookArchive.Read(file);
                    PrintCookBook(cb);
                    if (wantVoxel) PrintVoxel(cb.Manifest.Name, VoxelReport.Scan(cb));
                    break;
                }
                case ArchiveKind.Recipe:
                {
                    using var recipe = RecipeArchive.Read(file);
                    PrintRecipe(recipe, weight: null, indent: "");
                    if (wantVoxel) PrintVoxel(recipe.Manifest.Name, VoxelReport.Scan(recipe));
                    break;
                }
                case ArchiveKind.Ingredient:
                {
                    using var ing = IngredientArchive.Read(file);
                    PrintIngredient(ing, indent: "");
                    if (wantVoxel) PrintVoxel(ing.Manifest.Name, VoxelReport.Scan(ing));
                    break;
                }
                case ArchiveKind.Kitchen:
                {
                    // Nothing to dispose: a Kitchen lists PATHS rather than loading the archives it
                    // names, precisely so inspecting a workspace does not decode every PNG in it.
                    // --voxel would have to open every one, so it is refused rather than quietly
                    // ignored — an option that silently does nothing is worse than one that says no.
                    if (wantVoxel)
                        throw new InvalidOperationException(
                            "--voxel needs artwork to scan, and a Kitchen lists paths without opening "
                            + "them. Run it on one of the CookBooks the listing names.");
                    Console.Write(KitchenReport.Render(Kitchen.Open(file)));
                    break;
                }
                default:
                    // Archives.KindOf already rejects an unknown extension before we get here,
                    // so this only guards against a future ArchiveKind case added without a
                    // matching inspect branch.
                    throw new NotSupportedException($"inspect does not know how to print archive kind '{kind}'.");
            }
            return 0;
        });
        return cmd;
    }

    private static void PrintCookBook(LoadedCookBook cb)
    {
        Console.WriteLine($"CookBook: {cb.Manifest.Name} ({cb.Manifest.Canvas.Width}x{cb.Manifest.Canvas.Height})");

        // A book's palette travels inside the archive, so a collection handed to someone else brings
        // its colours with it — and nothing else in the CLI would have shown they were there.
        // Printed as the specs it is stored as: the same form an author types.
        if (cb.Manifest.Palette is { Count: > 0 } specs)
        {
            Console.WriteLine($"  Palette: {specs.Count} swatch{(specs.Count == 1 ? "" : "es")}");
            foreach (var spec in specs) Console.WriteLine($"    {spec}");
        }

        foreach (var r in cb.Recipes)
            PrintRecipe(r, cb.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id), "  ");
    }

    /// <summary>Prints the voxel-readiness report under whatever tree was just printed. Rendered in
    /// Core so the CLI and the GUI show byte-identical text.</summary>
    private static void PrintVoxel(string title, IReadOnlyList<VoxelVariant> rows)
    {
        Console.WriteLine();
        Console.Write(VoxelReport.Render(title, rows));
    }

    private static void PrintRecipe(LoadedRecipe recipe, double? weight, string indent)
    {
        string suffix = weight is double w ? $" (weight={w})" : string.Empty;
        Console.WriteLine($"{indent}Recipe: {recipe.Manifest.Name} [{recipe.Manifest.Id}]{suffix}");

        // inspect is a diagnostic run on any file, including a malformed one — the very case a
        // user reaches for it. So resolve tolerantly (last id wins, like Validator) and name a
        // dangling layerOrder entry rather than throwing a raw KeyNotFoundException at someone
        // trying to see what is wrong.
        var byId = new Dictionary<string, LoadedIngredient>();
        foreach (var i in recipe.Ingredients) byId[i.Manifest.Id] = i;
        foreach (var layerId in recipe.Manifest.LayerOrder)
        {
            if (byId.TryGetValue(layerId, out var ing)) PrintIngredient(ing, indent + "  ");
            else Console.WriteLine($"{indent}  Ingredient: <missing '{layerId}'>");
        }
    }

    private static void PrintIngredient(LoadedIngredient ing, string indent)
    {
        Console.WriteLine($"{indent}Ingredient: {ing.Manifest.Name} [{ing.Manifest.Kind}]");
        foreach (var v in ing.Manifest.Variants)
            Console.WriteLine($"{indent}  Variant: {v.Name} [{v.Id}] (w={v.Weight})");
    }

    private static Command Validate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file." };
        var cmd = new Command("validate",
            "Check a CookBook for problems and report every one found (id collisions, rule "
                + "conflicts, canvas mismatches, etc.) instead of stopping at the first. "
                + "generate runs this same check itself and refuses to run over a broken "
                + "CookBook, so this command exists for humans who want to see what's wrong "
                + "before generating.")
        { path };
        cmd.SetAction(parse =>
        {
            using var cb = CookBookArchive.Read(parse.GetValue(path)!);
            var problems = Validator.Validate(cb);
            if (problems.Count == 0) { Console.WriteLine("OK — no problems."); return 0; }
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        });
        return cmd;
    }

    private static Command Stats()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file." };
        var cmd = new Command("stats",
            "Show the odds this CookBook's weights imply: percent share per Recipe, and percent "
                + "share per Variant across the whole collection — computed from the CookBook's "
                + "configured weights, not from an actual generated Set.")
        { path };
        cmd.SetAction(parse =>
        {
            using var cb = CookBookArchive.Read(parse.GetValue(path)!);
            // Rendered in Core so the GUI's copyable report is byte-identical to this - the same
            // report, not a similar one. Includes the unique-DNA space, the figure generate reports
            // only on failure, surfaced so a run can be sized before it starts.
            Console.Write(CollectionReport.Render(cb));
            return 0;
        });
        return cmd;
    }

    private static Command Preview()
    {
        var path = new Argument<string>("file")
        {
            Description = "Path to a .igt (one layer's variants) or a .rcp (a whole layer stack). "
                + "Which one you pass chooses the form: an .igt renders exactly one named variant, "
                + "a .rcp rolls the whole stack from --seed.",
        };
        var variant = new Option<string>("--variant")
        {
            Description = "Variant id to render — an id, not its display name. Run inspect on "
                + "this .igt (or its parent Recipe/CookBook) to list the ids and names side by "
                + "side. Required for an .igt; the .rcp form rolls each layer's variant instead.",
        };
        var color = new Option<string?>("--color")
        {
            Description = "Color spec to colorize with. Required for Dynamic and Static "
                + "ingredients; omit it for Custom ingredients, which are full-color already and "
                + "are always rendered as-is, never colorized. Must carry an explicit hex:, rgb:, "
                + "hsl:, or hsv: prefix, e.g. hex:d6249f or hsv:200,70,80 — a missing or "
                + "unrecognized prefix is a validation error, never guessed.",
        };
        var model = new Option<string?>("--model")
        {
            Description = "Override the ingredient's own stored colorization model. Valid "
                + "values: hsv, hsl (case-insensitive). Defaults to whatever the ingredient "
                + "declares, since that is what generation actually uses; pass this only to "
                + "preview the variant as if it had been authored with the other model.",
        };
        model.Validators.Add(result =>
        {
            string? v = result.GetValueOrDefault<string?>();
            if (v is not null
                && !v.Equals("hsv", StringComparison.OrdinalIgnoreCase)
                && !v.Equals("hsl", StringComparison.OrdinalIgnoreCase))
                result.AddError($"--model must be 'hsv' or 'hsl', not '{v}'.");
        });
        var outp = new Option<string>("--out")
        {
            Description = "Output PNG path.",
            DefaultValueFactory = _ => "preview.png",
        };
        var seed = new Option<string>("--seed")
        {
            Description = "RNG seed for the .rcp form: it drives one roll of the whole stack — each "
                + "layer's variant, and each Dynamic layer's colour — so the same recipe and seed "
                + "always render the same PNG. Defaults to a fixed value, so the command works "
                + "without it.",
            DefaultValueFactory = _ => "nfty",
        };
        var only = new Option<string?>("--only")
        {
            Description = "Comma-separated ingredient ids to draw, for the .rcp form. The layers "
                + "still sit at their REAL depths — --only hides the others, it does not renumber "
                + "or compact the stack — and the whole recipe is still rolled, so a layer looks "
                + "the same whether or not its neighbours are drawn. An id the recipe does not "
                + "stack is an error.",
        };
        var with = new Option<string?>("--with")
        {
            Description = "Path to a loose .igt to composite ON TOP of the .rcp's stack — a layer "
                + "being authored against the recipe it will join. Its variant is PICKED rather "
                + "than rolled (see --with-variant) so it holds still while you vary --seed; a "
                + "colorized one still takes its colour from --seed. It must match the recipe's "
                + "canvas, and is never scaled to fit.",
        };
        var withVariant = new Option<string?>("--with-variant")
        {
            Description = "Which variant of --with to draw. Defaults to the deterministic pick "
                + "(highest weight, ties broken by ordinal-first id) so a reference layer never "
                + "changes appearance between runs.",
        };
        var cmd = new Command("preview",
            "Render a PNG exactly as generation would. Given an .igt, renders one named Variant: "
                + "colorized from its value-map for Dynamic/Static ingredients, or passed through "
                + "untouched for Custom ones. Given a .rcp, rolls that Recipe's whole layer stack "
                + "from --seed and composites it in depth order — depth 1 paints first and sits "
                + "furthest back.")
        { path, variant, color, model, outp, seed, only, with, withVariant };

        // Which options are legal depends on which FORM the file argument selects, and silently
        // ignoring a flag that does not apply is how a user comes to believe --color did something.
        // So the split is enforced at parse time, where the error names the form rather than
        // surfacing halfway through a render.
        cmd.Validators.Add(result =>
        {
            string? file = result.GetValue(path);
            if (string.IsNullOrEmpty(file)) return;   // the missing-argument error already speaks

            bool Given(Option option) => result.GetResult(option) is { Implicit: false };

            // Neither form, checked first. Without this the else-branch below treats "not a .rcp" as
            // "an .igt", so `preview book.cbk` was told --variant is required — advice that, followed,
            // produced a different error from a different layer. Say what preview actually reads, and
            // say it once.
            if (PreviewForm(file) is null)
            {
                result.AddError($"preview reads an Ingredient ({Archives.IngredientExtension}) or a "
                    + $"Recipe ({Archives.RecipeExtension}); '{file}' is neither. An .igt renders one "
                    + "variant, a .rcp rolls the whole layer stack.");
                return;
            }

            if (IsRecipePath(file))
            {
                if (Given(variant))
                    result.AddError("--variant names one Variant of an .igt; the .rcp form rolls "
                        + "every layer's variant from --seed. Use --with-variant for the --with layer.");
                if (Given(color))
                    result.AddError("--color applies to the .igt form; in the .rcp form each "
                        + "colorized layer's colour is rolled from --seed, as generation rolls it.");
                if (Given(model))
                    result.AddError("--model applies to the .igt form; in the .rcp form every layer "
                        + "renders in the colour model its own ingredient declares.");
                if (Given(withVariant) && !Given(with))
                    result.AddError("--with-variant names a variant of --with, but no --with was given.");
            }
            else
            {
                if (!Given(variant))
                    result.AddError($"--variant is required to preview '{file}': an Ingredient holds "
                        + "several variants and this form renders exactly one. Pass a .rcp instead to "
                        + "roll a whole stack.");
                foreach (var (option, name) in new (Option Option, string Name)[]
                         { (seed, "--seed"), (only, "--only"), (with, "--with"), (withVariant, "--with-variant") })
                    if (Given(option))
                        result.AddError($"{name} applies to the .rcp form, which rolls a whole layer "
                            + "stack; this form renders the one variant named by --variant.");
            }
        });

        cmd.SetAction(parse =>
        {
            string file = parse.GetValue(path)!;
            string outPath = parse.GetValue(outp)!;

            // Archives.KindOf owns the extension→kind decision, and rejects an unknown extension
            // rather than guessing, so preview never has to.
            return Archives.KindOf(file) switch
            {
                ArchiveKind.Ingredient => PreviewIngredient(
                    file, outPath, parse.GetValue(variant)!, parse.GetValue(color), parse.GetValue(model)),
                ArchiveKind.Recipe => PreviewRecipe(
                    file, outPath, parse.GetValue(seed)!, parse.GetValue(only),
                    parse.GetValue(with), parse.GetValue(withVariant)),
                // A backstop, not a reachable path: the validator above has already refused anything
                // that is neither form, with a better-worded message. Kept because a bare switch
                // expression would otherwise raise SwitchExpressionException if that ever stopped
                // being true, and this says what went wrong instead.
                var kind => throw new NotSupportedException(
                    $"preview reads an Ingredient ({Archives.IngredientExtension}) or a Recipe "
                    + $"({Archives.RecipeExtension}), not a {kind}."),
            };
        });
        return cmd;
    }

    /// <summary>
    /// Which of <c>preview</c>'s two forms a path selects, or null for neither.
    ///
    /// <para>Goes through <see cref="Archives.TryKindOf"/> rather than comparing extensions here:
    /// this runs during validation, where an unknown extension must become a parse error about the
    /// FORM rather than a thrown <c>NotSupportedException</c> from inside the parser — but the
    /// extension→kind table still has exactly one owner, which is what <c>Archives</c> is for. The
    /// action calls <c>KindOf</c> and gets its real message.</para>
    ///
    /// <para>Both forms are asked about explicitly rather than one being inferred from the other's
    /// absence — "not a .rcp" is not "an .igt", and treating it that way sent a <c>.cbk</c> the
    /// ingredient form's advice.</para>
    /// </summary>
    private static ArchiveKind? PreviewForm(string path) =>
        Archives.TryKindOf(path, out var kind) && kind is ArchiveKind.Recipe or ArchiveKind.Ingredient
            ? kind
            : null;

    /// <summary>Whether a path selects <c>preview</c>'s Recipe form.</summary>
    private static bool IsRecipePath(string path) => PreviewForm(path) == ArchiveKind.Recipe;

    /// <summary>The original form: one named Variant of one Ingredient.</summary>
    private static int PreviewIngredient(
        string file, string outPath, string variantId, string? colorSpec, string? modelName)
    {
        using var ing = IngredientArchive.Read(file);

        ColorModel? modelOverride = modelName is null
            ? null
            : modelName.Equals("hsl", StringComparison.OrdinalIgnoreCase) ? ColorModel.Hsl : ColorModel.Hsv;

        // The rule itself lives in Core so the GUI's export renders the identical image — the
        // whole point of this command is that it shows what generation would produce, and two
        // copies of that rule is how it stops being true. --color's requiredness, the Custom
        // passthrough and the spec→(H,S) resolution are all VariantPreview's.
        using var img = VariantPreview.Render(ing, variantId, colorSpec, modelOverride);
        img.Save(outPath, new PngEncoder());

        Console.WriteLine(ing.Manifest.Kind == LayerKind.Custom
            ? $"Wrote {outPath} (custom layer — rendered as-is, not colorized)"
            : $"Wrote {outPath}");
        return 0;
    }

    /// <summary>
    /// The stack form: one deterministic roll of a whole Recipe, composited in depth order.
    /// </summary>
    private static int PreviewRecipe(
        string file, string outPath, string seed, string? only, string? with, string? withVariant)
    {
        using var recipe = RecipeArchive.Read(file);

        // Gated on the same check `new recipe` and `add ingredient` use, and for a reason beyond
        // tidiness: it is what makes the canvas below well-defined. Rendering a recipe whose images
        // disagree in size would have to pick one of them as "the" canvas and reject the rest.
        var problems = Validator.ValidateRecipe(recipe);
        if (problems.Count > 0) { Report(problems); return 1; }

        var canvas = CanvasOf(recipe);

        // Resolved before anything is rolled: a mistyped --only id is a mistake in the command line,
        // and there is no reason to make the user wait for a roll and a composite to hear about it.
        var keep = OnlyFilter(recipe, only);

        // One RNG for the whole command: the recipe's layers first, in layerOrder, then the --with
        // layer that sits on top of them. Re-seeding for the loose layer would hand it a colour
        // correlated with the bottom layer's, which is exactly the sort of thing a preview is used
        // to judge.
        //
        // The roll walks EVERY layer, --only or not. --only decides what is drawn, never what was
        // rolled, so a layer looks identical whether or not its neighbours are on screen — which is
        // the only reading under which "at their real depths" means anything.
        var rng = StackRoll.RngFor(seed);
        var rolled = StackRoll.ForRecipe(recipe, rng);

        var drawn = new List<(string Depth, PreviewLayer Layer, string? Source)>();
        for (int i = 0; i < rolled.Count; i++)
        {
            string id = recipe.Manifest.LayerOrder[i];
            if (keep is not null && !keep.Contains(id)) continue;

            // The layer's own depth, not its position among the drawn ones: --only hides layers, it
            // does not renumber them, and a printed "1, 3, 4" is what says so.
            drawn.Add((Num(i + 1), rolled[i], null));
        }

        LoadedIngredient? loose = null;
        try
        {
            if (with is not null)
            {
                // Asked before opening, so a wrong extension gets the domain's own message
                // ("expected one of .cbk, .rcp, .igt, .ktn") instead of a JSON deserializer
                // complaining about missing required properties from inside the archive reader.
                if (Archives.KindOf(with) is var withKind and not ArchiveKind.Ingredient)
                    throw new InvalidOperationException(
                        $"--with adds one loose Ingredient ({Archives.IngredientExtension}) on top of "
                        + $"the stack; '{with}' is a {withKind}.");

                loose = IngredientArchive.Read(with);

                // Picked, not rolled. A --with layer is a REFERENCE — "how does this sit against
                // that stack?" — so it must hold still while the author varies --seed to look at
                // different rolls of the recipe. StackPreview.PickVariant is that deterministic
                // choice (highest weight, ordinal-first on a tie). Its colour still comes from the
                // seed, because a colorized layer needs one and there is no other source.
                string variantId = withVariant ?? StackPreview.PickVariant(loose);
                drawn.Add(("+", StackRoll.ForIngredient(loose, rng, variantId), with));
            }

            using var img = StackPreview.Render(canvas, drawn.Select(d => d.Layer).ToList());
            img.Save(outPath, new PngEncoder());

            ReportStack(outPath, recipe, canvas, seed, rolled.Count, drawn);
            return 0;
        }
        finally { loose?.Dispose(); }
    }

    /// <summary>
    /// The ids <c>--only</c> admits, or null when every layer draws.
    /// </summary>
    /// <exception cref="InvalidOperationException">An id the recipe does not stack — named, with the
    /// real stack listed beside it, because the usual cause is a display name typed where an id
    /// belongs.</exception>
    private static HashSet<string>? OnlyFilter(LoadedRecipe recipe, string? only)
    {
        if (only is null) return null;

        var wanted = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // RemoveEmptyEntries turns "", "," and " , " into NO ids, and an empty allow-list filters
        // every layer out — so this used to write a fully transparent PNG and report success, while a
        // single typo'd id was a hard error. Silent nothing is the worse of the two: the file looks
        // like the render failed for some reason the tool did not mention.
        if (wanted.Length == 0)
            throw new InvalidOperationException(
                "--only was given no layer ids. Pass a comma-separated list of ids to draw, or omit "
                + "--only entirely to draw the whole stack.");

        var stacked = new HashSet<string>(recipe.Manifest.LayerOrder, StringComparer.Ordinal);
        foreach (var id in wanted)
            if (!stacked.Contains(id))
                throw new InvalidOperationException(
                    $"Recipe '{recipe.Manifest.Id}' does not stack an ingredient '{id}'. Its layers, "
                    + "bottom to top: " + string.Join(", ", LayerDepth.Ordered(recipe.Manifest)
                        .Select(l => $"{l.IngredientId} (depth {Num(l.Depth)})"))
                    + ".");

        return new HashSet<string>(wanted, StringComparer.Ordinal);
    }

    /// <summary>
    /// The canvas a Recipe previews at.
    ///
    /// <para>A <c>.rcp</c> has no canvas of its own — canvas is a CookBook property, the single source
    /// of truth for a whole book, and the same recipe can legitimately be nested in books of different
    /// sizes. So the size is <b>derived from the recipe's own art</b>: <c>Validator.ValidateRecipe</c>
    /// has just proved that every variant image across every ingredient shares one size, which makes
    /// "the first one" well-defined rather than arbitrary — and that shared size is the only canvas
    /// this recipe could ever be cooked at.</para>
    ///
    /// <para>A <c>--canvas WxH</c> option was rejected: it would let a preview render at a size no
    /// CookBook could use, and <see cref="StackPreview"/> never scales a layer precisely so that a
    /// preview cannot show art lining up at a size it will not ship at.</para>
    /// </summary>
    private static Dimensions CanvasOf(LoadedRecipe recipe)
    {
        foreach (var ing in recipe.Ingredients)
            foreach (var img in ing.VariantImages.Values)
                return new Dimensions(img.Width, img.Height);

        throw new InvalidOperationException(
            $"Recipe '{recipe.Manifest.Id}' has no variant images, so there is no canvas to "
            + "preview it at.");
    }

    /// <summary>What was drawn, at what depth, in what colour — printed under the output path.</summary>
    private static void ReportStack(
        string outPath, LoadedRecipe recipe, Dimensions canvas, string seed, int total,
        IReadOnlyList<(string Depth, PreviewLayer Layer, string? Source)> drawn)
    {
        int stacked = drawn.Count(d => d.Source is null);
        string scope = stacked == total
            ? $"{Num(total)} layers"
            : $"{Num(stacked)} of {Num(total)} layers";
        if (drawn.Count > stacked) scope += $" + {Num(drawn.Count - stacked)} loose";

        Console.WriteLine($"Wrote {outPath} — recipe '{recipe.Manifest.Name}' [{recipe.Manifest.Id}] "
            + $"at {Num(canvas.Width)}x{Num(canvas.Height)}, seed '{seed}', {scope}");

        int idWidth = drawn.Count == 0 ? 0 : drawn.Max(d => d.Layer.Ingredient.Manifest.Id.Length);
        int variantWidth = drawn.Count == 0 ? 0 : drawn.Max(d => d.Layer.VariantId.Length);
        foreach (var (depth, layer, source) in drawn)
        {
            string id = layer.Ingredient.Manifest.Id.PadRight(idWidth);
            string variantId = layer.VariantId.PadRight(variantWidth);
            string colour = layer.ColorSpec ?? "(custom — as-is)";
            string from = source is null ? string.Empty : $"   ← {source}";
            Console.WriteLine($"  {depth,3}  {id}  {variantId}  {colour}{from}");
        }
    }

    private static Command Generate()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to a .cbk file." };
        var count = new Option<int>("--count") { Description = "Number of assets to generate.", Required = true };
        var seed = new Option<string>("--seed")
        {
            Description = "RNG seed driving generation. The same CookBook plus the same seed "
                + "always produces byte-identical output, including image bytes — pass a "
                + "different seed to get a different roll of the same CookBook.",
            DefaultValueFactory = _ => "nfty",
        };
        var outDir = new Option<string>("--out")
        {
            Description = "Directory to write the generated Set (images + metadata) into.",
            Required = true,
        };
        var pack = new Option<bool>("--pack") { Description = "Also package the output directory into a single .set archive." };
        var recipe = new Option<string?>("--recipe")
        {
            Description = "Restrict generation to one Recipe id (an id, not its display name — "
                + "run inspect on the CookBook to find it), instead of rolling a Recipe per "
                + "asset by the CookBook's own weights.",
        };
        var unlimited = new Option<bool>("--unlimited")
        {
            Description = "Accept every roll instead of requiring each asset to have unique DNA. "
                + "Identity is carried by the sequential token id, per the ERC-721 standard, so "
                + "assets may repeat and any --count is producible regardless of the unique space. "
                + "Skips the dedup and space-counting work — use it for large runs on slow "
                + "machines. Incompatibility rules are still enforced.",
        };
        var maxRerolls = MaxRerollsOption();
        var cmd = new Command("generate", "Generate a new Set of assets from a CookBook.") { path, count, seed, outDir, pack, recipe, unlimited, maxRerolls };
        cmd.SetAction(parse =>
        {
            using var book = CookBookArchive.Read(parse.GetValue(path)!);
            string dir = parse.GetValue(outDir)!;
            var opts = new GenerateOptions(
                Count: parse.GetValue(count),
                Seed: parse.GetValue(seed)!,
                RecipeId: parse.GetValue(recipe),
                MaxRerollsPerAsset: parse.GetValue(maxRerolls),
                EnforceUniqueDna: !parse.GetValue(unlimited));
            using var set = Generator.Generate(book, opts);
            SetWriter.Write(set, dir, parse.GetValue(pack));
            Console.WriteLine($"Generated {set.Assets.Count} → {dir}");
            return 0;
        });
        return cmd;
    }

    private static Command Extend()
    {
        var path = new Argument<string>("cookbook") { Description = "Path to the .cbk file the existing Set was generated from." };
        var dir = new Argument<string>("set-dir") { Description = "Directory of an existing Set, previously written by generate." };
        var to = new Option<int>("--to")
        {
            Description = "Target total asset count for the Set. extend rolls only the new, "
                + "non-colliding assets needed to reach it, then recomputes rarity across the "
                + "whole collection — including rewriting existing assets' rarity field, since "
                + "rarity is collection-wide.",
            Required = true,
        };
        var seed = new Option<string>("--seed")
        {
            Description = "RNG seed for the newly rolled assets. Existing assets and their DNA "
                + "are left untouched, aside from their recomputed rarity.",
            DefaultValueFactory = _ => "nfty-extend",
        };
        var unlimited = new Option<bool>("--unlimited")
        {
            Description = "Accept every new roll instead of requiring unique DNA, including against "
                + "the existing Set's DNA. Identity is carried by the sequential token id, per the "
                + "ERC-721 standard. Skips the dedup and space-counting work — use it for large "
                + "extensions on slow machines. Incompatibility rules are still enforced.",
        };
        var pack = new Option<bool>("--pack")
        {
            Description = "Also repackage the extended Set directory into a single .set archive, "
                + "as generate --pack does.",
        };
        var maxRerolls = MaxRerollsOption();
        var cmd = new Command("extend", "Grow an existing Set to a new total asset count, using the same CookBook.") { path, dir, to, seed, unlimited, pack, maxRerolls };
        cmd.SetAction(parse =>
        {
            using var book = CookBookArchive.Read(parse.GetValue(path)!);
            string setDir = parse.GetValue(dir)!;
            int target = parse.GetValue(to);

            var existing = SetWriter.ReadExisting(setDir);

            int have = existing.NextNumber - 1;
            int need = target - have;

            // Before the early return, but after the arithmetic: a run that adds nothing mixes
            // nothing, so warning about the book would be noise on a no-op.
            if (need <= 0) { Console.WriteLine($"Already at {have}."); return 0; }

            // A warning, to stderr, and never a refusal: re-cooking a deliberately edited book is a
            // legitimate thing to want, and the author is the one who knows. Core owns the wording so
            // a GUI says the identical thing, and owns the "a null on either side means cannot tell"
            // rule so neither front-end has to get it right twice.
            if (SetProvenance.Warning(existing.CookbookSha256, book.SourceSha256) is { } warning)
                Console.Error.WriteLine(warning);

            using var more = Generator.Generate(book,
                new GenerateOptions(need, parse.GetValue(seed)!,
                    MaxRerollsPerAsset: parse.GetValue(maxRerolls),
                    EnforceUniqueDna: !parse.GetValue(unlimited)),
                existing.Dnas, existing.NextNumber);
            SetWriter.Write(more, setDir, parse.GetValue(pack));
            Console.WriteLine($"Extended by {need} → {target} total.");
            return 0;
        });
        return cmd;
    }

    /// <summary>
    /// A fresh <c>--max-rerolls</c> option. Built per command rather than shared, since an Option
    /// instance belongs to one command's symbol tree. The default tracks
    /// <see cref="GenerateOptions.DefaultMaxRerolls"/> so the CLI and library never disagree on it.
    /// </summary>
    private static Option<int> MaxRerollsOption()
    {
        var opt = new Option<int>("--max-rerolls")
        {
            Description = "Per-asset reroll budget before generation gives up finding a legal, "
                + "unique roll. Raise it when a run reports the reroll budget ran out before the "
                + "unique space did; the default suits most CookBooks.",
            DefaultValueFactory = _ => GenerateOptions.DefaultMaxRerolls,
        };
        opt.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() < 1)
                result.AddError("--max-rerolls must be a positive integer.");
        });
        return opt;
    }
}
