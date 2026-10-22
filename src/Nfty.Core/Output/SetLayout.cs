namespace Nfty.Core.Output;

/// <summary>
/// The parts a cooked Set is made of, named once.
/// </summary>
/// <remarks>
/// <para><b>This exists because four places had their own copy of these four strings</b> — the
/// writer's <c>Prepare</c>, the writer's <c>ReadExisting</c>, <see cref="SetReader"/>, and
/// <see cref="SetReader.IsSetFolder"/> — and a fifth was about to be added by
/// <c>SetWriter.Pack</c>, which is the one that has to be exactly right. A Set folder is a folder
/// the user chose, so the only thing separating "the Set" from "everything else the user keeps
/// there" is this list.</para>
///
/// <para><b>It is an INCLUDE list, and that direction is the whole point.</b> <c>Pack</c> used to
/// zip every file under the output folder and merely exclude a top-level <c>.set</c>, which is
/// unbounded in exactly the wrong direction: cooking into a Kitchen — a folder whose entire purpose
/// is to hold loose <c>.cbk</c>, <c>.rcp</c> and <c>.igt</c> parts — packed the author's complete
/// source into the archive they were about to hand a buyer. No exclusion list can enumerate what a
/// person might leave in a folder; a description of what a Set IS can. It is the same rule
/// <c>Archives.KindOf</c> follows in refusing an unknown extension rather than guessing at it.</para>
///
/// <para>The three directories are taken WHOLE rather than filtered by extension. They are nfty's
/// own namespace inside the user's folder, so the boundary that matters is the directory, not the
/// file type — and filtering on <c>.png</c> would silently drop an asset the day the format grows a
/// second image encoding.</para>
/// </remarks>
public static class SetLayout
{
    /// <summary>The Set manifest, which is also what identifies a folder as a Set at all.</summary>
    public const string ManifestFile = "set.json";

    /// <summary>The generated art, one <c>NNNN.png</c> per asset.</summary>
    public const string ImagesDir = "images";

    /// <summary>The standards-pure OpenSea metadata, one <c>NNNN.json</c> per asset.</summary>
    public const string MetadataDir = "metadata";

    /// <summary>The rich nfty metadata — dna, seed, rarity, per-layer color — one per asset.</summary>
    public const string NftyDir = "nfty";

    /// <summary>The 3D models a modeled cook writes, one per fully-opaque asset. Absent from a Set
    /// cooked without models. NOT taken whole: see <see cref="ModelFilesIn"/>.</summary>
    public const string ModelsDir = "models";

    /// <summary>The subdirectories taken whole, in the order a listing reads best. <c>models/</c> is
    /// deliberately NOT here: it belongs to a Set only as far as <c>set.json</c> says
    /// (<see cref="ModelFilesIn"/>), because a folder named <c>models</c> is a name an art workspace
    /// plausibly already uses.</summary>
    public static IReadOnlyList<string> Directories { get; } =
        [ImagesDir, MetadataDir, NftyDir];

    /// <summary>
    /// The model files <c>set.json</c> in <paramref name="setDir"/> accounts for: for a Set that
    /// recorded a <c>models</c> block, <c>NNNN.glb</c> (or <c>.gltf</c> and <c>.bin</c> per the
    /// recorded format) for each numbered asset that was not recorded as skipped. Nothing else in
    /// <c>models/</c>, and nothing at all for a Set without the block.
    /// </summary>
    /// <param name="setDir">The Set folder.</param>
    /// <returns>Absolute paths, in <see cref="StringComparer.Ordinal"/> order.</returns>
    /// <remarks>This is the one rule for "which of the folder's <c>models/</c> is the Set's": the
    /// listing, <c>Pack</c>, the exporter and the export dialog all ask here, so a user's own
    /// <c>models/secret-source.blend</c> - or a stray <c>0001.glb</c> beside a Set cooked without
    /// models - cannot ride along into a buyer's archive.</remarks>
    public static IReadOnlyList<string> ModelFilesIn(string setDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setDir);
        string dir = Path.Combine(setDir, ModelsDir);
        var recorded = ReadManifest(setDir);
        if (recorded?.Models is not { } models || !Directory.Exists(dir)) return [];

        var extensions = ExtensionsFor(models.Format);
        var skipped = models.Skipped is null ? new HashSet<int>() : new HashSet<int>(models.Skipped);
        var files = new List<string>();
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
        {
            if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int number)) continue;
            if (number < 1 || number > recorded.Count || skipped.Contains(number)) continue;
            files.Add(file);
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>How many assets have a model in <paramref name="setDir"/> - a <c>.gltf</c> with its
    /// <c>.bin</c> is one model, not two.</summary>
    /// <param name="setDir">The Set folder.</param>
    /// <returns>The count of distinct numbered assets among <see cref="ModelFilesIn"/>.</returns>
    public static int ModelCountIn(string setDir) =>
        ModelFilesIn(setDir).Select(f => Path.GetFileNameWithoutExtension(f)).Distinct(StringComparer.Ordinal).Count();

    private static string[] ExtensionsFor(string format) => format switch
    {
        "glb" => [".glb"],
        "gltf" => [".gltf", ".bin"],
        "gltf-embedded" => [".gltf"],
        // A token this build does not know: stay inside nfty's own shapes rather than guess wider.
        _ => [".glb", ".gltf", ".bin"],
    };

    private static SetManifest? ReadManifest(string setDir)
    {
        string path = ManifestPath(setDir);
        if (!File.Exists(path)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<SetManifest>(File.ReadAllText(path), Formats.Json.Options); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Where a Set folder keeps its manifest.</summary>
    /// <param name="setDir">The Set folder.</param>
    /// <returns>The full path to <c>set.json</c>.</returns>
    public static string ManifestPath(string setDir) => Path.Combine(setDir, ManifestFile);

    /// <summary>
    /// Every file that belongs to the Set in <paramref name="setDir"/>, and nothing else in that
    /// folder.
    /// </summary>
    /// <param name="setDir">The Set folder.</param>
    /// <returns>
    /// Absolute paths, in <see cref="StringComparer.Ordinal"/> order. Ordinal because these reach an
    /// output file: the default comparer sorts by the current culture, so the same Set would pack to
    /// a different archive on an <c>en-US</c> machine than on a <c>sv-SE</c> one — the rule every
    /// other sort that reaches disk here already follows.
    /// </returns>
    public static IReadOnlyList<string> FilesIn(string setDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setDir);
        var files = new List<string>();

        string manifest = ManifestPath(setDir);
        if (File.Exists(manifest)) files.Add(manifest);

        foreach (var name in Directories)
        {
            string sub = Path.Combine(setDir, name);
            if (Directory.Exists(sub))
                files.AddRange(Directory.EnumerateFiles(sub, "*", SearchOption.AllDirectories));
        }

        files.AddRange(ModelFilesIn(setDir));

        files.Sort(StringComparer.Ordinal);
        return files;
    }
}
