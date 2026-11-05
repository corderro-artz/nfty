using Nfty.Core.Imaging;

namespace Nfty.Core.Publish;

/// <summary>Whether an export lands as a folder or as one file.</summary>
public enum ExportShape
{
    /// <summary>A plain folder, the same shape <c>generate --out</c> writes.</summary>
    Folder,

    /// <summary>One archive — a <c>.set</c>, or a <c>.tin</c> when sealed.</summary>
    Archive,
}

/// <summary>
/// A named starting point on <see cref="ExportOptions"/>, one per reason people actually hand a
/// collection to somebody.
/// </summary>
/// <remarks>
/// A preset is a starting point, never a mode: <see cref="ExportOptions.For"/> returns ordinary
/// options the caller is free to change afterward. The four exist because the axes are only
/// meaningful together — "include the nfty metadata" is not a question anyone can answer, and
/// "is this going to a marketplace or to a collaborator" is.
/// </remarks>
public enum ExportPreset
{
    /// <summary>Art, models, standard fields and <c>collection.json</c>, named by token id. No per-asset DNA, colors or seed.</summary>
    Marketplace,

    /// <summary>Everything a buyer gets, rarity and DNA included, but not the book.</summary>
    AssetPack,

    /// <summary>The Set and the CookBook that produced it, openly.</summary>
    FullProject,

    /// <summary>Sealed: encrypted, and marked view-only.</summary>
    SealedCritique,
}

/// <summary>
/// Exactly what leaves the machine.
/// </summary>
/// <remarks>
/// <para><b>Four independent axes, not a mode switch</b> — the art, each of the two metadata sets,
/// and the source book — because the reasons people share a collection do not line up into a
/// ladder. A marketplace wants less than a buyer, who wants less than a collaborator, but a sealed
/// review copy is not a point on that line at all.</para>
///
/// <para><b>Every member is <c>init</c>-only with a default</b>, so a new axis is additive at every
/// existing call site — the same rule the manifests follow for schema evolution.</para>
///
/// <para><b>The passphrase is deliberately NOT here.</b> It is a parameter of the operation, not a
/// property of the description: this record is the thing a front-end holds in a view model, binds
/// to checkboxes, and would quite reasonably log or serialize while debugging. A secret that lives
/// in it is a secret waiting to be written to disk by accident.</para>
/// </remarks>
public record ExportOptions
{
    /// <summary>Include <c>images/</c> — the generated art.</summary>
    public bool Images { get; init; } = true;

    /// <summary>Include <c>metadata/</c> — the standards-pure ERC-721 / OpenSea fields.</summary>
    public bool OpenSeaMetadata { get; init; } = true;

    /// <summary>
    /// Include <c>nfty/</c> — per-asset DNA, the seed, and the color rolled on every layer.
    /// </summary>
    /// <remarks>
    /// The axis with a consequence worth stating: this is the file that records HOW each asset came
    /// out the way it did. It is what a buyer wants and what a public listing has no use for.
    /// </remarks>
    public bool NftyMetadata { get; init; } = true;

    /// <summary>
    /// Include <c>models/</c> — the 3D model written for each asset at cook time.
    /// </summary>
    /// <remarks>
    /// <para><b>On by default, and on for <see cref="ExportPreset.Marketplace"/> too</b> — a
    /// marketplace shows a glb <c>animation_url</c> as a 3D viewer, so the models are part of the
    /// listing, not an extra. A Set cooked without models has no <c>models/</c>, so the flag then
    /// changes nothing and the plan never mentions the part.</para>
    ///
    /// <para><b>The models are copied as cooked and never re-rendered</b>, so the debug mark
    /// (<see cref="NumberWatermark"/>), which stamps only the art on the way out, cannot reach one:
    /// a model's texture is the unstamped asset.</para>
    /// </remarks>
    public bool Models { get; init; } = true;

    /// <summary>
    /// Ship the source <c>.cbk</c> alongside the Set.
    /// </summary>
    /// <remarks>
    /// A flag, with the PATH supplied to the operation — for the same reason the passphrase is. A
    /// Set records only its book's hash, never the book, so the caller is the one that knows which
    /// file is meant; but a preset has to be able to say "include it" without knowing where it
    /// lives, or <see cref="ExportPreset.FullProject"/> could not be a preset at all.
    /// </remarks>
    public bool IncludeCookBook { get; init; }

