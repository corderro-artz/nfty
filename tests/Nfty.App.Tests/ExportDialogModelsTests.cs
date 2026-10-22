using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The export card's 3D models row and the IMAGES-tab note about the mark.</summary>
public class ExportDialogModelsTests
{
    /// <summary>A cooked Set, optionally carrying a models/ file.</summary>
    internal static string SetDir(bool withModels)
    {
        var dir = VisualCapture.ExportCaptureSet();
        if (withModels)
        {
            var models = Path.Combine(dir, SetLayout.ModelsDir);
            Directory.CreateDirectory(models);
            File.WriteAllBytes(Path.Combine(models, "0001.glb"), new byte[] { 1, 2, 3, 4 });
            // A models/ folder is the Set's only as far as set.json says so.
            var json = Path.Combine(dir, SetLayout.ManifestFile);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<SetManifest>(
                File.ReadAllText(json), Nfty.Core.Formats.Json.Options)!;
            File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(
                manifest with { Models = new SetModels("glb", "bottom-center", 1f) }, Nfty.Core.Formats.Json.Options));
        }
        return dir;
    }

    internal static ExportDialogViewModel Vm(bool withModels) =>
        new(SetDir(withModels), new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs());

    internal static (Avalonia.Controls.Window, Views.ExportDialogView) Show(ExportDialogViewModel vm)
    {
        var view = new Views.ExportDialogView { DataContext = vm };
        var window = new Avalonia.Controls.Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    [AvaloniaFact]
    public void The_models_row_is_enabled_only_when_the_set_carries_models()
    {
        foreach (var has in new[] { true, false })
        {
            var vm = Vm(has);
            var (window, view) = Show(vm);
            try
            {
                var box = view.GetVisualDescendants().OfType<CheckBox>()
                    .Single(c => (c.Content as string) == "3D models");
                Assert.Equal(has, box.IsEnabled);
                Assert.True(box.Bounds.Height > 0, "the row is laid out either way");
                Assert.Equal(has, vm.SetHasModels);
                Assert.Equal(has ? "3D models of every asset, as cooked"
                                 : "This Set was cooked without 3D models.", vm.ModelsTip);
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void Unticking_models_drops_them_from_the_manifest_parts()
    {
        var vm = Vm(withModels: true);
        Assert.Contains(vm.Parts, p => p.StartsWith("3D models", StringComparison.Ordinal));

        vm.Models = false;

        Assert.DoesNotContain(vm.Parts, p => p.StartsWith("3D models", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void The_marketplace_tile_unticks_models()
    {
        var vm = Vm(withModels: true);
        Assert.True(vm.Models);

        vm.ApplyPresetCommand.Execute(vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace));

        Assert.False(vm.Models);
        Assert.True(vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace).IsSelected);
    }

    [AvaloniaFact]
    public void The_images_tab_says_the_mark_never_reaches_models()
    {
        foreach (var (has, mark, expected) in new[]
        {
            (true, true, 1d), (true, false, 0d), (false, true, 0d), (false, false, 0d),
        })
        {
            var vm = Vm(has);
            vm.Tab = ExportTab.Images;
            var (window, view) = Show(vm);
            try
            {
                vm.NumberWatermark = mark;
                Dispatcher.UIThread.RunJobs();
                var note = view.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Name == "ModelsMarkNote");
                Assert.Equal(expected, note.Opacity);
                Assert.True(note.Bounds.Height > 0, "laid out whether inked or not");
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaFact]
    public void Every_tab_is_the_same_height_with_the_models_row()
    {
        var heights = Enum.GetValues<ExportTab>().Select(tab =>
        {
            var vm = Vm(withModels: true);
            vm.Tab = tab;
            vm.NumberWatermark = true;
            vm.MakeSpriteSheet = true;
            var (window, view) = Show(vm);
            try
            {
                return view.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("modal")).Bounds.Height;
            }
            finally { window.Close(); }
        }).ToList();

        Assert.All(heights, h => Assert.Equal(heights[0], h, 1));
    }

    [AvaloniaFact]
    public void A_set_without_models_draws_the_box_unticked_and_presets_still_match()
    {
        var vm = Vm(withModels: false);
        var (window, view) = Show(vm);
        try
        {
            var box = view.GetVisualDescendants().OfType<CheckBox>()
                .Single(c => (c.Content as string) == "3D models");
            foreach (var preset in new[] { ExportPreset.Marketplace, ExportPreset.AssetPack,
                         ExportPreset.FullProject })
            {
                vm.ApplyPresetCommand.Execute(vm.Presets.Single(t => t.Preset == preset));
                Dispatcher.UIThread.RunJobs();
                Assert.False(box.IsChecked, $"{preset} left the models box ticked on a Set with none");
                Assert.True(vm.Presets.Single(t => t.Preset == preset).IsSelected,
                    $"{preset} no longer lights on a Set without models");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_users_own_models_folder_beside_an_unmodeled_set_does_not_light_the_box()
    {
        var dir = SetDir(withModels: false);
        var models = Path.Combine(dir, SetLayout.ModelsDir);
        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(models, "secret-source.blend"), "source");
        File.WriteAllText(Path.Combine(models, "0001.glb"), "stray");

        var vm = new ExportDialogViewModel(dir, new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs());

        Assert.False(vm.SetHasModels);
        Assert.DoesNotContain(vm.Parts, p => p.Contains("models", StringComparison.OrdinalIgnoreCase));
    }
}
