using System.CommandLine;
using Nfty.Core;
using Nfty.Core.Output;
using System.Globalization;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Publish;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>Which of <c>model</c>'s two forms an input selects.</summary>
    private enum ModelInput
    {
        /// <summary>A loose <c>.png</c>.</summary>
        Picture,

        /// <summary>An <c>.igt</c>, with <c>--variant</c>.</summary>
        Ingredient,

        /// <summary>An <c>.rcp</c>, with <c>--seed</c>.</summary>
        Recipe,
    }

    private static Command ModelCommand()
    {
        var input = new Argument<string>("input")
        {
            Description = "A .png picture, an .igt Ingredient together with --variant, or an .rcp "
                + "Recipe together with --seed. Which one you pass chooses the form.",
        };
        var outp = new Option<string>("--out")
        {
            Description = "The model file to write. Its extension must match --format: .glb for glb, "
                + ".gltf for gltf and gltf-embedded. The gltf format also writes a .bin beside it, "
                + "with the same name.",
            Required = true,
        };
        var variant = new Option<string>("--variant")
        {
            Description = "Variant id to rise — an id, not its display name. Required for an .igt, "
                + "refused for a .png. The variant's stored depth map is used; a variant with none "
                + "is paper. The variant's own pixels are risen as they are stored: a Dynamic or "
                + "Static layer's variant is its grayscale value-map.",
        };
        var seed = new Option<string>("--seed")
        {
            Description = "Roll the recipe with this seed and rise the whole stack - the same draw "
                + "`preview x.rcp --seed S` shows. Required for an .rcp, refused otherwise.",
        };
        var front = new Option<int>("--front")
        {
            Description = "Fill-all: every fully opaque pixel's front depth, 0..255 voxels toward the "
                + "viewer. Given with --back, never alone. Replaces any stored depth.",
        };
        var back = new Option<int>("--back")
        {
            Description = "Fill-all: every fully opaque pixel's back depth, 0..255 voxels away from "
                + "the viewer. Given with --front, never alone.",
        };
        var format = ModelFormatOption("--format");
        var pivot = ModelPivotOption();
        var voxelSize = ModelVoxelSizeOption();

        var cmd = new Command("model",
            "Rise a picture into a 3D model: every fully opaque pixel becomes a column of voxels — one "
                + "for the paper plus its front and back depth — colored with the picture's own pixels. "
                + "A partly transparent pixel is refused by name, never rounded.")
        { input, outp, variant, seed, front, back, format, pivot, voxelSize };

        // Which options are legal depends on the FORM the input selects, and an option that silently
        // does nothing is worse than one that says no — the rule `preview` keeps. Enforced at parse
        // time, where the error names the form.
        cmd.Validators.Add(result =>
        {
            string? file = result.GetValue(input);
            if (string.IsNullOrEmpty(file)) return;   // the missing-argument error already speaks

            switch (ModelInputOf(file))
            {
                case null:
                    result.AddError($"model reads a picture (.png), an Ingredient "
                        + $"({Archives.IngredientExtension}) with --variant, or a Recipe "
                        + $"({Archives.RecipeExtension}) with --seed; '{file}' is none of them.");
                    return;
                case ModelInput.Picture when result.IsGiven(variant):
                    result.AddError("--variant names one variant of an .igt; a .png is one picture already.");
                    break;
                case ModelInput.Ingredient when !result.IsGiven(variant):
                    result.AddError($"--variant is required to model '{file}': an Ingredient holds "
                        + "several variants and a model is made of one.");
                    break;
                case ModelInput.Recipe when result.IsGiven(variant):
                    result.AddError("--variant names one variant of an .igt; a Recipe is rolled with --seed.");
                    break;
                case ModelInput.Recipe when result.IsGiven(front) || result.IsGiven(back):
                    result.AddError("--front and --back fill a picture or one variant; a Recipe takes the "
                        + "depth its layers store.");
                    break;
                case ModelInput.Recipe when !result.IsGiven(seed):
                    result.AddError($"--seed is required to model '{file}': a Recipe is rolled, and the "
                        + "seed says which roll.");
                    break;
            }
            if (result.IsGiven(seed) && ModelInputOf(file) is not ModelInput.Recipe)
                result.AddError("--seed rolls a Recipe (.rcp); a picture or a variant is not rolled.");

            if (result.IsGiven(front) != result.IsGiven(back))
                result.AddError("--front and --back fill every opaque pixel together; pass both "
                    + "(0 leaves a side flat).");
            foreach (var (option, name) in new (Option<int> Option, string Name)[] { (front, "--front"), (back, "--back") })
            {
                int value = result.GetValue(option);
                if (result.IsGiven(option) && value is < 0 or > 255)
                    result.AddError($"{name} must be 0..255 (one byte per side); got {value}.");
            }

            CheckVoxelSize(result, voxelSize);

            string? outPath = result.GetValue(outp);
            if (outPath is not null && string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(outPath)))
            {
                result.AddError($"--out '{outPath}' has no file name before the extension; "
                    + $"give it one, e.g. 'model{Path.GetExtension(outPath)}'.");
                return;
            }

            // Read as a raw TOKEN, never through GetValue: format.AcceptOnlyFromAmong has already
            // flagged an unrecognized value (like "obj") as its own parse error, and calling
            // GetValue on an option whose value failed that check throws instead of returning it —
            // this is the one place that value is needed for something AcceptOnlyFromAmong does not
            // already say, so it must not re-trip the same failure into an unhandled exception.
            string fmt = result.IsGiven(format) ? result.GetResult(format)!.Tokens[0].Value : "glb";
            if (outPath is not null && ModelTokens.FormatNames.Contains(fmt, StringComparer.Ordinal))
            {
                string expected = fmt == "glb" ? ".glb" : ".gltf";
                if (!string.Equals(Path.GetExtension(outPath), expected, StringComparison.OrdinalIgnoreCase))
                    result.AddError($"--out '{outPath}' does not match --format {fmt}: that format "
                        + $"writes a {expected} file.");
            }
        });

        cmd.SetAction(parse =>
        {
            string file = parse.GetValue(input)!;
            string outPath = parse.GetValue(outp)!;
            var options = new RiseOptions
            {
                VoxelSize = (float)parse.GetValue(voxelSize),
                Pivot = ModelTokens.PivotOf(parse.GetValue(pivot)!),
                Format = ModelTokens.FormatOf(parse.GetValue(format)!),
            };
            (byte Front, byte Back)? fill = parse.IsGiven(front)
                ? ((byte)parse.GetValue(front), (byte)parse.GetValue(back))
                : null;

            return ModelInputOf(file) switch
            {
                ModelInput.Picture => ModelFromPicture(file, outPath, options, fill),
                ModelInput.Ingredient => ModelFromIngredient(file, parse.GetValue(variant)!, outPath, options, fill, ResolveThreads(parse)),
                ModelInput.Recipe => ModelFromRecipe(file, parse.GetValue(seed)!, outPath, options, ResolveThreads(parse)),
                // A backstop, not a reachable path: the validator has already refused anything that
                // is neither form, with a better-worded message.
                _ => throw new NotSupportedException(
                    $"model reads a picture (.png) or an Ingredient ({Archives.IngredientExtension}), not '{file}'."),
            };
        });
        return cmd;
    }

    /// <summary>The model format option, spelled <paramref name="name"/> — <c>--format</c> on
    /// <c>model</c>, <c>--model-format</c> on <c>generate</c>, which already writes images.</summary>
    private static Option<string> ModelFormatOption(string name)
    {
        var format = new Option<string>(name)
        {
            Description = "glb (one binary file), gltf (text JSON plus a .bin beside it) or "
                + "gltf-embedded (text JSON with the buffer inline).",
            DefaultValueFactory = _ => "glb",
        };
        format.AcceptOnlyFromAmong(ModelTokens.FormatNames.ToArray());
        return format;
    }

    private static Option<string> ModelPivotOption()
    {
        var pivot = new Option<string>("--pivot")
        {
            Description = "Which point of the model's bounding box sits at the origin: bottom-center "
                + "(standing on the floor), center, or min-corner.",
            DefaultValueFactory = _ => "bottom-center",
        };
        pivot.AcceptOnlyFromAmong(ModelTokens.PivotNames.ToArray());
        return pivot;
    }

    /// <summary>
    /// <c>--voxel-size</c>, parsed with the INVARIANT culture: the built-in converter reads the
    /// current one, so under de-DE <c>0.5</c> came out as <c>5</c> (see BACKLOG, "fractional CLI
    /// options parse culture-sensitively"). Shared by <c>model</c> and <c>generate</c> so the two
    /// cannot disagree; the range is <see cref="ModelVoxelSize"/>, the one the GUI keeps too.
    /// </summary>
    private static Option<double> ModelVoxelSizeOption() => new("--voxel-size")
    {
        Description = "World units per pixel, and per voxel of depth, from " + ModelVoxelSize.Min.ToString(CultureInfo.InvariantCulture) + " to " + ModelVoxelSize.Max.ToString(CultureInfo.InvariantCulture) + ".",
        DefaultValueFactory = _ => 1d,
        CustomParser = result =>
        {
            string token = result.Tokens.Count == 1 ? result.Tokens[0].Value : string.Empty;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return value;
            result.AddError($"--voxel-size must be a number; got '{token}'.");
            return 1d;
        },
    };

    /// <summary>Refuses a given voxel size outside <see cref="ModelVoxelSize"/>'s range. Only checked when
    /// GIVEN: an unset option has not run its default factory yet at validation time.</summary>
    private static void CheckVoxelSize(System.CommandLine.Parsing.CommandResult result, Option<double> voxelSize)
    {
        if (result.GetResult(voxelSize) is not { Implicit: false } given || given.Errors.Any()) return;
        if (ModelVoxelSize.ProblemWith(result.GetValue(voxelSize)) is { } problem)
            result.AddError(problem);
    }

    /// <summary>The line <c>generate</c> and <c>extend</c> print after writing models, plus the skipped
    /// line when some assets had nothing fully opaque. Only numbers from <paramref name="firstNumber"/>
    /// on are listed: <c>extend</c> reports its own assets, not the Set's whole history.</summary>
    private static void PrintModelsMade(Nfty.Core.Output.SetModels models, int firstNumber)
    {
        Console.WriteLine("models: " + Nfty.Core.Stats.SetReport.ModelsSummary(models));
        var skipped = (models.Skipped ?? []).Where(n => n >= firstNumber).ToList();
        if (skipped.Count > 0)
            Console.WriteLine("skipped (nothing fully opaque): "
                + string.Join(", ", skipped.Select(n => "#" + SetLayout.Stem(n))));
    }

    /// <summary>
    /// Which of <c>model</c>'s forms a path selects, or null for neither. A picture is recognized by
    /// its extension FIRST: it is not an archive kind, so <see cref="Archives.KindOf"/> would refuse
    /// it. Every other path goes through <see cref="Archives.TryKindOf"/> — the one owner of the
    /// extension-to-kind table — and only an Ingredient is accepted.
    /// </summary>
    private static ModelInput? ModelInputOf(string path) =>
        string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) ? ModelInput.Picture
        : Archives.TryKindOf(path, out var kind) && kind == ArchiveKind.Ingredient ? ModelInput.Ingredient
        : kind == ArchiveKind.Recipe ? ModelInput.Recipe
        : null;

    private static int ModelFromPicture(string file, string outPath, RiseOptions options, (byte Front, byte Back)? fill)
    {
        using var art = Image.Load<Rgba32>(file);
        DepthMap? depth = fill is { } f ? DepthFill.All(art, f.Front, f.Back) : null;
        return WriteModel(art, depth, options, outPath, $"'{file}'");
    }

    private static int ModelFromIngredient(string file, string variantId, string outPath, RiseOptions options,
        (byte Front, byte Back)? fill, Parallelism threads)
    {
        using var ing = IngredientArchive.Read(file, threads);
        if (!ing.VariantImages.TryGetValue(variantId, out var art))
            throw new ArgumentException(
                $"Ingredient '{ing.Manifest.Id}' has no variant '{variantId}'; its variants are "
                + string.Join(", ", ing.Manifest.Variants.Select(v => v.Id).Order(StringComparer.Ordinal)) + ".");

        DepthMap? depth = fill is { } f ? DepthFill.All(art, f.Front, f.Back) : ing.Depths.GetValueOrDefault(variantId);
        return WriteModel(art, depth, options, outPath, $"variant '{variantId}' of '{file}'");
    }

    /// <summary>Rolls a recipe the way <c>preview x.rcp --seed S</c> does and rises the whole stack
    /// through <see cref="StackDepth.Combine"/>, the one stack rule the Rise page and a cooked Set use.</summary>
    private static int ModelFromRecipe(string file, string seed, string outPath, RiseOptions options, Parallelism threads)
    {
        using var recipe = RecipeArchive.Read(file, threads);
        var problems = Validator.ValidateRecipe(recipe);
        if (problems.Count > 0) { Report(problems); return 1; }

        var rolled = StackRoll.ForRecipe(recipe, StackRoll.RngFor(seed));
        string source = $"the {recipe.Manifest.Name} recipe (seed '{seed}')";
        if (rolled.Count == 0)
            throw new InvalidOperationException($"{source} rolled no layer at all, so there is nothing to rise.");

        var images = new List<Image<Rgba32>>(rolled.Count);
        try
        {
            var pairs = new List<(Image<Rgba32>, DepthMap?)>(rolled.Count);
            foreach (var layer in rolled)
            {
                // Rendered as StackPreview.Render does: the rolled color wins over its spelling.
                var image = layer.Render();
                images.Add(image);
                pairs.Add((image, layer.Ingredient.Depths.GetValueOrDefault(layer.VariantId)));
            }
            using var rise = StackDepth.Combine(pairs, source);
            return WriteModel(rise.Art, rise.Depth, options, outPath, source, rise.Faces);
        }
        finally
        {
            foreach (var image in images) image.Dispose();
        }
    }

    private static int WriteModel(Image<Rgba32> art, DepthMap? depth, RiseOptions options, string outPath, string source,
        StackFaces? faces = null)
    {
        var mesh = Riser.TryBuild(art, depth, options, source, faces)
            ?? throw new InvalidOperationException($"{source} has no fully opaque pixel, so there is nothing to rise.");

        string dir = Path.GetDirectoryName(Path.GetFullPath(outPath))!;
        var files = ModelWriter.Write(mesh, art, options, Path.GetFileNameWithoutExtension(outPath), faces);
        Directory.CreateDirectory(dir);

        // ModelWriter names every file with ITS OWN canonical lower-case extension (.glb, .gltf,
        // .bin); the validator only compared --out's extension case-INsensitively, so the casing
        // the user actually typed (e.g. "Model.GLB") must be restored here for the one file --out
        // names. A sibling file with a different extension (the .bin next to a .gltf) was never
        // named by --out, so its casing is untouched.
        string outExt = Path.GetExtension(outPath);
        var written = new List<string>(files.Count);
        foreach (var f in files)
        {
            string name = string.Equals(Path.GetExtension(f.FileName), outExt, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(f.FileName) + outExt
                : f.FileName;
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, f.Bytes);
            written.Add(path);
        }

        Console.WriteLine($"Wrote {string.Join(" and ", written)} ("
            + mesh.TriangleCount.ToString("N0", CultureInfo.InvariantCulture) + " triangles)");
        return 0;
    }
}
