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
    /// <summary>Move the selected layer up or down the stack.</summary>
    public static string MoveLayer { get; } = Alt + Gap + "↑↓";
    /// <summary>Drop the editor's selection marquee.</summary>
    public static string DropSelection { get; } = "Esc";
}
