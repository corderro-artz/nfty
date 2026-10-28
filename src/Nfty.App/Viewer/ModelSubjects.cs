using System.Globalization;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.App.Viewer;

/// <summary>
/// Builds viewer subjects whose 3D half comes from somewhere other than a live page: a Set asset's
/// recorded model, the same asset risen from the CookBook that cooked it, or a loose model file.
/// </summary>
public static class ModelSubjects
{
    /// <summary>
    /// The viewer subject for one asset of a cooked Set. The model is, in order: the model file the
    /// Set recorded for it (<c>set.json</c> has a <c>models</c> block and the file is there); else the
    /// asset risen from the CookBook found by hash, as the Rise page does; else none, and the subject
    /// is 2D only. A sealed Set can be viewed but its subject refuses export.
    /// </summary>
    /// <param name="set">The Set; borrowed, the subject does not outlive it.</param>
    /// <param name="item">The asset.</param>
    /// <param name="bookCandidates">Paths of CookBooks the app can already name; read on the calling
    /// thread, as <see cref="SetRiseSource"/> requires.</param>
    /// <returns>A snapshot subject the caller owns.</returns>
    public static async Task<SnapshotSubject> ForSetAssetAsync(LoadedSet set, SetItem item,
        Func<IEnumerable<string>>? bookCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(item);
        using var art = LoadArt(item.ImagePath);
        string number = string.Create(CultureInfo.InvariantCulture, $"{item.Number:D4}");
        var facts = new List<ViewerFact>
        {
            new("ASSET", $"#{number}"),
            new("RECIPE", item.Recipe),
            new("SIZE", string.Create(CultureInfo.InvariantCulture, $"{art.Width} x {art.Height}")),
        };
        bool allow = set is not SealedSet;

        var recorded = RecordedModel(set, item);
        if (recorded is not null)
        {
            facts.Add(new("MODEL", "recorded in the Set"));
            using var owned = recorded.Texture;   // the subject clones what it is given
            return new SnapshotSubject($"{set.Manifest.Name} #{number}", facts, art, recorded) { AllowExport = allow };
        }

        var risen = await RiseFromBook(set, item, bookCandidates);
        using var ownedTexture = risen?.Texture;
        {
            if (risen is not null) facts.Add(new("MODEL", "risen from the CookBook that cooked this Set"));
            else facts.Add(new("MODEL", set.Manifest.CookbookSha256 is { Length: > 0 }
                ? "none: the CookBook was not found, or the asset cannot rise" : "none: this Set records no book"));
            return new SnapshotSubject($"{set.Manifest.Name} #{number}", facts, art, risen) { AllowExport = allow };
        }
    }

    /// <summary>
    /// The viewer subject for a loose <c>.glb</c> or <c>.gltf</c> nfty wrote. Its 2D art is the front
    /// half of the model's own texture atlas.
    /// </summary>
    /// <param name="path">The model file.</param>
    /// <returns>A snapshot subject the caller owns.</returns>
    /// <exception cref="UnsupportedModelException">The file is not one nfty could have written.</exception>
    public static SnapshotSubject ForModelFile(string path)
    {
        var read = GltfReader.Read(path);
        using var texture = Image.Load<Rgba32>(read.TexturePng);
        using var art = FrontHalf(texture);
        string dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        string stem = Path.GetFileNameWithoutExtension(path);
        var files = new List<ModelFile>();
        foreach (string ext in new[] { Path.GetExtension(path), ".bin" })
        {
            string f = Path.Combine(dir, stem + ext);
            if (File.Exists(f) && !files.Any(x => x.FileName == Path.GetFileName(f)))
                files.Add(new ModelFile(Path.GetFileName(f), File.ReadAllBytes(f)));
        }
        var facts = new List<ViewerFact>
        {
            new("FILE", Path.GetFileName(path)),
            new("TRIANGLES", read.Mesh.TriangleCount.ToString("N0", CultureInfo.InvariantCulture)),
        };
        return new SnapshotSubject(Path.GetFileName(path), facts, art,
            new ViewerModel(read.Mesh, texture, files));
    }

    private static ViewerModel? RecordedModel(LoadedSet set, SetItem item)
    {
        if (set.Manifest.Models is null || string.IsNullOrEmpty(set.SourceDirectory)) return null;
        string stem = SetLayout.Stem(item.Number);
        var files = SetLayout.ModelFilesIn(set.SourceDirectory)
            .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.Ordinal))
            .ToList();
        string? main = files.FirstOrDefault(f => Path.GetExtension(f) is ".glb" or ".gltf");
        if (main is null) return null;
        try
        {
            var read = GltfReader.Read(main);
            var texture = Image.Load<Rgba32>(read.TexturePng);
            var model = files.Select(f => new ModelFile(Path.GetFileName(f), File.ReadAllBytes(f))).ToList();
            return new ViewerModel(read.Mesh, texture, model);
        }
        catch (Exception ex) when (ex is UnsupportedModelException or IOException or UnauthorizedAccessException)
        {
            return null;   // a damaged or foreign file: fall back to rising it, never refuse to open
        }
    }

    private static async Task<ViewerModel?> RiseFromBook(LoadedSet set, SetItem item,
        Func<IEnumerable<string>>? bookCandidates)
    {
        if (set.Manifest.CookbookSha256 is not { Length: > 0 }) return null;
        using var source = new SetRiseSource(set, item.Number, bookCandidates, new ImageBridge());
        await source.ExportReady;
        if (source.BookName is null) return null;   // no book: every asset would be a flat slab of paper, which is not a model of it
        var chosen = source.Items.First(i => int.Parse(i.Id, CultureInfo.InvariantCulture) == item.Number);
        var subject = source.Open(chosen);
        if (!subject.IsRisable) return null;
        var mesh = Riser.TryBuild(subject.Art, subject.Depth.ToDepthMap(), new RiseOptions(), source.Title, subject.Faces);
        return mesh is null ? null : new ViewerModel(mesh, subject.Texture.Clone(), null);
    }

    private static Image<Rgba32> FrontHalf(Image<Rgba32> atlas)
    {
        int width = (atlas.Width - 1) / 2;
        return atlas.Clone(c => c.Crop(new Rectangle(0, 0, Math.Max(1, width), atlas.Height)));
    }

    private static Image<Rgba32> LoadArt(string path)
    {
        try { return Image.Load<Rgba32>(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException
                                       or InvalidImageContentException)
        {
            return new Image<Rgba32>(1, 1);
        }
    }
}
