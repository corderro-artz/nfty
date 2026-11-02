using System.Text.RegularExpressions;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>One thing the quick-reference sheet says. The pages and the search results are both drawn
/// from the same <see cref="HelpCatalog"/> list of these, so a search can never find a sentence the
/// pages do not carry, nor miss one they do.</summary>
public abstract record HelpEntry
{
    /// <summary>The tab (page) this entry lives on.</summary>
    public required HelpTab Tab { get; init; }

    /// <summary>The page column, 0 to 3, left to right.</summary>
    public required int Column { get; init; }

    /// <summary>The section label above the entry, already uppercased (Avalonia has no text-transform).</summary>
    public required string Section { get; init; }

    /// <summary>Starts an unlabeled block under a dashed rule (Kitchen: a sixth word, not a link in the chain).</summary>
    public bool Dashed { get; init; }

    /// <summary>The term, key action, color prefix or command.</summary>
    public string Term { get; init; } = "";

    /// <summary>The explanation, with light markup: <c>*word*</c> is emphasis, <c>^word^</c> accent.</summary>
    public string Description { get; init; } = "";

    /// <summary>The description as plain text, markup stripped: what search reads and a result shows.</summary>
    public string Plain => HelpMarkup.Strip(Description);

    /// <summary>The cap text for a key row, else null.</summary>
    public virtual string? Cap => null;

    /// <summary>What a search result shows on the left: the cap, or the term.</summary>
    public virtual string Lead => Term;

    /// <summary>What a search result shows in the middle.</summary>
    public virtual string Body => Plain;

    /// <summary>Whether <see cref="Lead"/> is drawn as a key cap.</summary>
    public bool LeadIsKey => Cap is not null;

    /// <summary>The chip naming where the entry lives: tab · section.</summary>
    public string Where => $"{HelpCatalog.TabLabel(Tab)} · {Section}";

    /// <summary>Everything a search matches against, lower-cased.</summary>
    public string SearchText => string.Join(' ', HelpCatalog.TabLabel(Tab), Section, Term, Plain, Cap ?? "").ToLowerInvariant();
}

/// <summary>A word with an optional glyph in a 20px gutter, an optional file extension, and a sentence.</summary>
public sealed record HelpTerm : HelpEntry
{
    /// <summary>Gutter glyph: <c>icon:Key</c>, <c>mark:Key[:accent|success]</c>, <c>kind:D|S|C</c> or <c>dot</c>.</summary>
    public string? Glyph { get; init; }

    /// <summary>The file extension, drawn small and accented after the term.</summary>
    public string? Ext { get; init; }

    /// <summary>The extension with its leading space, or empty: one run in the template.</summary>
    public string ExtText => Ext is null ? "" : " " + Ext;

    /// <summary>Whether the gutter is drawn.</summary>
    public bool HasGlyph => Glyph is not null;

    /// <summary>A term with no explanation reads as a plain legend line.</summary>
    public bool IsPlain => Description.Length == 0;

    /// <summary>Whether there is a sentence under the term.</summary>
    public bool HasDescription => Description.Length > 0;
}

/// <summary>A key (or mouse gesture) and what it does. <see cref="HelpEntry.Term"/> is the action.</summary>
public sealed record HelpKey : HelpEntry
{
    /// <summary>The cap text: a <see cref="KeyHints"/> string, or a plain key or gesture name.</summary>
    public required string Key { get; init; }

    /// <summary>Width of the cap column, shared by the section so the actions line up.</summary>
    public double KeyWidth { get; set; } = 62;

    /// <inheritdoc />
    public override string? Cap => Key;

    /// <inheritdoc />
    public override string Lead => Key;

    /// <inheritdoc />
    public override string Body => Term;
}

/// <summary>A color-spec prefix and an example: <see cref="HelpEntry.Term"/> the prefix, Description the value.</summary>
public sealed record HelpSpec : HelpEntry;

/// <summary>A line of fine print.</summary>
public sealed record HelpNote : HelpEntry
{
    /// <summary>The nesting line under THE SIX WORDS: mono, tucked under the label.</summary>
    public bool Chain { get; init; }

    /// <summary>Whether this is ordinary fine print (the template shows one of two blocks).</summary>
    public bool IsFine => !Chain;

    /// <inheritdoc />
    public override string Lead => "";
}

