using CommunityToolkit.Mvvm.Input;
using Nfty.App.Rise;
using Nfty.Core.Editing;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

/// <summary>
/// A recipe page that WRITES THROUGH: depth belongs to the layers' own variants, so the page first asks which
/// layer a stroke is for (the pick), then paints only that one while the 3D tile goes on showing the whole
/// character.
/// </summary>
public sealed partial class RiseViewModel
{
    private RecipeRiseSource? Recipe => Source as RecipeRiseSource;

    /// <summary>The source is a recipe that can be painted: its depth is written to the layers' variants.</summary>
    public bool IsWriteThrough => Recipe is { IsEditable: true };

    /// <summary>A write-through page with no layer chosen yet: the next press picks.</summary>
    public bool IsPicking => IsWriteThrough && Recipe!.Target is null;

    /// <summary>The header over the canvas is laid out (a write-through page); its text says which state it is in.</summary>
    public bool ShowTargetHeader => IsWriteThrough;

    /// <summary>The header: what to do, or which layer is being edited and how far the edit reaches. Empty off a
    /// write-through page.</summary>
    public string TargetHeader
    {
        get
        {
            if (Recipe is not { IsEditable: true } recipe) return "";
            if (recipe.Target is not { } target) return "click a pixel to choose its layer";
            return $"editing {target.Name} · {Reach(recipe, target)?.Text}";
        }
    }

    /// <summary>The header's tooltip: what the reach figure is.</summary>
    public string TargetHeaderTip => Recipe is { IsEditable: true, Target: not null } recipe && Reach(recipe, recipe.Target) is { } r
        ? r.Tip
        : "Left-click a pixel to pick the layer that owns its front, right-click for the layer that owns its back.";

    // RarityCalculator walks the whole book; the figure only changes with the target, so it is kept per target.
    private (string Ing, string Variant, VariantReach Reach)? _reach;

    private VariantReach? Reach(RecipeRiseSource recipe, RiseLayerTarget target)
    {
        if (_reach is { } r && r.Ing == target.IngredientId && r.Variant == target.VariantId) return r.Reach;
        if (recipe.TargetReach is not { } fresh) return null;
        _reach = (target.IngredientId, target.VariantId, fresh);
        return fresh;
    }

    /// <summary>Whether a press at this pixel may start a stroke. Off a write-through page every pixel is
    /// hittable; while picking, any pixel of the stack's union; once a layer is chosen, only pixels that layer
    /// is solid at (everything else is dimmed and inert).</summary>
    /// <param name="x">Canvas x.</param>
    /// <param name="y">Canvas y.</param>
    public bool IsHittable(int x, int y)
    {
        if (Recipe is not { IsEditable: true } recipe) return true;
        return recipe.Target is null ? Subject.Depth.IsSolid(x, y) : recipe.IsTargetSolid(x, y);
    }

    /// <summary>Chooses the layer that owns the pixel's front (left) or back (right). Paints nothing.</summary>
    /// <param name="x">Canvas x.</param>
    /// <param name="y">Canvas y.</param>
    /// <param name="side">Which face's owner.</param>
    /// <returns>False on a clear pixel, off a write-through page, or while a save runs.</returns>
    public bool PickAt(int x, int y, DepthSide side)
    {
        if (Recipe is not { IsEditable: true } recipe) return false;
        bool picked = recipe.TryPick(x, y, side);
        if (picked) SayTarget();
        return picked;
    }

    /// <summary>Makes a layer of the roll the one being painted.</summary>
    /// <param name="row">One of <see cref="LayerRows"/>.</param>
    [RelayCommand]
    private void TargetLayer(RiseLayerRow? row)
    {
        if (row is null || !row.IsPickable || Recipe is not { } recipe) return;
        if (recipe.SetTarget(row.IngredientId)) SayTarget();
    }

    /// <summary>Moves the target through the layers present in the roll.</summary>
    /// <param name="delta">+1 toward the top of the stack, -1 toward the bottom.</param>
    [RelayCommand]
    private void StepTarget(int delta)
    {
        if (Recipe is { } recipe && recipe.StepTarget(delta)) SayTarget();
    }

    /// <summary>Escape: drops the marquee if there is one, else the target.</summary>
    [RelayCommand]
    private void Escape()
    {
        if (Selection is not null) { Selection = null; return; }
        Recipe?.SetTarget(null);
    }

    // The toolstrip prints one of TWO mouse maps, both laid out always, one inked (reserve space, toggle ink):
    // while a recipe is choosing a layer the buttons PICK, so the painting legend would mislead, and swapping
    // the text itself changed the strip's width and so where it wraps.

    /// <summary>Ink of the painting legend.</summary>
    public double PaintLegendOpacity => IsPicking ? 0 : 1;

    /// <summary>Ink of the picking legend.</summary>
    public double PickLegendOpacity => IsPicking ? 1 : 0;

    private void SayTarget()
    {
        if (Recipe?.Target is { } t) _status.Say($"Painting {t.Name}.");
    }

    /// <summary>The target, the dim art and everything read from them moved.</summary>
    private void NotifyTargetBound()
    {
        OnPropertyChanged(nameof(IsWriteThrough));
        OnPropertyChanged(nameof(IsPicking));
        OnPropertyChanged(nameof(PaintLegendOpacity));
        OnPropertyChanged(nameof(PickLegendOpacity));
        OnPropertyChanged(nameof(ShowTargetHeader));
        OnPropertyChanged(nameof(TargetHeader));
        OnPropertyChanged(nameof(TargetHeaderTip));
        OnPropertyChanged(nameof(ShowSave));
    }
}
