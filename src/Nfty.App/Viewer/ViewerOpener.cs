using Nfty.Core.Imaging;
using System.Globalization;
using System.Text.Json;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Viewer;

/// <summary>
/// Turns a path into what the viewer shows: a gallery for an archive, a single subject for a
/// picture, a model file or an asset's metadata file. Anything else says so on the error card and
/// opens nothing.
/// </summary>
/// <remarks>
/// <para>Archives are recognised by <see cref="Archives.TryKindOf"/> (plus a Set folder, which has no
/// extension to ask about), never by guessing. A sealed <c>.tin</c> shows what it is, asks for the
/// passphrase and asks again when it is wrong, exactly as the Landing page does, and every tile of
/// what comes out is view-only.</para>
/// <para>Nothing here writes: the viewer only reads.</para>
/// </remarks>
public static class ViewerOpener
{
    /// <summary>Every extension the viewer opens, for a file picker's filter.</summary>
    public static readonly string[] Extensions =
    {
        ".cbk", ".rcp", ".igt", Archives.SetExtension, Archives.SealedExtension, ".mnu", ".ktn",
        ".png", ".jpg", ".jpeg", ".glb", ".gltf", ".json",
    };

    /// <summary>What the error card says for a JSON file that is not an asset's metadata.</summary>
    public const string NotMetadata = "Not an nfty asset metadata file.";

    /// <summary>Opens a path for viewing.</summary>
    /// <param name="path">A file or Set folder.</param>
    /// <param name="dialogs">The dialog layer: the busy card, the passphrase card and the error card.</param>
    /// <param name="ct">Cancels the open.</param>
    /// <param name="host">Where a gallery's opened tiles go; null leaves tiles inert.</param>
    /// <param name="bookCandidates">CookBook paths the app can name, so a Set asset can show its model.</param>
    /// <returns>The subject (the caller owns it), or null when it could not be opened or the reader
    /// canceled; the reason has already been shown.</returns>
    public static async Task<ViewerSubject?> OpenAsync(string path, IDialogService dialogs,
        CancellationToken ct = default, IViewerHost? host = null,
        Func<IEnumerable<string>>? bookCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        try
        {
            var subject = await OpenCoreAsync(path, dialogs, ct, host, bookCandidates);
            if (subject is GallerySubject g) { g.Gallery.Host = host; g.Gallery.Dialogs = dialogs; }
            return subject;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            await dialogs.ShowErrorAsync("Could not open", Failure(path, ex));
            return null;
        }
    }

    /// <summary>
    /// The sentence a failed open shows. The engine's own exceptions carry sentences written to be
    /// shown; the framework's (a damaged ZIP's "End of Central Directory record could not be found",
    /// a picture codec's complaint, an access refusal) do not, and are worded here instead - a file
    /// double-clicked in Explorer reaches this path with nothing else on screen to explain it.
    /// </summary>
    /// <param name="path">What was being opened.</param>
    /// <param name="ex">Why it failed.</param>
    /// <returns>The sentence.</returns>
    public static string Failure(string path, Exception ex)
    {
        string name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return ex switch
        {
            InvalidDataException or UnknownImageFormatException or InvalidImageContentException
                or JsonException or EndOfStreamException =>
                $"“{name}” could not be read: it is damaged, only partly downloaded, or not the kind of file its name says. Download or export it again.",
            UnauthorizedAccessException or System.Security.SecurityException =>
                $"nfty is not allowed to read “{name}”. Check the file's permissions, or copy it somewhere you can read, then open it again.",
            FileNotFoundException or DirectoryNotFoundException =>
                $"“{name}” is not there any more, so there is nothing to show.",
            IOException =>
                $"“{name}” could not be read: another program may have it open. Close it there and try again.",
            _ => ErrorText.For(ex),
        };
    }

