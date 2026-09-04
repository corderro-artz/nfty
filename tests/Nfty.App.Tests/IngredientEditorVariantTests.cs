using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorVariantTests
{
    private static IngredientEditorViewModel Editor(out CookBookSession session, out string path,
        IDialogService? dialogs = null)
    {
        (path, session, var recipe, var ing) = IngredientEditorSaveTests.OnDisk();
        return new IngredientEditorViewModel(ing, recipe, session.Current!, new ImageBridge(),
            new FakeNav(), session, dialogs ?? new FakeDialogs(), new FilePickerService());
    }

    [AvaloniaFact]
    public void AddVariant_appends_a_selected_blank_variant_and_dirties()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            var before = vm.Variants.Count;
            vm.AddVariantCommand.Execute(null);
            Assert.Equal(before + 1, vm.Variants.Count);
            Assert.Same(vm.Variants[^1], vm.SelectedVariant);
            Assert.True(vm.IsDirty);
            Assert.Equal(vm.Variants.Count, vm.Variants.Select(v => v.Id).Distinct().Count());  // unique ids
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void DuplicateVariant_copies_the_selected_painted_pixels()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 180;
            vm.ApplyToolStroke(new[] { (0, 0) });                   // paint the current variant
            vm.DuplicateVariantCommand.Execute(null);
            Assert.Same(vm.Variants[^1], vm.SelectedVariant);       // copy selected
            Assert.Equal(180, vm.ValueAt(4, 4));                    // copy carries the painted value
            Assert.NotEqual(vm.Variants[0].Id, vm.Variants[^1].Id); // distinct id
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void DeleteVariant_can_execute_tracks_the_count()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            Assert.False(vm.DeleteVariantCommand.CanExecute(null));  // one variant → disabled
            vm.AddVariantCommand.Execute(null);
            Assert.True(vm.DeleteVariantCommand.CanExecute(null));   // two → enabled
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Delete_removes_from_all_three_structures_when_confirmed()
    {
        var vm = Editor(out var session, out var path, new ConfirmingDialogs(true));
        try
        {
            vm.AddVariantCommand.Execute(null);
            var id = vm.SelectedVariant!.Id;
            await vm.DeleteVariantCommand.ExecuteAsync(null);
            Assert.DoesNotContain(vm.Variants, v => v.Id == id);
            Assert.NotNull(vm.SelectedVariant);                     // neighbor selected
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Delete_is_canceled_when_the_confirm_is_declined()
    {
        var vm = Editor(out var session, out var path, new ConfirmingDialogs(false));
        try
        {
            vm.AddVariantCommand.Execute(null);
            var count = vm.Variants.Count;
            await vm.DeleteVariantCommand.ExecuteAsync(null);
            Assert.Equal(count, vm.Variants.Count);                 // declined → nothing removed
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Added_variant_persists_through_save()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.AddVariantCommand.Execute(null);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 90;
            vm.ApplyToolStroke(new[] { (0, 0) });
            await vm.SaveCommand.ExecuteAsync(null);
            using var reread = CookBookArchive.Read(path);
            var rip = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(2, rip.Manifest.Variants.Count);           // original + added
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Rename_writes_through_to_draft_and_filmstrip_and_rejects_empty()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.SelectedName = "Glowy";
            Assert.Equal("Glowy", vm.SelectedVariant!.Name);        // filmstrip updated
            Assert.True(vm.IsDirty);
            vm.SelectedName = "   ";                                // invalid → rejected
            Assert.Equal("Glowy", vm.SelectedVariant!.Name);       // unchanged
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Reweight_writes_through_and_rejects_non_positive()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.SelectedWeight = 3.5;
            Assert.Equal(3.5, vm.SelectedVariant!.Weight);
            Assert.True(vm.IsDirty);
            vm.SelectedWeight = 0;                                  // invalid → rejected
            Assert.Equal(3.5, vm.SelectedVariant!.Weight);
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Selecting_a_variant_syncs_the_name_and_weight_fields_without_dirtying()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            vm.AddVariantCommand.Execute(null);        // adds + selects "Variant 2" (dirties)
            vm.IsDirty = false;                         // reset to observe selection sync
            vm.SelectVariantCommand.Execute(vm.Variants[0]);
            Assert.Equal(vm.Variants[0].Name, vm.SelectedName);
            Assert.Equal(vm.Variants[0].Weight, vm.SelectedWeight);
            Assert.False(vm.IsDirty);                   // pure selection sync must not dirty
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Preview_toggles_are_independent_and_do_not_touch_the_draft()
    {
        var vm = Editor(out var session, out var path);
        try
        {
            var canvas = vm.Canvas; var preview = vm.Preview;

            Assert.Equal(120, vm.PreviewHeight);          // inset by default
            Assert.True(vm.ShowPaintCanvas);

            vm.EnlargePreviewCommand.Execute(null);
            Assert.Equal(208, vm.PreviewHeight);          // enlarged in place, short of the 320 canvas
            Assert.True(vm.ShowPaintCanvas);              // independent of fill-pane
            vm.EnlargePreviewCommand.Execute(null);
            Assert.Equal(120, vm.PreviewHeight);          // toggles back

            vm.FillPanePreviewCommand.Execute(null);
            Assert.True(vm.PreviewFillsPane);
            Assert.False(vm.ShowPaintCanvas);             // preview took over the pane
            Assert.Equal(120, vm.PreviewHeight);          // independent of enlarge
            vm.FillPanePreviewCommand.Execute(null);
            Assert.True(vm.ShowPaintCanvas);              // toggles back

            // The tile is what carries the three buttons, so it stays on screen in both states and
            // simply names the half it is showing. Hiding it took fill-pane's own off switch with it.
            Assert.Equal("PREVIEW", vm.PreviewBlipLabel);
            vm.FillPanePreviewCommand.Execute(null);
            Assert.Equal("SOURCE", vm.PreviewBlipLabel);
            vm.FillPanePreviewCommand.Execute(null);

            Assert.False(vm.IsDirty);                     // presentation only — no draft edit
            Assert.Same(canvas, vm.Canvas);               // and no re-render
            Assert.Same(preview, vm.Preview);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // Minimal confirm-returning dialog double (mirrors the one in IngredientEditorSaveTests).
    private sealed class ConfirmingDialogs : IDialogService
    {
        private readonly bool _v;
        public ConfirmingDialogs(bool v) => _v = v;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d) => Task.FromResult((TResult?)(object?)_v);
        public void Close(object? result) { }
    }
}
