using System.Text.Json.Serialization;

namespace Nfty.App.Models;

/// <summary>One row in the Landing screen's Recent list.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Meta">The subtitle line, e.g. "cookbook · 2 recipes".</param>
/// <param name="Path">Where it lives on disk.</param>
/// <param name="Loose">Whether it is a loose Recipe or Ingredient rather than a CookBook.</param>
public record RecentItem(string Name, string Meta, string Path, bool Loose)
{
    // Which of the four kinds this row points at, read from the extension.
    //
    // The row used to pick its glyph from `Loose` alone, which is a boolean over four kinds: a
    // cooked Set and a loose Recipe both got the wrong icon, because "not loose" meant CookBook and
    // "loose" meant Ingredient. The extension already carries the answer.
    //
    // Deliberately tolerant, unlike Archives.KindOf, which refuses an unknown extension rather than
    // guessing: that is right when the answer decides how a file is PARSED, and wrong here, where a
    // stale entry pointing at something unrecognised should draw a neutral icon rather than take the
    // start screen down with it.
    private string Extension => System.IO.Path.GetExtension(Path ?? "").ToLowerInvariant();

    /// <summary>Whether this row is a CookBook.</summary>
    [JsonIgnore] public bool IsCookBook => Extension == ".cbk";
    /// <summary>Whether this row is a loose Recipe.</summary>
    [JsonIgnore] public bool IsRecipe => Extension == ".rcp";
    /// <summary>Whether this row is a loose Ingredient.</summary>
    [JsonIgnore] public bool IsIngredient => Extension == ".igt";
    /// <summary>Whether this row is a cooked Set.</summary>
    [JsonIgnore] public bool IsSet => Extension == ".set";
    /// <summary>Whether the extension is none of the four — a moved or hand-edited entry.</summary>
    [JsonIgnore] public bool IsUnknownKind => !IsCookBook && !IsRecipe && !IsIngredient && !IsSet;

    /// <summary>The path as the row prints it: the containing folder and the file name.</summary>
    /// <remarks>
    /// The row used to print the whole path, trimmed with an ellipsis at the END — which is the end
    /// that holds the file name. Two rows in one folder therefore rendered as the same string
    /// (both HD rows printed "M:\nfty-demo-hd\VaporPet…"), so the column meant to tell them apart was the one thing that
    /// could not. The folder is kept because it is what distinguishes two books of the same name;
    /// the full path is still on the row's tooltip.
    /// </remarks>
    [JsonIgnore]
    public string DisplayPath
    {
        get
        {
            var file = System.IO.Path.GetFileName(Path ?? "");
            if (file.Length == 0) return Path ?? "";
            var folder = System.IO.Path.GetFileName(
                System.IO.Path.GetDirectoryName(Path) ?? "");
            return folder.Length == 0 ? file : folder + System.IO.Path.DirectorySeparatorChar + file;
        }
    }
}
