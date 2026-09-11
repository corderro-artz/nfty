namespace Nfty.App.Services;

/// <summary>
/// The keyboard hints the UI prints, spelled for the platform it is running on.
///
/// <para>The locked mockups draw <c>⌘</c> throughout, because they were authored on a Mac. This app
/// ships on Windows and Linux, where that key does not exist and the real one is Ctrl — so a hint
/// reading <c>⌘N</c> is not a style choice, it is wrong. Every hint now comes from here, which also
/// means the Landing screen and the quick-reference sheet cannot disagree about the same chord: they
/// read the same property.</para>
///
/// <para>The spacing differs too, and deliberately. <c>⌘N</c> is conventionally set closed up;
/// <c>Ctrl N</c> needs the gap or it reads as a word.</para>
/// </summary>
public static class KeyHints
{
    /// <summary>The platform's primary modifier, as a user would recognize it.</summary>
    public static string Mod { get; } = OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";

    /// <summary>The platform's secondary modifier, used for the layer-reorder chord.</summary>
    public static string Alt { get; } = OperatingSystem.IsMacOS() ? "⌥" : "Alt";

    private static readonly string Gap = OperatingSystem.IsMacOS() ? "" : " ";

    /// <summary>Formats one chord on the primary modifier.</summary>
    /// <param name="key">The key pressed with it, e.g. <c>"N"</c>.</param>
    /// <returns>The hint as it should be printed.</returns>
    public static string WithMod(string key) => Mod + Gap + key;

    /// <summary>Open the quick-reference sheet.</summary>
    public static string Help { get; } = WithMod("/");
    /// <summary>Focus the Explorer's search box.</summary>
    public static string Search { get; } = WithMod("K");
    /// <summary>New CookBook.</summary>
    public static string NewCookBook { get; } = WithMod("N");
    /// <summary>Open a CookBook.</summary>
    public static string Open { get; } = WithMod("O");
    /// <summary>Import a loose file.</summary>
    public static string Import { get; } = WithMod("I");
    /// <summary>Close the open document.</summary>
    public static string CloseDocument { get; } = WithMod("W");
    /// <summary>Switch light/dark.</summary>
    public static string Theme { get; } = WithMod("T");
    /// <summary>Reset zoom to 100%.</summary>
    public static string ZoomReset { get; } = WithMod("0");
    /// <summary>Undo.</summary>
    public static string Undo { get; } = WithMod("Z");
    /// <summary>Redo.</summary>
    public static string Redo { get; } = WithMod("Y");
    /// <summary>Hold while drawing: 45° for a line, a square bounding box for a shape. Ctrl does the
    /// same thing and is deliberately not printed — the product teaches the convention, and the
    /// second key is there so reaching for the wrong one still works.</summary>
    public static string Constrain { get; } = OperatingSystem.IsMacOS() ? "⇧" : "Shift";
    /// <summary>Hold while drawing a shape: the press point is its center.</summary>
    public static string FromCenter { get; } = Alt;

    // The canvas tools' tooltips are composed HERE rather than written out in the view, for the same
    // reason every other chord in this app is: the modifier names differ by platform, and a literal
    // "Shift"/"Alt"/"Ctrl+Z" in the markup is a hint that is simply wrong on a Mac. The tool names
    // travel with them only because a tooltip is one string and Avalonia has no way to join a
    // literal to an x:Static in markup.
    private static string Tail(string what) => "  ·  " + what;

    /// <summary>The brush tool's tooltip.</summary>
    public static string BrushTool { get; } = "Brush  (B)" + Tail($"hold {Constrain} for a straight stroke");
    /// <summary>The eraser tool's tooltip.</summary>
    public static string EraserTool { get; } = "Eraser (writes alpha)  (E)" + Tail($"hold {Constrain} for a straight stroke")
        + Tail("the right mouse button erases with any tool");
    /// <summary>The rectangle tool's tooltip.</summary>
    public static string RectangleTool { get; } = "Rectangle  (R)" + Tail($"{Constrain} squares it, {FromCenter} draws from the center");
    /// <summary>The circle tool's tooltip.</summary>
    public static string CircleTool { get; } = "Circle  (C)" + Tail($"{Constrain} rounds it, {FromCenter} draws from the center");
    /// <summary>The triangle tool's tooltip.</summary>
    public static string TriangleTool { get; } = "Triangle  (T)" + Tail($"{Constrain} squares its box, {FromCenter} draws from the center");
    /// <summary>The line tool's tooltip.</summary>
    public static string LineTool { get; } = "Line  (L)" + Tail($"hold {Constrain} to snap to 45°");
    /// <summary>The select tool's tooltip.</summary>
    public static string SelectTool { get; } = "Select region, then drag it to move  (M)"
        + Tail($"{Constrain} squares the marquee and constrains the move")
        + Tail($"{WithMod("A")} marks everything, Delete clears what is marked");
    /// <summary>The fill tool's tooltip.</summary>
    public static string FillTool { get; } = "Fill  (G)";
    /// <summary>The brush-size field's tooltip.</summary>
    public static string BrushSizeField { get; } = "Brush size (px)" + Tail("[ and ] step it");
    /// <summary>The undo button's tooltip.</summary>
    public static string UndoTool { get; } = "Undo" + Tail(Undo);
    /// <summary>The redo button's tooltip.</summary>
    public static string RedoTool { get; } = "Redo" + Tail(Redo);
    /// <summary>Move the selected layer up or down the stack.</summary>
    public static string MoveLayer { get; } = Alt + Gap + "↑↓";
    /// <summary>Drop the editor's selection marquee.</summary>
    public static string DropSelection { get; } = "Esc";
}
