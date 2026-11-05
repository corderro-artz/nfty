namespace Nfty.Core.Output;

/// <summary>One OpenSea trait. The property name is <c>Trait_type</c> because the published JSON
/// key is <c>trait_type</c>, which the shared serializer options map to.</summary>
/// <param name="Trait_type">The trait category.</param>
/// <param name="Value">Its value for this asset.</param>
public record MetadataAttribute(string Trait_type, string Value);

/// <summary>One trait with how common it is across the whole collection.</summary>
/// <param name="Trait_type">The trait category.</param>
/// <param name="Value">Its value for this asset.</param>
/// <param name="RarityPct">Percentage of the collection carrying it.</param>
public record RarityAttribute(string Trait_type, string Value, double RarityPct);

/// <summary>
/// Standard ERC-721 / OpenSea per-item metadata. Written to <c>metadata/NNNN.json</c>.
/// Deliberately kept standards-pure — only the fields marketplaces expect, no nfty extras.
/// </summary>
/// <param name="Name">The item's published name, <c>"{collection} #{n}"</c>.</param>
/// <param name="Description">The collection's description.</param>
/// <param name="Image">Where the item's image lives, as the metadata records it.</param>
/// <param name="Attributes">The item's traits.</param>
public record OpenSeaMetadata(
    string Name,
    string Description,
    string Image,
    IReadOnlyList<MetadataAttribute> Attributes);

/// <summary>One layer's resolved color for the rich nfty metadata (all kinds represented).</summary>
/// <param name="Layer">The ingredient id.</param>
/// <param name="Kind">The layer kind, lowercase.</param>
/// <param name="Model">The color model, when the layer is colorized.</param>
/// <param name="H">Hue, ROUNDED to 0.1 degree for reading.</param>
/// <param name="S">Saturation, ROUNDED to 0.001 for reading.</param>
/// <param name="ExactH">The unrounded rolled hue. Additive and optional (absent in Sets cooked before it
/// existed, so no schema bump); what a 3D model of the Set recolors from, so it matches the cook's own.</param>
/// <param name="ExactS">The unrounded rolled saturation; see <paramref name="ExactH"/>.</param>
public record LayerColor(string Layer, string Kind, string? Model, double? H, double? S,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? ExactH = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] double? ExactS = null);

/// <summary>
/// Rich nfty-specific per-item metadata. Written to <c>nfty/NNNN.json</c> alongside the
/// OpenSea file so the OpenSea file stays standards-pure. Carries the generator's extras.
/// </summary>
/// <param name="SetNumber">The asset's number within the Set.</param>
/// <param name="Recipe">The id of the recipe it was rolled from.</param>
/// <param name="Dna">Its identity hash.</param>
/// <param name="Seed">The seed of the run that produced it.</param>
/// <param name="Rarity">The share of the collection carrying each of its traits.</param>
/// <param name="Layers">What each layer contributed, including the color it rolled.</param>
/// <param name="AbsentLayers">The display names of optional layers the roll left out; null when none were.</param>
public record NftyMetadata(
    int SetNumber,
    string Recipe,
    string Dna,
    string Seed,
    IReadOnlyList<RarityAttribute> Rarity,
    IReadOnlyList<LayerColor> Layers,
    IReadOnlyList<string>? AbsentLayers = null);

/// <summary>How many assets one recipe produced.</summary>
/// <param name="Recipe">The recipe's <b>id</b> — what <c>--recipe</c> takes, and what the
/// <c>distribution</c> array in <c>set.json</c> has always held. This said "display name" for a
/// long time and no reader was in a position to notice: nothing rendered the field until
/// <see cref="Nfty.Core.Stats.SetReport"/> did, and the writer takes it from
/// <c>GeneratedAsset.RecipeId</c>. The doc is what was wrong, not the data — the array is sorted by
/// this value with an ordinal comparer to keep <c>set.json</c> byte-identical across machines, and
/// Sets carrying ids are out in the world.</param>
/// <param name="Count">How many assets it produced.</param>
/// <param name="Percent">Its share of the collection.</param>
public record RecipeCount(string Recipe, int Count, double Percent);

