using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Output;

namespace Nfty.Core.Publish;

/// <summary>One item of an upload bundle, as its metadata file describes it.</summary>
/// <param name="Number">Its position: the <c>#n</c> in its name, else the number its file is named,
/// else its place in the listing.</param>
/// <param name="File">The metadata file's name inside <c>metadata/</c>.</param>
/// <param name="Name">The item's published name.</param>
/// <param name="Description">The item's description; empty when it has none.</param>
/// <param name="ImageField">The <c>image</c> field exactly as the metadata writes it.</param>
/// <param name="ImagePath">The picture on disk that field resolves to inside the bundle, or null when
/// the bundle does not carry it.</param>
/// <param name="ExternalUrl">The <c>external_url</c>, or null.</param>
/// <param name="AnimationUrl">The <c>animation_url</c>, or null.</param>
/// <param name="Attributes">Its traits, in file order.</param>
public sealed record BundleItem(int Number, string File, string Name, string Description, string ImageField,
    string? ImagePath, string? ExternalUrl, string? AnimationUrl, IReadOnlyList<MetadataAttribute> Attributes);

/// <summary><c>collection.json</c> as an upload bundle carries it. Every field is optional but the name.</summary>
/// <param name="Name">The collection's name.</param>
/// <param name="Description">Its description; empty when absent.</param>
/// <param name="Image">The collection image link, or null.</param>
/// <param name="ExternalLink">The collection's site, or null.</param>
/// <param name="SellerFeeBasisPoints">The royalty in basis points, or null.</param>
/// <param name="FeeRecipient">Who the royalty is paid to, or null.</param>
/// <param name="Symbol">The ticker-style symbol, or null.</param>
public sealed record BundleCollection(string Name, string Description, string? Image, string? ExternalLink,
    int? SellerFeeBasisPoints, string? FeeRecipient, string? Symbol);

/// <summary>An upload bundle opened for looking at: a marketplace export that left <c>set.json</c> out.
/// Read only; owns the temporary folder when it came from a packed <c>.set</c>.</summary>
public sealed class LoadedBundle : IDisposable
{
    /// <summary>What to call it: the collection's name, else the folder's.</summary>
    public required string Name { get; init; }

    /// <summary>The items, in number order.</summary>
    public required IReadOnlyList<BundleItem> Items { get; init; }

    /// <summary>The contract-level file, or null when the bundle carries none.</summary>
    public BundleCollection? Collection { get; init; }

    /// <summary>Metadata files (or <c>collection.json</c>) that could not be read, each with the reason.
    /// Listed rather than thrown: the point of opening a bundle is to find what is wrong with it.</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>The folder the bundle's files are in.</summary>
    public string SourceDirectory { get; init; } = string.Empty;

    internal string? TempDir { get; init; }

