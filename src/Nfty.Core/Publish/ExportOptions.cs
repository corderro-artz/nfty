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
    /// <summary>Art and the standard fields. No per-asset DNA or layer colors.</summary>
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

    /// <summary>The options a preset starts from.</summary>
    /// <param name="preset">Which one.</param>
    /// <returns>Ordinary options; change any of them afterward.</returns>
    public static ExportOptions For(ExportPreset preset) => preset switch
    {
        ExportPreset.Marketplace => new ExportOptions { NftyMetadata = false },
        ExportPreset.AssetPack => new ExportOptions(),
        ExportPreset.FullProject =>
            new ExportOptions { IncludeCookBook = true, Shape = ExportShape.Folder },
        ExportPreset.SealedCritique => new ExportOptions { Sealed = true },
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    /// <summary>The preset's name, as a person reads it.</summary>
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
            "Art and the standard fields. No per-asset DNA or colors.",
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
    /// Answered by comparing content rather than by remembering which button was pressed, so a
    /// screen cannot go on claiming a preset after a box beneath it was unticked. The note is
    /// ignored: it is a message to a person, not part of the export's shape.
    /// </remarks>
    /// <returns>The preset these options equal, or null.</returns>
    public ExportPreset? MatchingPreset()
    {
        foreach (ExportPreset p in Enum.GetValues<ExportPreset>())
            if ((For(p) with { Note = Note }) == this) return p;
        return null;
    }
}
