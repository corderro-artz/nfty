using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>One layer the recipe carries, with its variants, as the pickers offer it.</summary>
/// <param name="Id">The layer id a rule stores.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Variants">Its variants, in manifest order.</param>
public record RuleLayerOption(string Id, string Name, IReadOnlyList<RuleVariantOption> Variants)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>One variant, as the pickers offer it.</summary>
/// <param name="Id">The variant id a rule stores.</param>
/// <param name="Name">Its display name.</param>
public record RuleVariantOption(string Id, string Name)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>One target row in the dialog: a layer and one of its variants, both being chosen.</summary>
public partial class RuleTargetDraft : ObservableObject
{
    private readonly Action _changed;

    /// <summary>The layers this recipe carries.</summary>
    public IReadOnlyList<RuleLayerOption> Layers { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variants))]
    private RuleLayerOption? _layer;

    [ObservableProperty] private RuleVariantOption? _variant;

    /// <summary>The chosen layer's variants — empty until a layer is picked, which is what keeps the
    /// second picker from offering variants that belong to a different layer.</summary>
    public IReadOnlyList<RuleVariantOption> Variants => Layer?.Variants ?? Array.Empty<RuleVariantOption>();

    partial void OnLayerChanged(RuleLayerOption? value)
    {
        // The variant belonged to the OLD layer. Keeping it would let a target name a pair that
        // does not exist — the exact shape Validator has to report and the dialog exists to prevent.
        Variant = value?.Variants.FirstOrDefault();
        _changed();
    }

    partial void OnVariantChanged(RuleVariantOption? value) => _changed();

    /// <summary>Whether this row names a real pair yet.</summary>
    public bool IsComplete => Layer is not null && Variant is not null;

    /// <summary>The pair, or null while the row is half-filled.</summary>
    public RuleTarget? ToTarget() =>
        IsComplete ? new RuleTarget(Layer!.Id, Variant!.Id) : null;

    /// <summary>Creates a target row.</summary>
    /// <param name="layers">The layers to offer.</param>
    /// <param name="changed">Raised whenever the row's choice changes, so the dialog can re-check
    /// whether the whole rule is valid yet.</param>
    public RuleTargetDraft(IReadOnlyList<RuleLayerOption> layers, Action changed)
    {
        Layers = layers;
        _changed = changed;
    }
}