    /// <summary>Folder or single file.</summary>
    public ExportShape Shape { get; init; } = ExportShape.Archive;

    /// <summary>
    /// Encrypt the export and mark it view-only.
    /// </summary>
    /// <remarks>
    /// Orthogonal to the content axes on purpose — any of the four presets can be sealed, and a
    /// sealed export still has to decide what is inside it.
    /// </remarks>
    public bool Sealed { get; init; }

    /// <summary>What the recipient should read first. Travels in the clear on a sealed export.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// Also stitch every asset into one <c>spritesheet.png</c>.
    /// </summary>
    /// <remarks>
    /// <b>A fifth content axis, not a fifth shape.</b> It travels with the export rather than
    /// replacing it — a sheet is what an engine loads and the numbered PNGs are what a marketplace
    /// mints, and a collection handed to a game developer usually wants both. So it is a checkbox
    /// beside the other four and not a preset, and it is the one entry in an export that does not
    /// exist on disk until the export makes it.
    /// </remarks>
    public bool SpriteSheet { get; init; }

    /// <summary>Cells across the sheet, or null to fall to the squarest grid that fits.</summary>
    /// <remarks>
    /// <b>Null is a real answer and not a zero.</b> Most people want "just lay it out", and asking
    /// two numbers of somebody who has no opinion is how a checkbox becomes a form. The default is
    /// <c>SpriteSheet.Fit</c>, and either number may be given alone — the other follows from the
    /// count.
    /// </remarks>
    public int? SpriteSheetColumns { get; init; }

    /// <summary>Cells down the sheet, or null to derive it from the columns and the count.</summary>
    public int? SpriteSheetRows { get; init; }

    /// <summary>
    /// Stamp each asset's set number into a corner of its art.
    /// </summary>
    /// <remarks>
    /// <para><b>A debug mark, and it says so.</b> Loose sprites lose their filenames the moment they
    /// are dragged into an engine, a sprite editor or a chat window, and a folder of five hundred
    /// near-identical characters is unsortable without one. The number stamped is the asset's own
    /// set number, so a stamped sprite cross-references <c>nfty/NNNN.json</c> with nothing else
    /// having to be recorded.</para>
    ///
    /// <para><b>It is destructive, so it only ever happens on the way OUT.</b> The export renders a
    /// stamped copy and ships that; the author's Set is never touched. A stamped export is for
    /// working with, not for minting - which is why it is a box you tick rather than part of any
    /// preset.</para>
    /// </remarks>
    public bool NumberWatermark { get; init; }

    /// <summary>Which corner the stamp sits in.</summary>
    public StampCorner WatermarkCorner { get; init; } = StampCorner.BottomRight;

    /// <summary>
    /// Include <c>set.json</c> — the Set's own record: seed, source-book hash, rarity table.
    /// </summary>
    /// <remarks>
    /// <para><b>On by default, off for <see cref="ExportPreset.Marketplace"/></b>: a public drop has no
    /// use for the seed and the book's hash, and a seed that should stay private until the reveal is
    /// otherwise published with the art. Without it nfty cannot reopen the export as a full Set;
    /// a suitable art/metadata export can still reopen as a read-only <see cref="MarketplaceBundle"/>.
    /// A sealed export requires the manifest.</para>
    /// </remarks>
    public bool IncludeSetManifest { get; init; } = true;