/// <summary>
/// The Set's own record, written to <c>set.json</c>: its name, size, seed, the hash of the book that
/// produced it, the collection-wide rarity table and the per-recipe distribution. Reports such as
/// <see cref="Nfty.Core.Stats.SetReport"/> read it alone, so inspecting a large Set costs the same
/// as inspecting a small one.
/// </summary>
/// <param name="Name">The collection's name.</param>
/// <param name="Count">How many assets the Set holds.</param>
/// <param name="Seed">The seed that produced it; reproduction also needs the same cookbook and
/// compatible generation options, including <see cref="UniqueDna"/>.</param>
/// <param name="CookbookSha256">SHA-256 of the source <c>.cbk</c> — what ties a Set back to the exact
/// archive that produced it. Null when the cookbook never came from a file (an in-memory book).
///
/// <para>Documented here rather than on the parameter itself: a <c>&lt;summary&gt;</c> inside a
/// positional record's parameter list is not a valid documentation location, so the compiler
/// discarded it with CS1587 and no tooling ever saw it — on the one field CLAUDE.md insists stays
/// threaded through.</para></param>
/// <param name="GeneratorVersion">The engine version stamp.</param>
/// <param name="Distribution">Per-recipe counts and shares across the Set.</param>
/// <param name="Rarity">The collection-wide rarity table.</param>
/// <param name="UniqueDna">Whether every asset was required to have distinct DNA.
///
/// <para>Recorded because the seed does NOT reproduce a run on its own. The same cookbook and the
/// same seed generate a different collection depending on this switch — with uniqueness on, a
/// colliding roll is discarded and re-rolled, which consumes RNG draws that the unlimited run
/// never spends, so the two diverge from the first collision onward. Without this field a Set
/// carried two thirds of its own recipe.</para>
///
/// <para><b>Null means "not recorded"</b>, not "false": Sets written before this field existed
/// cannot say, and claiming they were unique would be inventing a fact. Additive and optional, so
/// <c>Schema.Current</c> is deliberately NOT bumped — older builds ignore unknown properties,
/// whereas a bump would make them reject the file outright.</para></param>
/// <param name="Models">The 3D models the cook made, or null (and absent from the file) when it made
/// none. Additive and optional, no schema bump.</param>
/// <param name="Symbol">The collection's ticker-style symbol, so an export can write
/// <c>collection.json</c> without the source book. Additive and optional, no schema bump; null in a
/// Set cooked before it existed.</param>
/// <param name="MetadataShape">Which shape of per-item metadata the Set carries (<see cref="MetadataTraits"/>):
/// absent means 1, the shape before color traits and explicit <c>None</c>; new cooks write 2.
/// <c>extend</c> keeps writing the recorded shape. Additive and optional, no schema bump.</param>
/// <param name="Files">How the Set's per-asset files are NAMED when an export renamed them (token ids,
/// a different start id), or marks an export whose metadata links were rewritten; null (and absent from
/// the file) means the writer's own padded names from 1. Written only by an export, and read by every
/// reader of per-asset files through <see cref="SetFiles"/>, so an export reopens as a Set whatever it
/// was named. Additive and optional, no schema bump.</param>
public record SetManifest(
    string Name,
    int Count,
    string Seed,
    string? CookbookSha256,
    string GeneratorVersion,
    IReadOnlyList<RecipeCount> Distribution,
    IReadOnlyList<RarityAttribute> Rarity,
    bool? UniqueDna = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SetModels? Models = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Symbol = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? MetadataShape = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SetFiles? Files = null)
{
    /// <summary>The naming this Set's per-asset files follow: <see cref="Files"/>, or the writer's own.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public SetFiles FileNaming => Files ?? SetFiles.Default;
}

