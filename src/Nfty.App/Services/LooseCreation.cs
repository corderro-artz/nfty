using System;
using System.IO;
using System.Threading.Tasks;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.Services;

/// <summary>
/// Creates a new loose Recipe or Ingredient from a finished wizard: where it goes, the write, and
/// every refusal along the way. The one place both Landing and the Explorer do it.
/// </summary>
/// <remarks>
/// <para>The two screens used to carry their own copies, and the copies drifted: only the Explorer
/// defaulted into an open Kitchen, guarded the picker against throwing and refused an empty id; only
/// Landing opened what it had just written. A button that behaves differently depending on which
/// screen it sits on reads as a bug in whichever screen is wrong, so there is one flow now and its
/// behavior is the union of the good halves.</para>
///
/// <para>This writes and reports; it does not open. Opening needs the navigation stack, the session
/// and a Recent list, which belong to the screen, so the caller opens whatever path comes back.</para>
/// </remarks>
public sealed class LooseCreation
{
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _picker;
    private readonly IKitchenSession? _kitchen;

    /// <summary>Builds the service over the services both screens already hold.</summary>
    /// <param name="dialogs">Where refusals are shown, as error cards.</param>
    /// <param name="picker">Asked for a path when no Kitchen is open.</param>
    /// <param name="kitchen">The open workspace, if any; loose parts default into it.</param>
    public LooseCreation(IDialogService dialogs, IFilePickerService picker, IKitchenSession? kitchen)
    {
        _dialogs = dialogs; _picker = picker; _kitchen = kitchen;
    }

    /// <summary>Writes the wizard's recipe as an empty loose <c>.rcp</c>.</summary>
    /// <param name="result">The finished wizard.</param>
    /// <returns>The path written, or null when nothing was (canceled, or refused with an error card).</returns>
    /// <remarks>A fresh recipe is empty by design, so it is not held to the generatable standard;
    /// <see cref="LooseWorkspace.WriteRecipe"/> says so.</remarks>
    public async Task<string?> NewRecipeAsync(NewRecipeViewModel result)
    {
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            await _dialogs.ShowErrorAsync("Invalid recipe", "The recipe needs a name.");
            return null;
        }
        var path = await ChoosePathAsync(result.DerivedId + Archives.RecipeExtension,
            "Save new recipe", Archives.RecipeExtension);
        if (path is null) return null;

        var recipe = LoadedRecipe.Empty(result.DerivedId, result.Name);
        try
        {
            var problems = LooseWorkspace.WriteRecipe(path, recipe);
            if (problems.Count > 0)
            {
                await _dialogs.ShowErrorAsync("Invalid recipe", Problems.Join(problems));
                return null;
            }
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Could not save", ex.Message); return null; }
        return path;
    }

    /// <summary>Writes the wizard's ingredient as a loose <c>.igt</c> with one blank variant.</summary>
    /// <param name="result">The finished wizard.</param>
    /// <returns>The path written, or null when nothing was (canceled, or refused with an error card).</returns>
    public async Task<string?> NewIngredientAsync(NewIngredientViewModel result)
    {
        if (string.IsNullOrWhiteSpace(result.DerivedId))
        {
            await _dialogs.ShowErrorAsync("Invalid ingredient", "The ingredient needs a name.");
            return null;
        }
        if (!result.TryGetCanvas(out var canvas))
        {
            await _dialogs.ShowErrorAsync("Invalid canvas", "Enter a canvas size like 512x512.");
            return null;
        }
        var path = await ChoosePathAsync(result.DerivedId + Archives.IngredientExtension,
            "Save new ingredient", Archives.IngredientExtension);
        if (path is null) return null;

        LoadedIngredient built;
        try { built = result.Build(canvas); }   // Build allocates the raster - guard it (OOM on a huge canvas)
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Could not save", ex.Message); return null; }

        try
        {
            var problems = LooseWorkspace.WriteIngredient(path, built);
            if (problems.Count > 0)
            {
                await _dialogs.ShowErrorAsync("Invalid ingredient", Problems.Join(problems));
                return null;
            }
        }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Could not save", ex.Message); return null; }
        finally { built.Dispose(); }
        return path;
    }

    /// <summary>The Kitchen IS the loose-items folder, so with one open the save goes into it
    /// without asking. Otherwise (or when a file of that name is already there - replacing something
    /// in the workspace is a deletion, not a save) the picker decides.</summary>
    private async Task<string?> ChoosePathAsync(string fileName, string title, string extension)
    {
        var dir = _kitchen?.Current?.Directory;
        if (dir is not null)
        {
            var candidate = Path.Combine(dir, fileName);
            if (!File.Exists(candidate)) return candidate;
        }
        try { return await _picker.SaveFileAsync(title, extension); }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Could not save", ex.Message); return null; }
    }
}