    private static async Task<ViewerSubject?> OpenCoreAsync(string path, IDialogService dialogs,
        CancellationToken ct, IViewerHost? host, Func<IEnumerable<string>>? bookCandidates)
    {
        if (Directory.Exists(path))
        {
            if (SetReader.IsSetFolder(path)) return await OpenSetAsync(path, dialogs, ct, bookCandidates);
            if (MarketplaceBundle.IsBundle(path)) return await OpenBundleAsync(path, dialogs, ct);
            await dialogs.ShowErrorAsync("Could not open", $"“{path}” is a folder, but not a cooked Set.");
            return null;
        }
        if (!File.Exists(path))
        {
            await dialogs.ShowErrorAsync("Missing file", $"“{path}” is not there, so there is nothing to show.");
            return null;
        }

        if (Archives.TryKindOf(path, out var kind))
        {
            switch (kind)
            {
                case ArchiveKind.CookBook: return Gallery(GalleryBuilder.ForCookBook(path));
                case ArchiveKind.Recipe: return Gallery(GalleryBuilder.ForRecipe(path));
                case ArchiveKind.Ingredient: return Gallery(GalleryBuilder.ForIngredient(path));
                case ArchiveKind.Menu: return Gallery(GalleryBuilder.ForMenu(path));
                case ArchiveKind.Set:
                    if (MarketplaceBundle.IsBundle(path)) return await OpenBundleAsync(path, dialogs, ct);
                    return await OpenSetAsync(path, dialogs, ct, bookCandidates);
                case ArchiveKind.Sealed: return await OpenSealedAsync(path, dialogs, bookCandidates);
                case ArchiveKind.Kitchen:
                    return Gallery(GalleryBuilder.ForKitchen(path, async p =>
                    {
                        var inner = await OpenAsync(p, dialogs, ct, host, bookCandidates);
                        return inner;
                    }));
            }
        }

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png" or ".jpg" or ".jpeg":
                return await Task.Run<ViewerSubject>(() =>
                {
                    using var art = ImageFormats.Load(path);
                    var facts = new List<ViewerFact>
                    {
                        new("FILE", Path.GetFileName(path), Group: ViewerFactGroup.File),
                        new("SIZE", string.Create(CultureInfo.InvariantCulture, $"{art.Width} x {art.Height}")),
                    };
                    return new SnapshotSubject(Path.GetFileName(path), facts, art, null);
                }, ct);
            case ".glb" or ".gltf":
                try { return await Task.Run(() => ModelSubjects.ForModelFile(path), ct); }
                catch (UnsupportedModelException ex)
                {
                    await dialogs.ShowErrorAsync("Could not open the model", ErrorText.For(ex));
                    return null;
                }
            case ".json":
                var meta = await Task.Run(() => MetadataSubject.Open(path), ct);
                if (meta is null) await dialogs.ShowErrorAsync("Could not open", NotMetadata);
                return meta;
        }

        // A marketplace export with token-id naming writes metadata/1, no extension: recognised by
        // what is inside (an OpenSea item: a JSON object with a string name and image), never by name.
        if (Path.GetExtension(path).Length == 0 && LooksLikeMetadata(path))
        {
            var bare = await Task.Run(() => MetadataSubject.Open(path), ct);
            if (bare is null) await dialogs.ShowErrorAsync("Could not open", NotMetadata);
            return bare;
        }

        await dialogs.ShowErrorAsync("Could not open",
            $"nfty cannot show “{Path.GetFileName(path)}”: its extension is not one the viewer opens.");
        return null;
    }

    /// <summary>Whether an extensionless file is an OpenSea metadata item: a small JSON object whose
    /// <c>name</c> and <c>image</c> are strings.</summary>
    /// <param name="path">The file.</param>
    /// <returns><c>true</c> when it is.</returns>
    internal static bool LooksLikeMetadata(string path)
    {
        try
        {
            if (new FileInfo(path).Length is 0 or > 1_048_576) return false;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            return doc.RootElement is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                && root.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return false; }
    }

    private static GallerySubject Gallery(ArchiveGalleryViewModel g) => new(g);

    /// <summary>Opens a marketplace upload bundle (no <c>set.json</c>) read-only, as a gallery marked as one.</summary>
    private static async Task<ViewerSubject?> OpenBundleAsync(string path, IDialogService dialogs, CancellationToken ct)
    {
        var bundle = await BusyViewModel.RunAsync(dialogs, "Opening upload bundle", "Reading the bundle…",
            (_, token) => Task.Run(() => MarketplaceBundle.Read(path), token), cancellable: true);
        ct.ThrowIfCancellationRequested();
        return Gallery(GalleryBuilder.ForBundle(bundle));
    }

    private static async Task<ViewerSubject?> OpenSetAsync(string path, IDialogService dialogs,
        CancellationToken ct, Func<IEnumerable<string>>? bookCandidates)
    {
        var set = await BusyViewModel.RunAsync(dialogs, "Opening Set", "Reading the Set…",
            (_, token) => SetReader.ReadAsync(path, token), cancellable: true);
        ct.ThrowIfCancellationRequested();
        return Gallery(GalleryBuilder.ForSet(set, null, bookCandidates));
    }

    /// <summary>Shows what the sealed file is, asks for its passphrase, and asks again while it is
    /// wrong. Canceling the card opens nothing.</summary>
    private static async Task<ViewerSubject?> OpenSealedAsync(string path, IDialogService dialogs,
        Func<IEnumerable<string>>? bookCandidates)
    {
        var header = Seal.Peek(path);
        var prompt = new PassphraseDialogViewModel(header, dialogs);
        while (true)
        {
            var passphrase = await dialogs.ShowAsync<string>(prompt);
            if (string.IsNullOrEmpty(passphrase)) return null;
            try
            {
                var set = await BusyViewModel.RunAsync(dialogs, "Opening sealed export", "Decrypting…",
                    (_, _) => Task.Run(() => SealedSetReader.Open(path, passphrase)));
                return Gallery(GalleryBuilder.ForSet(set, null, bookCandidates));
            }
            catch (SealedSetException ex)
            {
                prompt.Error = ErrorText.For(ex);
                prompt.Passphrase = "";
            }
        }
    }
}