/// <summary>A CLI command (Term) and the sentence under it.</summary>
public sealed record HelpCommand : HelpEntry;

/// <summary>Markup helpers for <see cref="HelpEntry.Description"/>.</summary>
public static class HelpMarkup
{
    private static readonly Regex Marks = new(@"[*^]", RegexOptions.Compiled);

    /// <summary>The text without its emphasis marks.</summary>
    public static string Strip(string markup) => Marks.Replace(markup, "");
}

/// <summary>The single list the quick-reference sheet is drawn from. Every key hint comes from
/// <see cref="KeyHints"/> (a modifier spelled by hand is wrong on a Mac); letters and mouse buttons
/// are not modifiers, so they are written out here.</summary>
public static class HelpCatalog
{
    private const string Words = "THE SIX WORDS";
    private const string Kinds = "LAYER KINDS";

    /// <summary>The uppercased tab label, as the tab and the result chips spell it.</summary>
    public static string TabLabel(HelpTab tab) => tab switch
    {
        HelpTab.Basics => "BASICS",
        HelpTab.Editor => "EDITOR",
        HelpTab.Rise => "RISE & 3D",
        _ => "VIEWER & RUNTIME",
    };

    /// <summary>Every entry, in page order.</summary>
    public static IReadOnlyList<HelpEntry> Entries { get; } = Build();

    private static HelpTerm T(HelpTab tab, int col, string sec, string term, string desc = "",
        string? glyph = null, string? ext = null, bool dashed = false) =>
        new() { Tab = tab, Column = col, Section = sec, Term = term, Description = desc, Glyph = glyph, Ext = ext, Dashed = dashed };

    private static HelpKey K(HelpTab tab, int col, string sec, string key, string action) =>
        new() { Tab = tab, Column = col, Section = sec, Key = key, Term = action };

    private static HelpNote N(HelpTab tab, int col, string sec, string text) =>
        new() { Tab = tab, Column = col, Section = sec, Description = text };

    private static HelpSpec S(string prefix, string value) =>
        new() { Tab = HelpTab.Basics, Column = 2, Section = "COLOR", Term = prefix, Description = value };

    private static HelpCommand C(string cmd, string desc) =>
        new() { Tab = HelpTab.Viewer, Column = 3, Section = "COMMANDS", Term = cmd, Description = desc };

