using Nfty.Core.Diagnostics;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Output;
using Nfty.Core.Stats;

namespace Nfty.Model;

/// <summary>
/// Rises every asset of a cook into model files: <see cref="StackDepth.ForAsset"/> for the depth,
/// <see cref="Riser.TryBuild"/> for the solid, <see cref="ModelWriter"/> for the bytes.
/// </summary>
/// <remarks>
/// Reads only — the book and the asset's picture are never mutated — so concurrent calls for
/// different assets are safe and the bytes depend on the asset alone, at any thread count.
/// </remarks>
public sealed class SetModeler : IAssetModeler
{
    private readonly string _bookName;
    private readonly RiseOptions _options;
    private readonly SetModels _record;
    private readonly Dictionary<string, LoadedRecipe> _recipes;

    /// <summary>Creates the modeler.</summary>
    /// <param name="book">The book the Set is cooked from.</param>
    /// <param name="options">How to rise and write.</param>
    /// <exception cref="PartialAlphaBookException">Some variant has a partly transparent pixel.</exception>
    public SetModeler(LoadedCookBook book, RiseOptions options)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(options);
        if (Opacity.HasPartialAlpha(book))
            throw new PartialAlphaBookException(book.Manifest.Name,
                VoxelReport.Scan(book).Where(v => !v.IsClean).ToList());

        _bookName = book.Manifest.Name;
        _options = options;
        _record = ModelTokens.Record(options);
        _recipes = new Dictionary<string, LoadedRecipe>(StringComparer.Ordinal);
        foreach (var r in book.Recipes) _recipes[r.Manifest.Id] = r;
    }

    /// <inheritdoc />
    public SetModels Options => _record;

    /// <inheritdoc />
    public IReadOnlyList<string> FileNames(string baseName) => _options.Format switch
    {
        ModelFormat.Glb => [baseName + ".glb"],
        ModelFormat.GltfWithBin => [baseName + ".gltf", baseName + ".bin"],
        ModelFormat.GltfEmbedded => [baseName + ".gltf"],
        _ => throw new ArgumentOutOfRangeException(nameof(baseName), _options.Format, "Unknown model format."),
    };

    /// <inheritdoc />
    public IReadOnlyList<SetFile>? Model(GeneratedAsset asset, string baseName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        using var perf = Perf.Measure("SetModeler.Model");
        if (!_recipes.TryGetValue(asset.RecipeId, out var recipe))
            throw new KeyNotFoundException(
                $"asset #{asset.SetNumber:D4}: CookBook '{_bookName}' has no recipe '{asset.RecipeId}'.");

        string source = $"asset #{asset.SetNumber:D4}";
        var depth = StackDepth.ForAsset(recipe, asset, source);
        var mesh = Riser.TryBuild(asset.Image, depth, _options, source);
        if (mesh is null) return null;
        return ModelWriter.Write(mesh, asset.Image, _options, baseName)
            .Select(f => new SetFile(f.FileName, f.Bytes)).ToList();
    }
}