/// <summary>A single asset's metadata file (<c>nfty/NNNN.json</c> or <c>metadata/NNNN.json</c>) shown
/// with the picture it describes, found through <see cref="SetLayout"/> beside it.</summary>
internal static class MetadataSubject
{
    /// <summary>Reads a metadata file.</summary>
    /// <param name="path">The JSON file.</param>
    /// <returns>A subject, or null when the file is not one of the two shapes or does not sit in a
    /// Set's <c>nfty/</c> or <c>metadata/</c> folder.</returns>
    public static ViewerSubject? Open(string path)
    {
        string json;
        try { json = File.ReadAllText(path); } catch (IOException) { return null; }

        var facts = new List<ViewerFact>();
        if (Try<NftyMetadata>(json) is { Dna: not null, Rarity: not null } rich)
        {
            facts.Add(new("ASSET", "#" + SetLayout.Stem(rich.SetNumber)));
            facts.Add(new("RECIPE", rich.Recipe));
            facts.Add(new("DNA", rich.Dna));
            foreach (var r in rich.Rarity)
                facts.Add(new(r.Trait_type.ToUpperInvariant(),
                    string.Create(CultureInfo.InvariantCulture, $"{r.Value} ({r.RarityPct:0.##}%)"), Group: ViewerFactGroup.Traits));
        }
        else if (Try<OpenSeaMetadata>(json) is { Image: not null, Attributes: not null } open)
        {
            facts.Add(new("NAME", open.Name));
            foreach (var a in open.Attributes) facts.Add(new(a.Trait_type.ToUpperInvariant(), a.Value, Group: ViewerFactGroup.Traits));
        }
        else return null;

        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        string kind = Path.GetFileName(folder);
        if (kind is not (SetLayout.NftyDir or SetLayout.MetadataDir)) return null;
        string root = Path.GetDirectoryName(folder) ?? "";
        // The picture is named by the Set's own naming (an export may use token ids), or - in an upload
        // bundle with no set.json - by the metadata's own image field.
        string png;
        if (File.Exists(SetLayout.ManifestPath(root)))
        {
            var naming = SetLayout.NamingOf(root);
            int? number = kind == SetLayout.NftyDir && Try<NftyMetadata>(json) is { } n ? n.SetNumber
                : naming.NumberOf(path);
            png = Path.Combine(root, SetLayout.ImagesDir,
                number is { } known ? naming.ImageFile(known) : Path.GetFileNameWithoutExtension(path) + ".png");
        }
        else
        {
            png = Try<OpenSeaMetadata>(json) is { } meta
                ? MarketplaceBundle.ResolveImage(root, meta.Image) ?? Path.Combine(root, SetLayout.ImagesDir, "missing.png")
                : Path.Combine(root, SetLayout.ImagesDir, Path.GetFileNameWithoutExtension(path) + ".png");
        }

        Image<Rgba32>? art = null;
        try { art = ImageFormats.Load(png); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException
                                       or InvalidImageContentException) { /* the missing mark says so */ }

        // An OpenSea item that links a model (animation_url) shows it in 3D when the file sits in the
        // export's own models/ folder - mapped by file name, never fetched.
        var model = AnimationUrl(json) is { Length: > 0 } link
            && MarketplaceBundle.ResolveModel(root, link) is { } modelPath
                ? ModelSubjects.TryModel(modelPath) : null;

        // The title is the item's published name ("Chest Demo #1"), not the file's ("1").
        string title = Try<OpenSeaMetadata>(json) is { Name: { Length: > 0 } name } ? name : Path.GetFileName(path);
        using (art)
        using (model?.Texture)
            return new SnapshotSubject(title, facts, art, model) { ImageMissing = art is null };
    }

    // OpenSeaMetadata is the standards-pure record nfty WRITES and carries no animation_url; an
    // export's item does when its models were linked, so it is read straight off the JSON.
    private static string? AnimationUrl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("animation_url", out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static T? Try<T>(string json) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, Json.Options); }
        catch (JsonException) { return null; }
    }
}