    private static IReadOnlyList<HelpEntry> Build()
    {
        const HelpTab B = HelpTab.Basics, E = HelpTab.Editor, R = HelpTab.Rise, V = HelpTab.Viewer;
        var list = new List<HelpEntry>
        {
            // ---------------- BASICS ----------------
            new HelpNote { Tab = B, Column = 0, Section = Words, Chain = true, Description = "CookBook ^›^ Recipe ^›^ Ingredient ^›^ Variant  —  cook it to get a Set" },
            T(B, 0, Words, "CookBook", "An *uncooked* Set — holds the Recipes, the canvas size, the collection.", "icon:IconTypeCookBook", ".cbk"),
            T(B, 0, Words, "Recipe", "One character *type* — an ordered stack of layers, plus its rules.", "icon:IconTypeRecipe", ".rcp"),
            T(B, 0, Words, "Ingredient", "One layer — its kind, its coloring, its weighted variants.", "icon:IconTypeIngredient", ".igt"),
            T(B, 0, Words, "Variant", "One image and a weight, inside an Ingredient.", "icon:IconTypeVariant"),
            T(B, 0, Words, "Set", "What ^Cook^ produces: images, metadata, rarity, seed.", "icon:IconTypeSet", ".set"),

            T(B, 1, Kinds, "dynamic", "Gray value-map. Color *rolled* per asset.", "kind:D"),
            T(B, 1, Kinds, "static", "Gray value-map. One *fixed* color.", "kind:S"),
            T(B, 1, Kinds, "custom", "Full color, composited *as-is*.", "kind:C"),
            T(B, 1, Words, "Kitchen", "Your shelf of *loose parts* — Ingredients and Recipes that belong to no one CookBook. Pull them into any project. Found by *scanning the folder*.",
                "icon:IconKitchen", ".ktn", dashed: true),

            T(B, 2, "RULES & STATE", "never together", glyph: "mark:IconMarkExclude:accent"),
            T(B, 2, "RULES & STATE", "always together", glyph: "mark:IconMarkRequire:success"),
            T(B, 2, "RULES & STATE", "this layer is in a rule", glyph: "mark:IconMarkFlag"),
            T(B, 2, "RULES & STATE", "CookBook is valid", glyph: "dot"),
            T(B, 2, "RULES & STATE", "adding, deleting, reordering locked", glyph: "mark:IconLock"),
            S("hex:", "d6249f"), S("rgb:", "214,36,159"), S("hsl:", "322,72,49"), S("hsv:", "322,83,84"),
            N(B, 2, "COLOR", "The prefix is required — a color is never guessed."),

            K(B, 3, "KEYS · ANYWHERE", KeyHints.Help, "this sheet"),
            K(B, 3, "KEYS · ANYWHERE", KeyHints.CloseDocument, "close document"),
            K(B, 3, "KEYS · ANYWHERE", KeyHints.Theme, "light / dark"),
            K(B, 3, "KEYS · ANYWHERE", KeyHints.ZoomReset, "zoom to 100%"),
            K(B, 3, "KEYS · LANDING", KeyHints.NewCookBook, "new CookBook"),
            K(B, 3, "KEYS · LANDING", KeyHints.Open, "open"),
            K(B, 3, "KEYS · LANDING", KeyHints.Import, "import a loose file"),
            K(B, 3, "KEYS · TREE", KeyHints.Search, "search the tree"),
            K(B, 3, "KEYS · TREE", KeyHints.MoveLayer, "move layer or recipe"),

            // ---------------- EDITOR ----------------
            K(E, 0, "TOOLS", "B", "brush"),
            K(E, 0, "TOOLS", "E", "eraser (writes alpha)"),
            K(E, 0, "TOOLS", "G", "fill"),
            K(E, 0, "TOOLS", "R", "rectangle"),
            K(E, 0, "TOOLS", "C", "circle"),
            K(E, 0, "TOOLS", "T", "triangle"),
            K(E, 0, "TOOLS", "L", "line"),
            K(E, 0, "TOOLS", "M", "select, then drag to move"),

            K(E, 1, "DRAWING", "[  ]", "shrink / grow the brush"),
            K(E, 1, "DRAWING", KeyHints.Constrain, "hold: 45° line, square shape"),
            K(E, 1, "DRAWING", KeyHints.FromCenter, "hold: shape from its center"),
            K(E, 1, "DRAWING", "Right click", "erase with the armed tool"),
            K(E, 1, "DRAWING", KeyHints.DropSelection, "cancel a stroke, else drop the marquee"),

            K(E, 2, "MARKED REGION", KeyHints.MarkAll, "mark everything (arms Select)"),
            K(E, 2, "MARKED REGION", "Delete", "clear it (Backspace too)"),
            K(E, 2, "HISTORY", KeyHints.Undo, "undo"),
            K(E, 2, "HISTORY", KeyHints.Redo, "redo"),
            K(E, 2, "HISTORY", KeyHints.RedoAlt, "redo, too"),

            K(E, 3, "CANVAS VIEW", "Wheel", "zoom about the pointer"),
            K(E, 3, "CANVAS VIEW", "Middle drag", "pan"),
            K(E, 3, "CANVAS VIEW", KeyHints.ZoomKeys, "zoom in, zoom out"),
            K(E, 3, "CANVAS VIEW", KeyHints.ZoomFit, "fit the canvas"),
            K(E, 3, "SET INSPECTOR", "← →", "previous / next asset"),
            K(E, 3, "SET INSPECTOR", KeyHints.ZoomKeys, "zoom; 0 fits"),
            K(E, 3, "SET INSPECTOR", "Double-click", "toggle 1x and 4x"),
            K(E, 3, "SET INSPECTOR", KeyHints.DropSelection, "close the inspector"),

            // ---------------- RISE & 3D ----------------
            T(R, 0, "WHAT RISE DOES", "Pixel art to a 3D model", "Each solid pixel gets a *depth*: how far it stands out."),
            T(R, 0, "WHAT RISE DOES", "Front and back", "Measured in voxels, one pixel wide. Zero on both is flat paper."),
            T(R, 0, "WHAT RISE DOES", "2D only", "Partial alpha cannot rise. Fix it with *Make voxel-ready*."),
            T(R, 0, "WHAT RISE DOES", "Export", "GLB, *glTF + .bin*, or glTF with the data embedded."),

            K(R, 1, "PAINT DEPTH", "Left", "raise the front"),
            K(R, 1, "PAINT DEPTH", "Right", "raise the back"),
            K(R, 1, "PAINT DEPTH", KeyHints.LowerDepth, "lower instead"),
            K(R, 1, "PAINT DEPTH", KeyHints.RiseConstrain, "straighten a stroke"),
            N(R, 1, "PAINT DEPTH", "On a Recipe roll the first click *picks* the layer that owns the pixel: left the front owner, right the back owner. It paints nothing."),

            K(R, 2, "TOOLS", "B", "brush"),
            K(R, 2, "TOOLS", "L", "line"),
            K(R, 2, "TOOLS", "R", "rectangle"),
            K(R, 2, "TOOLS", "G", "same-color fill"),
            K(R, 2, "TOOLS", "M", "select; click inside to raise"),
            K(R, 2, "PAGE", KeyHints.StepTarget, "step target layer"),
            K(R, 2, "PAGE", KeyHints.ZoomKeys, "zoom"),
            K(R, 2, "PAGE", KeyHints.ZoomFit, "fit"),
            K(R, 2, "PAGE", KeyHints.DropSelection, "cancel stroke, or drop marquee, then target"),

            K(R, 3, "3D VIEW", "Drag", "orbit"),
            K(R, 3, "3D VIEW", "R / M drag", "pan"),
            K(R, 3, "3D VIEW", "Wheel", "zoom at pointer"),
            K(R, 3, "3D VIEW", KeyHints.Arrows, "orbit"),
            K(R, 3, "3D VIEW", KeyHints.ZoomKeys, "zoom"),
            K(R, 3, "3D VIEW", KeyHints.ResetView, "reset, or double-click"),

            // ---------------- VIEWER & RUNTIME ----------------
            T(V, 0, "THE VIEWER", "A window of its own", "2D art and the 3D model side by side, without leaving your page."),
            T(V, 0, "THE VIEWER", "Live or snapshot", "*Live* follows the editor or Rise page; a *snapshot* is a copy that stays."),
            T(V, 0, "THE VIEWER", "In the window", $"Wheel zooms the 2D art; left or middle drag pans. *{KeyHints.CloseDocument}* closes the window."),
            T(V, 0, "THE VIEWER", "Sealed .tin", "View-only: *no save, copy or export*."),

            T(V, 1, "OPEN IT", "Pop out", "The Pop out buttons beside previews and the 3D tile."),
            T(V, 1, "OPEN IT", "Tile, row, canvas or 3D tile", $"*{KeyHints.ViewerClick}*, or right-click *Open in viewer*."),
            T(V, 1, "OPEN IT", "From a file", "*Open in viewer…* on the opening screen."),

            T(V, 2, "RUNTIME", "Menu", "A CookBook *flattened* for runtime: weights, rules, colors and art. No .cbk needed.", ext: ".mnu"),
            T(V, 2, "RUNTIME", "Ticket", "One rolled outcome in a few hundred bytes. It names the *selection*, not the pixels."),
            T(V, 2, "RUNTIME", "Nfty.Ticket", "No dependencies. A server or game engine rolls with it alone.", ext: ".dll"),

            C("nfty menu", "Writes a .mnu from a .cbk."),
            C("nfty serve --seed", "Rolls and renders one asset."),
            C("nfty serve --ticket", "Renders an asset already rolled."),
            N(V, 3, "COMMANDS", "Manual, Reference: Ticket API."),
        };

        // Each COLUMN's cap column is as wide as its widest cap, so the actions line up down the whole
        // column - across its sections too - without a short cap leaving a wide hole beside it.
        foreach (var g in list.OfType<HelpKey>().GroupBy(k => (k.Tab, k.Column)))
        {
            var w = Math.Max(30, g.Max(k => k.Key.Length) * 6.4 + 18);
            foreach (var k in g) k.KeyWidth = Math.Ceiling(w);
        }
        return list;
    }

    /// <summary>The entries matching <paramref name="query"/>: every whitespace-separated word must
    /// appear (case-insensitive) in the entry's tab, section, term, description or key.</summary>
    public static IReadOnlyList<HelpEntry> Search(string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        return Entries.Where(e => words.All(w => e.SearchText.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