    /// <summary>Removes the temporary folder a packed bundle was unpacked into.</summary>
    public void Dispose()
    {
        if (TempDir is not null && Directory.Exists(TempDir))
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Recognizes and reads a marketplace upload bundle: <c>images/</c> and <c>metadata/</c> (and
/// optionally <c>collection.json</c>) with no <c>set.json</c>. It is NOT a Set - it records no seed,
/// no rarity and no DNA - so it is opened read-only, to check what is about to be uploaded.
/// </summary>
/// <remarks>
/// <para><b>The test is exact, not a guess at arbitrary folders.</b> A folder or <c>.set</c> archive
/// is a bundle when it has NO <c>set.json</c>, has a <c>metadata/</c> folder, and the first file in
/// that folder (ordinal order) is a JSON object with a string <c>name</c>, a string <c>image</c> and an
/// <c>attributes</c> array - OpenSea's item shape. Anything else is not claimed.</para>
/// <para><b>Images resolve inside the bundle, never over the network.</b> A relative <c>image</c> is a
/// path in the bundle; an absolute link (<c>ipfs://</c>, <c>https://</c>) is mapped by its trailing
/// <c>images/&lt;file&gt;</c> onto the bundle's own <c>images/</c> folder, which is where the bundle
/// will be uploaded from.</para>
/// </remarks>
public static class MarketplaceBundle
{
    /// <summary>Whether <paramref name="path"/> is an upload bundle: a folder, or a packed <c>.set</c>.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns>True under the rule in the type's remarks.</returns>
    public static bool IsBundle(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (Directory.Exists(path)) return IsBundleFolder(path);
            if (File.Exists(path)
                && string.Equals(Path.GetExtension(path), Archives.SetExtension, StringComparison.OrdinalIgnoreCase))
                return IsBundleArchive(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
        return false;
    }

    private static bool IsBundleFolder(string dir)
    {
        if (File.Exists(SetLayout.ManifestPath(dir))) return false;
        string meta = Path.Combine(dir, SetLayout.MetadataDir);
        if (!Directory.Exists(meta)) return false;
        string? first = Directory.EnumerateFiles(meta).OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        return first is not null && LooksLikeItem(System.IO.File.ReadAllText(first));
    }

    private static bool IsBundleArchive(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.GetEntry(SetLayout.ManifestFile) is not null) return false;
        string prefix = SetLayout.MetadataDir + "/";
        var first = zip.Entries
            .Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal) && e.Name.Length > 0
                && e.FullName.IndexOf('/', prefix.Length) < 0)
            .OrderBy(e => e.FullName, StringComparer.Ordinal).FirstOrDefault();
        if (first is null) return false;
        using var reader = new StreamReader(first.Open(), Encoding.UTF8);
        return LooksLikeItem(reader.ReadToEnd());
    }

    private static bool LooksLikeItem(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                && root.TryGetProperty("image", out var i) && i.ValueKind == JsonValueKind.String
                && root.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Reads a bundle.</summary>
    /// <param name="path">A bundle folder, or a packed <c>.set</c> without <c>set.json</c>.</param>
    /// <returns>The bundle; the caller disposes it.</returns>
    /// <exception cref="CorruptSetException"><paramref name="path"/> is not an upload bundle.</exception>
    public static LoadedBundle Read(string path)
    {
        if (!IsBundle(path))
            throw new CorruptSetException(path,
                $"'{path}' is not an upload bundle: it needs a metadata/ folder of OpenSea items and no set.json.");

        string? temp = null;
        string dir = path;
        if (File.Exists(path))
        {
            temp = Directory.CreateTempSubdirectory("nfty-bundle-").FullName;
            try { ZipFile.ExtractToDirectory(path, temp); }
            catch { try { Directory.Delete(temp, true); } catch { } throw; }
            dir = temp;
        }

        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            var problems = new List<string>();
            var files = Directory.EnumerateFiles(Path.Combine(root, SetLayout.MetadataDir))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();

            var items = new List<(int? Number, BundleItem Item)>();
            foreach (var file in files)
            {
                string leaf = Path.GetFileName(file);
                try
                {
                    var item = ParseItem(root, file, leaf);
                    items.Add((NumberOf(item.Name, leaf), item));
                }
                catch (JsonException ex) { problems.Add($"{SetLayout.MetadataDir}/{leaf}: not readable JSON ({ex.Message})"); }
                catch (InvalidDataException ex) { problems.Add($"{SetLayout.MetadataDir}/{leaf}: {ex.Message}"); }
            }

            // The number is the name's own #n where it has one, else the file's, else the place in the
            // listing; the order is by that number, then by file name, never by completion.
            var numbered = items
                .Select((x, index) => x.Item with { Number = x.Number ?? index + 1 })
                .OrderBy(i => i.Number).ThenBy(i => i.File, StringComparer.Ordinal)
                .ToList();

            BundleCollection? collection = null;
            string collectionPath = Path.Combine(root, MarketplaceFiles.CollectionName);
            if (File.Exists(collectionPath))
            {
                try { collection = ParseCollection(File.ReadAllText(collectionPath)); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException)
                {
                    problems.Add($"{MarketplaceFiles.CollectionName}: {ex.Message}");
                }
            }

            string leafName = Path.GetFileName(root);
            return new LoadedBundle
            {
                Name = collection?.Name is { Length: > 0 } n ? n
                    : File.Exists(path) ? Path.GetFileNameWithoutExtension(path)
                    : leafName.Length > 0 ? leafName : "bundle",
                Items = numbered,
                Collection = collection,
                Problems = problems,
                SourceDirectory = root,
                TempDir = temp,
            };
        }
        catch
        {
            if (temp is not null) try { Directory.Delete(temp, true); } catch { }
            throw;
        }
    }

    private static BundleItem ParseItem(string root, string file, string leaf)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var el = doc.RootElement;
        if (el.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");

        string? Str(string key) =>
            el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        string name = Str("name") ?? throw new InvalidDataException("no \"name\"");
        string image = Str("image") ?? throw new InvalidDataException("no \"image\"");

        var attributes = new List<MetadataAttribute>();
        if (el.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
            foreach (var a in attrs.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("trait_type", out var t)
                    || t.ValueKind != JsonValueKind.String || !a.TryGetProperty("value", out var v)) continue;
                string? value = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
                    _ => null,
                };
                if (value is not null) attributes.Add(new MetadataAttribute(t.GetString()!, value));
            }

        return new BundleItem(0, leaf, name, Str("description") ?? string.Empty, image,
            ResolveImage(root, image), Str("external_url"), Str("animation_url"), attributes);
    }

