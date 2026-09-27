using System.CommandLine;
using System.Globalization;
using Nfty.Core.Formats;
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
    }

    private static readonly string[] ModelFormatNames = ["glb", "gltf", "gltf-embedded"];
    private static readonly string[] PivotNames = ["bottom-center", "center", "min-corner"];

    private static Command ModelCommand()
    {
        var input = new Argument<string>("input")
        {
            Description = "A .png picture, or an .igt Ingredient together with --variant. Which one "
                + "you pass chooses the form.",
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
        var format = new Option<string>("--format")
        {
            Description = "glb (one binary file), gltf (text JSON plus a .bin beside it) or "
                + "gltf-embedded (text JSON with the buffer inline).",
            DefaultValueFactory = _ => "glb",
        };
        format.AcceptOnlyFromAmong(ModelFormatNames);
        var pivot = new Option<string>("--pivot")
        {
            Description = "Which point of the model's bounding box sits at the origin: bottom-center "
                + "(standing on the floor), center, or min-corner.",
            DefaultValueFactory = _ => "bottom-center",
        };
        pivot.AcceptOnlyFromAmong(PivotNames);
        var voxelSize = new Option<float>("--voxel-size")
        {
            Description = "World units per pixel, and per voxel of depth. Must be positive.",
            DefaultValueFactory = _ => 1f,
        };

        var cmd = new Command("model",
            "Rise a picture into a 3D model: every fully opaque pixel becomes a column of voxels — one "
                + "for the paper plus its front and back depth — colored with the picture's own pixels. "
                + "A partly transparent pixel is refused by name, never rounded.")
        { input, outp, variant, front, back, format, pivot, voxelSize };

        // Which options are legal depends on the FORM the input selects, and an option that silently
        // does nothing is worse than one that says no — the rule `preview` keeps. Enforced at parse
        // time, where the error names the form.
        cmd.Validators.Add(result =>
        {
            string? file = result.GetValue(input);
            if (string.IsNullOrEmpty(file)) return;   // the missing-argument error already speaks

            bool Given(Option option) => result.GetResult(option) is { Implicit: false };

            switch (ModelInputOf(file))
            {
                case null:
                    result.AddError($"model reads a picture (.png) or an Ingredient "
                        + $"({Archives.IngredientExtension}) with --variant; '{file}' is neither.");
                    return;
                case ModelInput.Picture when Given(variant):
                    result.AddError("--variant names one variant of an .igt; a .png is one picture already.");
                    break;
                case ModelInput.Ingredient when !Given(variant):
                    result.AddError($"--variant is required to model '{file}': an Ingredient holds "
                        + "several variants and a model is made of one.");
                    break;
            }

            if (Given(front) != Given(back))
                result.AddError("--front and --back fill every opaque pixel together; pass both "
                    + "(0 leaves a side flat).");
            foreach (var (option, name) in new (Option<int> Option, string Name)[] { (front, "--front"), (back, "--back") })
            {
                int value = result.GetValue(option);
                if (Given(option) && value is < 0 or > 255)
                    result.AddError($"{name} must be 0..255 (one byte per side); got {value}.");
            }

            // Only checked when GIVEN: an unset option has not run its DefaultValueFactory yet at
            // this point in the parse (it always reads back as 1f's own type default, 0, otherwise)
            // — the same reason front/back's own range check is gated on Given rather than trusting
            // whatever GetValue answers for an option nobody typed.
            if (Given(voxelSize))
            {
                float size = result.GetValue(voxelSize);
                if (!float.IsFinite(size) || size <= 0f)
                    result.AddError("--voxel-size must be a positive number of world units per pixel; got "
                        + size.ToString(CultureInfo.InvariantCulture) + ".");
            }

            string? outPath = result.GetValue(outp);

            // Read as a raw TOKEN, never through GetValue: format.AcceptOnlyFromAmong has already
            // flagged an unrecognized value (like "obj") as its own parse error, and calling
            // GetValue on an option whose value failed that check throws instead of returning it —
            // this is the one place that value is needed for something AcceptOnlyFromAmong does not
            // already say, so it must not re-trip the same failure into an unhandled exception.
            string fmt = Given(format) ? result.GetResult(format)!.Tokens[0].Value : "glb";
            if (outPath is not null && ModelFormatNames.Contains(fmt, StringComparer.Ordinal))
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
                VoxelSize = parse.GetValue(voxelSize),
                Pivot = PivotOf(parse.GetValue(pivot)!),
                Format = ModelFormatOf(parse.GetValue(format)!),
            };
            (byte Front, byte Back)? fill = parse.GetResult(front) is { Implicit: false }
                ? ((byte)parse.GetValue(front), (byte)parse.GetValue(back))
                : null;

            return ModelInputOf(file) switch
            {
                ModelInput.Picture => ModelFromPicture(file, outPath, options, fill),
                ModelInput.Ingredient => ModelFromIngredient(file, parse.GetValue(variant)!, outPath, options, fill),
                // A backstop, not a reachable path: the validator has already refused anything that
                // is neither form, with a better-worded message.
                _ => throw new NotSupportedException(
                    $"model reads a picture (.png) or an Ingredient ({Archives.IngredientExtension}), not '{file}'."),
            };
        });
        return cmd;
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
        : null;

    private static int ModelFromPicture(string file, string outPath, RiseOptions options, (byte Front, byte Back)? fill)
    {
        using var art = Image.Load<Rgba32>(file);
        DepthMap? depth = fill is { } f ? DepthFill.All(art, f.Front, f.Back) : null;
        return WriteModel(art, depth, options, outPath, $"'{file}'");
    }

    private static int ModelFromIngredient(string file, string variantId, string outPath, RiseOptions options,
        (byte Front, byte Back)? fill)
    {
        using var ing = IngredientArchive.Read(file);
        if (!ing.VariantImages.TryGetValue(variantId, out var art))
            throw new ArgumentException(
                $"Ingredient '{ing.Manifest.Id}' has no variant '{variantId}'; its variants are "
                + string.Join(", ", ing.Manifest.Variants.Select(v => v.Id).Order(StringComparer.Ordinal)) + ".");

        DepthMap? depth = fill is { } f ? DepthFill.All(art, f.Front, f.Back) : ing.Depths.GetValueOrDefault(variantId);
        return WriteModel(art, depth, options, outPath, $"variant '{variantId}' of '{file}'");
    }

    private static int WriteModel(Image<Rgba32> art, DepthMap? depth, RiseOptions options, string outPath, string source)
    {
        var mesh = Riser.TryBuild(art, depth, options, source)
            ?? throw new InvalidOperationException($"{source} has no fully opaque pixel, so there is nothing to rise.");

        string dir = Path.GetDirectoryName(Path.GetFullPath(outPath))!;
        var files = ModelWriter.Write(mesh, art, options, Path.GetFileNameWithoutExtension(outPath));
        Directory.CreateDirectory(dir);
        foreach (var f in files)
            File.WriteAllBytes(Path.Combine(dir, f.FileName), f.Bytes);

        Console.WriteLine($"Wrote {string.Join(" and ", files.Select(f => Path.Combine(dir, f.FileName)))} ("
            + mesh.TriangleCount.ToString("N0", CultureInfo.InvariantCulture) + " triangles)");
        return 0;
    }

    private static Pivot PivotOf(string name) => name switch
    {
        "bottom-center" => Pivot.BottomCenter,
        "center" => Pivot.Center,
        "min-corner" => Pivot.MinCorner,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown pivot."),
    };

    private static ModelFormat ModelFormatOf(string name) => name switch
    {
        "glb" => ModelFormat.Glb,
        "gltf" => ModelFormat.GltfWithBin,
        "gltf-embedded" => ModelFormat.GltfEmbedded,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown model format."),
    };
}
