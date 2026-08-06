using System.Text;
using Nfty.Core.Formats;

namespace Nfty.Core.Stats;

/// <summary>
/// The <c>inspect</c> listing for a Kitchen: what the workspace folder actually holds, grouped by
/// kind.
///
/// <para>Lives here beside <see cref="IdentityReport"/> and <see cref="CollectionReport"/> for the
/// same reason they do — a front-end that re-derived "something similar" would drift from this one
/// the first time either changed. The GUI has no Kitchen listing yet; when it grows one it shows
/// these bytes rather than its own approximation.</para>
///
/// <para>Paths are printed as bare file names, not as the absolute paths <see cref="KitchenContents"/>
/// carries. The folder is named once at the top, so repeating it on every row would bury the only
/// part that differs, and a listing full of machine-specific absolute paths cannot be pasted into an
/// issue.</para>
/// </summary>
public static class KitchenReport
{
    /// <summary>Renders the listing.</summary>
    /// <param name="kitchen">The opened Kitchen.</param>
    /// <returns>The report text. An empty workspace says so rather than printing three empty
    /// headings — a fresh Kitchen is a normal state, not a broken one.</returns>
    public static string Render(KitchenContents kitchen)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Kitchen: {kitchen.Manifest.Name} [{kitchen.Manifest.Id}]");
        sb.AppendLine($"  Folder: {kitchen.Directory}");

        if (kitchen.IsEmpty)
        {
            sb.AppendLine("  (empty — nothing has been saved into this workspace yet)");
            return sb.ToString();
        }

        Section(sb, "CookBooks", kitchen.CookBooks);
        Section(sb, "Recipes", kitchen.Recipes);
        Section(sb, "Ingredients", kitchen.Ingredients);
        return sb.ToString();
    }

    /// <summary>One kind's heading and rows. A kind with nothing in it is omitted entirely rather
    /// than printed as an empty heading: the listing answers "what is in here", and a heading with
    /// no rows under it reads as a failed scan.</summary>
    private static void Section(StringBuilder sb, string heading, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        sb.AppendLine($"  {heading} ({paths.Count}):");
        foreach (var p in paths) sb.AppendLine($"    {Path.GetFileName(p)}");
    }
}