/// <summary>
/// ONE mapping between a set number and the name its files carry: <c>images/</c>, <c>metadata/</c>
/// and <c>models/</c>. The writer's own names (<c>0001.png</c>) are the default; an export may rename
/// them to token ids and shift the first id, and records that in <c>set.json</c> so every reader
/// finds the same files the exporter wrote.
/// </summary>
/// <remarks>
/// <b>Every reader of a per-asset file asks here</b> - <c>SetReader</c>, the models lookup, the
/// viewer, the exporter itself - because a second copy of "number to file name" is how a renamed
/// export stops opening. <c>nfty/</c> is the exception by design: it keeps the Set's own names, as
/// nfty's cross-referenced record, and carries the set number inside.
/// </remarks>
/// <param name="Naming">The spelling token: <c>padded</c> (<c>0001</c>) or <c>token-id</c> (<c>1</c>).</param>
/// <param name="Start">The token id of the first asset: 1, or 0.</param>
public sealed record SetFiles(string Naming = SetFiles.PaddedWord, int Start = 1)
{
    /// <summary>The token for the writer's own zero-padded names.</summary>
    public const string PaddedWord = "padded";

    /// <summary>The token for bare token-id names.</summary>
    public const string TokenIdWord = "token-id";

    /// <summary>The writer's own names, starting at 1: what a Set with no record follows.</summary>
    public static SetFiles Default { get; } = new();

    /// <summary>Whether this is exactly the writer's own naming.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefault => IsPadded && Start == 1;

    /// <summary>Whether names are zero-padded.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPadded => string.Equals(Naming, PaddedWord, StringComparison.Ordinal);

    /// <summary>Whether this build knows the spelling; an unknown one is refused rather than guessed.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsKnown => IsPadded || string.Equals(Naming, TokenIdWord, StringComparison.Ordinal);

    /// <summary>The token id of the asset with this set number.</summary>
    /// <param name="number">The set number, from 1.</param>
    /// <returns>The id.</returns>
    public int Id(int number) => number - 1 + Start;

    /// <summary>The name an asset's files share, without extension.</summary>
    /// <param name="number">The set number.</param>
    /// <returns>For example <c>0001</c> or <c>0</c>.</returns>
    public string Stem(int number) => IsPadded
        ? SetLayout.Stem(Id(number))
        : Id(number).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The asset's picture, relative to <c>images/</c>.</summary>
    /// <param name="number">The set number.</param>
    /// <returns>The file name.</returns>
    public string ImageFile(int number) => Stem(number) + ".png";

    /// <summary>The asset's metadata file, relative to <c>metadata/</c>: the bare token id when token-id
    /// named, so a contract's <c>baseURI + tokenId</c> resolves it.</summary>
    /// <param name="number">The set number.</param>
    /// <returns>The file name.</returns>
    public string MetadataFile(int number) => IsPadded ? Stem(number) + ".json" : Stem(number);

    /// <summary>The set number a per-asset file's name carries, or null for a name the writer would not have given.</summary>
    /// <param name="file">A file name or path; the extension is ignored.</param>
    /// <returns>The set number, from 1, or null.</returns>
    public int? NumberOf(string file)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        if (!int.TryParse(stem, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int id)) return null;
        int n = id - Start + 1;
        return n >= 1 && stem == Stem(n) ? n : null;
    }
}

/// <summary>
/// <c>set.json</c>'s record of the 3D models a cook made. Additive and optional, so
/// <c>Schema.Current</c> is not bumped; absent means the Set was cooked without models.
/// </summary>
/// <param name="Format">The model format token, as the CLI spells it (<c>glb</c>, ...).</param>
/// <param name="Pivot">The pivot token, as the CLI spells it (<c>bottom-center</c>, ...).</param>
/// <param name="VoxelSize">World units per voxel.</param>
/// <param name="Skipped">Set numbers with no model because nothing in them is fully opaque,
/// ascending. Null when none.</param>
public sealed record SetModels(
    string Format,
    string Pivot,
    float VoxelSize,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<int>? Skipped = null);