/// <summary>
/// Add or edit one incompatibility rule, on one screen.
///
/// <para><b>Every id is picked, never typed.</b> That is the point of the dialog rather than a
/// convenience: an unknown layer or variant id is a thing <c>Validator</c> can only report after the
/// fact, and every rule that ever reached a book by hand-editing JSON could carry one. Offering only
/// what the recipe holds makes that whole class of rule unwritable here.</para>
///
/// <para>It also refuses live rather than on submit: <see cref="Problem"/> runs the same
/// <see cref="RuleEdits"/> checks the CLI does, so the reason a rule cannot be saved is on screen
/// while you are still looking at the control that caused it.</para>
/// </summary>
public partial class RuleDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly RecipeManifest _recipe;
    private readonly int _editingIndex;

    /// <summary>The layers this recipe carries, offered to every picker.</summary>
    public IReadOnlyList<RuleLayerOption> Layers { get; }

    /// <summary>The dialog's title — it says which of the two things this is.</summary>
    public string Title => _editingIndex >= 0 ? "Edit rule" : "Add rule";

    /// <summary>The confirm button's label, naming the action rather than saying OK.</summary>
    public string ConfirmLabel => _editingIndex >= 0 ? "Save rule" : "Add rule";

    /// <summary>True for "never together", false for "always together".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RelationText))]
    private bool _isExclude = true;

    /// <summary>The relationship in the same words the panel prints, so the dialog and the row it
    /// produces cannot describe the same rule differently.</summary>
    public string RelationText => IsExclude
        ? "Never together — if the trigger is rolled, none of these may be."
        : "Always together — if the trigger is rolled, every one of these must be too.";

    /// <summary>The trigger the rule applies to.</summary>
    public RuleTargetDraft Trigger { get; }

    /// <summary>What the trigger forbids or requires. At least one, and a rule with several is a
    /// conjunction — every one of them.</summary>
    public ObservableCollection<RuleTargetDraft> Targets { get; } = new();

    /// <summary>Why the rule cannot be saved yet, or empty when it can.</summary>
    [ObservableProperty] private string _problem = string.Empty;

    /// <summary>Whether <see cref="Problem"/> has anything to show.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>Whether more than one target is present, which is what makes removing one sensible.</summary>
    public bool CanRemoveTarget => Targets.Count > 1;

    partial void OnProblemChanged(string value)
    {
        OnPropertyChanged(nameof(HasProblem));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsExcludeChanged(bool value) => Revalidate();

    /// <summary>Creates the dialog.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="recipe">The recipe being edited — the source of every option offered, and of the
    /// duplicate check.</param>
    /// <param name="ingredients">Its ingredients, for the variant lists.</param>
    /// <param name="editingIndex">The rule being edited, or -1 to add a new one.</param>
    public RuleDialogViewModel(IDialogService dialogs, RecipeManifest recipe,
        IReadOnlyList<LoadedIngredient> ingredients, int editingIndex = -1)
    {
        _dialogs = dialogs;
        _recipe = recipe;
        _editingIndex = editingIndex;

        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var i in ingredients) byId[i.Manifest.Id] = i;

        // In layerOrder, resolved tolerantly — the same projection the detail pane uses, so the two
        // offer the layers in the same order rather than one in paint order and one in load order.
        Layers = recipe.LayerOrder
            .Select(id => byId.GetValueOrDefault(id))
            .Where(i => i is not null)
            .Select(i => new RuleLayerOption(i!.Manifest.Id, i.Manifest.Name,
                i.Manifest.Variants.Select(v => new RuleVariantOption(v.Id, v.Name)).ToList()))
            .ToList();

        Trigger = new RuleTargetDraft(Layers, Revalidate);
        Targets.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanRemoveTarget));
            Revalidate();
        };

        if (editingIndex >= 0 && editingIndex < recipe.Rules.Count)
        {
            var rule = recipe.Rules[editingIndex];
            IsExclude = rule.Type == RuleType.Exclude;
            Seat(Trigger, rule.When);
            foreach (var t in rule.Targets) Targets.Add(NewTarget(t));
        }
        else
        {
            Trigger.Layer = Layers.FirstOrDefault();
            Targets.Add(NewTarget(null));
        }

        Revalidate();
    }

    private RuleTargetDraft NewTarget(RuleTarget? seed)
    {
        var draft = new RuleTargetDraft(Layers, Revalidate);
        if (seed is not null) { Seat(draft, seed); return draft; }

        // A NEW row opens on the first pair that is actually legal, rather than on the first layer
        // in the list. Two ways it could otherwise open already invalid, and the frame caught the
        // second: a rule cannot constrain a layer against itself, so the trigger's layer is out;
        // and a target already listed is a duplicate, so "+ Target" defaulting to the first layer
        // produced aura:none twice and a refusal the user had not done anything to earn.
        var taken = Targets.Select(t => t.ToTarget()).Where(t => t is not null).ToHashSet();
        foreach (var layer in Layers.Where(l => l.Id != Trigger.Layer?.Id))
            foreach (var v in layer.Variants)
                if (!taken.Contains(new RuleTarget(layer.Id, v.Id)))
                {
                    draft.Layer = layer;
                    draft.Variant = v;
                    return draft;
                }

        // Every legal pair is already listed. The row opens empty and says so through Problem —
        // there is nothing better to seat it with, and refusing to add the row would leave the
        // button doing nothing with no explanation.
        draft.Layer = Layers.FirstOrDefault(l => l.Id != Trigger.Layer?.Id) ?? Layers.FirstOrDefault();
        return draft;
    }

    private static void Seat(RuleTargetDraft draft, RuleTarget target)
    {
        draft.Layer = draft.Layers.FirstOrDefault(l => l.Id == target.IngredientId);
        draft.Variant = draft.Layer?.Variants.FirstOrDefault(v => v.Id == target.VariantId);
    }

    /// <summary>The rule as it currently stands, or null while any picker is empty.</summary>
    public IncompatibilityRule? Build()
    {
        if (!Trigger.IsComplete) return null;
        var targets = Targets.Select(t => t.ToTarget()).ToList();
        if (targets.Any(t => t is null)) return null;

        return new IncompatibilityRule(
            IsExclude ? RuleType.Exclude : RuleType.Require,
            Trigger.ToTarget()!,
            targets.Select(t => t!).ToList());
    }

    /// <summary>
    /// Re-runs every check the CLI runs, on every keystroke's worth of change. Both halves are here
    /// for the same reason they are both in the CLI: <see cref="RuleEdits.Validate"/> catches a rule
    /// that cannot mean anything on its own, and the duplicate check catches one that is only wrong
    /// against the rules this recipe already carries.
    /// </summary>
    private void Revalidate()
    {
        var rule = Build();
        if (rule is null) { Problem = "Pick a layer and a variant on every row."; return; }

        try { RuleEdits.Validate(rule); }
        catch (ArgumentException ex) { Problem = ex.Message; return; }

        for (int i = 0; i < _recipe.Rules.Count; i++)
            if (i != _editingIndex && RuleEdits.AreSame(_recipe.Rules[i], rule))
            {
                Problem = $"This recipe already carries that rule, at position {i + 1}.";
                return;
            }

        Problem = string.Empty;
    }

    /// <summary>Picks the relationship. Two commands rather than a bound toggle, to match the
    /// Dynamic/Static tray the ingredient editor uses — a binary choice looks the same everywhere in
    /// the app, and a tray button reads as the thing it selects rather than as a state.</summary>
    /// <param name="relation">"exclude" or anything else for require.</param>
    [RelayCommand]
    private void SetRelation(string relation) =>
        IsExclude = string.Equals(relation, "exclude", StringComparison.Ordinal);

    [RelayCommand]
    private void AddTarget() => Targets.Add(NewTarget(null));

    [RelayCommand]
    private void RemoveTarget(RuleTargetDraft target)
    {
        // Never below one. A rule with no targets can never fire, and RuleEdits refuses it — so the
        // control that would create one is simply not offered.
        if (Targets.Count > 1) Targets.Remove(target);
    }

    private bool CanConfirm() => !HasProblem;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => _dialogs.Close(Build());

    [RelayCommand]
    private void Cancel() => _dialogs.Close(null);
}