    private static BundleCollection ParseCollection(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var el = doc.RootElement;
        if (el.ValueKind != JsonValueKind.Object) throw new InvalidDataException("not a JSON object");
        string? Str(string key) =>
            el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? bps = el.TryGetProperty("seller_fee_basis_points", out var f) && f.ValueKind == JsonValueKind.Number
            && f.TryGetInt32(out int b) ? b : null;
        return new BundleCollection(Str("name") ?? string.Empty, Str("description") ?? string.Empty, Str("image"),
            Str("external_link"), bps, Str("fee_recipient"), Str("symbol"));
    }

    /// <summary>The number an item answers to: the <c>#n</c> ending its name, else the number its file
    /// is named (<c>7.json</c>, or <c>7</c>), else null.</summary>
    private static int? NumberOf(string name, string leaf)
    {
        int hash = name.LastIndexOf('#');
        if (hash >= 0 && int.TryParse(name.AsSpan(hash + 1).Trim(), NumberStyles.None,
                CultureInfo.InvariantCulture, out int n)) return n;
        return int.TryParse(Path.GetFileNameWithoutExtension(leaf), NumberStyles.None,
            CultureInfo.InvariantCulture, out int f) ? f : null;
    }

    /// <summary>
    /// Finds the picture an item's <c>image</c> field names, inside the bundle.
    /// </summary>
    /// <param name="root">The bundle folder.</param>
    /// <param name="imageField">The metadata's <c>image</c> value.</param>
    /// <returns>The full path of a file that exists inside <paramref name="root"/>, or null.
    /// Nothing is fetched: a link with a scheme (<c>ipfs://</c>, <c>https://</c>) is mapped by its
    /// trailing <c>images/&lt;file&gt;</c> onto the bundle's own <c>images/</c> folder.</returns>
    public static string? ResolveImage(string root, string imageField)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(imageField)) return null;

        string text = imageField.Trim();
        int cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0) text = text[..cut];

        var candidates = new List<string>();
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            string tail = "/" + Uri.UnescapeDataString(text[(scheme + 3)..]);
            int at = tail.LastIndexOf("/images/", StringComparison.Ordinal);
            if (at >= 0) candidates.Add(tail[(at + 1)..]);
            candidates.Add(SetLayout.ImagesDir + "/" + tail[(tail.LastIndexOf('/') + 1)..]);
        }
        else
        {
            string rel = Uri.UnescapeDataString(text).Replace('\\', '/').TrimStart('/');
            while (rel.StartsWith("./", StringComparison.Ordinal)) rel = rel[2..];
            candidates.Add(rel);
            if (!rel.Contains('/')) candidates.Add(SetLayout.ImagesDir + "/" + rel);
        }

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        foreach (var rel in candidates)
        {
            if (rel.Length == 0) continue;
            string path = Path.GetFullPath(Path.Combine(full, rel.Replace('/', Path.DirectorySeparatorChar)));
            // Never outside the bundle: a link with .. in it must not read the user's other folders.
            if (!path.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