    /// <summary>
    /// Where the uploaded <c>images/</c> folder lives. Each <c>image</c> link becomes this followed by
    /// the image's FILE NAME: <c>ipfs://CID/</c> gives <c>ipfs://CID/1.png</c>.
    /// </summary>
    /// <remarks>
    /// <para>By file name, not by path inside the export: uploading the <c>images/</c> folder to IPFS
    /// (Pinata, NFT.Storage, <c>ipfs add -r</c>) gives a CID whose root IS that folder's contents, so
    /// the right link has no <c>images/</c> in it. It is also the only order that works: the metadata
    /// carries this address, so the art has to be uploaded (and addressed) first.</para>
    /// <para>Export-only; the cooked Set keeps its relative paths. Must be absolute, use the scheme
    /// <c>ipfs</c>, <c>https</c>, <c>http</c> or <c>ar</c>, and end with <c>/</c>
    /// (<see cref="MarketplaceRules.BaseUriProblem"/>). Never guessed or repaired. Null leaves the
    /// relative path and the plan says so.</para>
    /// </remarks>
    public string? ImageBaseUri { get; init; }

    /// <summary>
    /// Where the uploaded <c>models/</c> folder lives. Each <c>animation_url</c> becomes this followed
    /// by the model's file name: <c>ipfs://CID/</c> gives <c>ipfs://CID/1.glb</c>.
    /// </summary>
    /// <remarks>
    /// Same rules and reason as <see cref="ImageBaseUri"/>. An <c>animation_url</c> is written only
    /// when this is set, the models are exported and their format is glb.
    /// </remarks>
    public string? ModelBaseUri { get; init; }

    /// <summary>How the exported files are named — the Set's padded names, or bare token ids.</summary>
    /// <remarks>
    /// Applies to metadata, image and model file names, the <c>name</c> inside the metadata and every
    /// URI. The rich <c>nfty/</c> files keep the Set's own numbering and names: they are nfty's record
    /// of the Set, cross-referenced by set number, and are not what a marketplace reads.
    /// </remarks>
    public TokenNaming TokenNaming { get; init; } = TokenNaming.Padded;

    /// <summary>The token id of the Set's first asset: 1 (the default) or 0, as the contract counts.</summary>
    /// <remarks>Token id = set number - 1 + this.</remarks>
    public int TokenStart { get; init; } = 1;

    /// <summary>When set, each item's <c>external_url</c> is this followed by its token id.</summary>
    public string? ExternalUrlBase { get; init; }

    /// <summary>
    /// Write <c>collection.json</c>, the contract-level metadata (OpenSea's <c>contractURI</c> shape).
    /// </summary>
    /// <remarks>
    /// <b>An axis of its own.</b> It is not an item file and no Set contains it; the export makes it.
    /// On for <see cref="ExportPreset.Marketplace"/>, off elsewhere. The name and symbol come from the
    /// Set, the description from its metadata.
    /// </remarks>
    public bool IncludeCollectionMetadata { get; init; }

    /// <summary><c>collection.json</c>'s <c>external_link</c>: the collection's website.</summary>
    public string? ExternalLink { get; init; }

    /// <summary>Creator royalty, as a percentage from 0 to 100 with at most 2 decimals; null for none.</summary>
    /// <remarks>Written as <c>seller_fee_basis_points</c> (2.5% is 250).</remarks>
    public double? RoyaltyPercent { get; init; }

    /// <summary>Who the royalty is paid to: <c>0x</c> and 40 hexadecimal digits.</summary>
    public string? FeeRecipient { get; init; }

    /// <summary>
    /// <c>collection.json</c>'s <c>image</c>. Null falls to the first token's image address when a
    /// <see cref="ImageBaseUri"/> is set and the art is exported, and is otherwise left out.
    /// </summary>
    public string? CollectionImage { get; init; }

    /// <summary>The options a preset starts from.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a defined preset.</exception>
    /// <param name="preset">Which one.</param>
    /// <returns>Ordinary options; change any of them afterward.</returns>
    public static ExportOptions For(ExportPreset preset) => preset switch
    {
        ExportPreset.Marketplace => new ExportOptions
        {
            NftyMetadata = false,
            IncludeCollectionMetadata = true,
            IncludeSetManifest = false,
            // A contract's tokenURI is baseURI + tokenId, so the files are named by token id (and
            // the metadata has no extension) or nothing the contract asks for resolves.
            TokenNaming = TokenNaming.TokenId,
        },
        ExportPreset.AssetPack => new ExportOptions(),
        ExportPreset.FullProject =>
            new ExportOptions { IncludeCookBook = true, Shape = ExportShape.Folder },
        ExportPreset.SealedCritique => new ExportOptions { Sealed = true },
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    /// <summary>The preset's name, as a person reads it.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a defined preset.</exception>
    /// <param name="preset">Which one.</param>
    /// <returns>A short title.</returns>
    public static string TitleOf(ExportPreset preset) => preset switch
    {
        ExportPreset.Marketplace => "Marketplace",
        ExportPreset.AssetPack => "Asset pack",
        ExportPreset.FullProject => "Full project",
        ExportPreset.SealedCritique => "Sealed critique",
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    /// <summary>
    /// One sentence saying who the preset is for. Stated in Core so the CLI's help and the GUI's
    /// tiles cannot describe the same choice differently.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset"/> is not a defined preset.</exception>
    /// <param name="preset">Which one.</param>
    /// <returns>The sentence.</returns>
    public static string SummaryOf(ExportPreset preset) => preset switch
    {
        // Precise rather than sweeping. An early draft said "nothing that says how it was made",
        // which set.json makes false the moment anyone looks: the seed and the source book's hash
        // are in every export, because they are part of what makes the result a Set rather than a
        // folder of pictures. What this preset actually drops is the PER-ASSET record - the DNA and
        // the color rolled on each layer of each asset - and a seed is inert without the book.
        // Kept to about a dozen words each. These are read in a four-across row of tiles roughly
        // 140px wide, and a longer sentence there costs the screen a line of height everywhere -
        // which is what pushed the passphrase field below the fold when sealing was armed.
        ExportPreset.Marketplace =>
            "Art and metadata for listing. No seed, DNA or colors.",
        ExportPreset.AssetPack =>
            "What a buyer gets: rarity and DNA, but not the book.",
        ExportPreset.FullProject =>
            "The Set and the book that made it. They can re-cut it.",
        ExportPreset.SealedCritique =>
            "Encrypted, view-only. They can look, not take.",
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    /// <summary>
    /// Which preset these options ARE, or null when they are a combination of the author's own.
    /// </summary>
    /// <remarks>
    /// <para>Answered by comparing content rather than by remembering which button was pressed, so a
    /// screen cannot go on claiming a preset after a box beneath it was unticked. The note is
    /// ignored: it is a message to a person, not part of the export's shape. So are the marketplace
    /// VALUES (base URI, naming, links, royalty): they fill in what a content axis writes, they do
    /// not choose what leaves. <see cref="ExportPreset.Marketplace"/> STARTS at token-id naming, but
    /// a different naming does not un-name it.</para>
    ///
    /// <para><b><see cref="Shape"/> is ignored too, and that is the correction.</b> A preset names
    /// WHAT LEAVES the machine; folder-or-file is how it is packed, and the two are separate
    /// questions — which is why the shape controls sit in their own column rather than among the
    /// content boxes. Comparing it meant every preset came un-named the moment the shape was
    /// changed: ticking all four content boxes and choosing Single file left Full project lit until
    /// the shape was touched and then lit nothing at all, and Marketplace did the same in the other
    /// direction, unnoticed because its default shape is the common one. The presets still START at
    /// a shape apiece (<see cref="ExportPreset.FullProject"/> at a folder, the rest at an archive),
    /// which is what a starting point is for.</para>
    ///
    /// <para>They stay distinguishable without it: the four differ in content or in the seal, never
    /// in shape alone.</para>
    /// </remarks>
    /// <returns>The preset these options equal, or null.</returns>
    public ExportPreset? MatchingPreset()
    {
        foreach (ExportPreset p in Enum.GetValues<ExportPreset>())
            if ((For(p) with
                {
                    Note = Note,
                    Shape = Shape,
                    ImageBaseUri = ImageBaseUri,
                    ModelBaseUri = ModelBaseUri,
                    TokenNaming = TokenNaming,
                    TokenStart = TokenStart,
                    ExternalUrlBase = ExternalUrlBase,
                    ExternalLink = ExternalLink,
                    RoyaltyPercent = RoyaltyPercent,
                    FeeRecipient = FeeRecipient,
                    CollectionImage = CollectionImage,
                }) == this) return p;
        return null;
    }
}
