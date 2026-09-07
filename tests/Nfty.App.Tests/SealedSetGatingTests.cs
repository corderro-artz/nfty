using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// What the app will and will not do with a Set that came out of a sealed export.
/// </summary>
/// <remarks>
/// <b>Every exit is gated, not just the obvious one.</b> Save image is an export of one asset, and
/// the inspector has a second copy of that button — a seal that closed the Export button while
/// leaving either of those working would be a label rather than a rule. These are the tests that
/// say so; <c>SetExporter</c> refuses a sealed source independently, so the screen agrees with the
/// engine rather than being the only thing enforcing it.
/// </remarks>
public class SealedSetGatingTests
{
    private const string Pass = "correct-horse-battery-staple";

    private static string CookedSet()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-seal-").FullName, "Chest Demo");
        var ids = new[] { "a", "b", "c", "d" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v,
                v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255))),
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "d", "CHST"),
                new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
        using var set = Generator.Generate(book, new GenerateOptions(3, "launch"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    /// <summary>A sealed export on disk, and the Set opened back out of it.</summary>
    private static SealedSet OpenSealed(string? note = "Draft for review")
    {
        var outDir = Directory.CreateTempSubdirectory("nfty-tin-").FullName;
        var result = SetExporter.Export(CookedSet(), outDir,
            ExportOptions.For(ExportPreset.SealedCritique) with { Note = note }, passphrase: Pass);
        return SealedSetReader.Open(result.Path, Pass);
    }

    private static SetBrowserViewModel Browser(LoadedSet set) =>
        new(set, new FilePickerService(), new FakeDialogs(), new StatusService(),
            new NoopFolderRevealer());

    [Fact]
    public void An_ordinary_set_can_be_exported_and_its_assets_saved()
    {
        // The control case. Without it the assertions below could pass on a browser that simply
        // never enables anything.
        using var set = SetReader.Read(CookedSet());
        var vm = Browser(set);

        Assert.False(vm.IsSealed);
        Assert.True(vm.CanExport);
        Assert.True(vm.ExportCommand.CanExecute(null));
        Assert.True(vm.SaveImageCommand.CanExecute(null));
    }

    [Fact]
    public void A_sealed_set_refuses_export_and_both_save_paths()
    {
        using var set = OpenSealed();
        var vm = Browser(set);

        Assert.True(vm.IsSealed);
        Assert.False(vm.CanExport);
        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.False(vm.SaveImageCommand.CanExecute(null));

        // The inspector's Save is a second copy of the same button, two clicks away and easy to
        // forget. It is told at construction, not left to work it out.
        using var inspector = new SetInspectViewModel(vm.Items, 0, new FilePickerService(),
            new FakeDialogs(), new StatusService(), allowExport: vm.CanExport);
        Assert.False(inspector.AllowExport);
        Assert.False(inspector.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void The_inspector_allows_saving_from_an_ordinary_set()
    {
        // The other half of the pair above: allowExport defaults to true, so an ordinary Set is
        // unaffected by the gate.
        using var set = SetReader.Read(CookedSet());
        var vm = Browser(set);
        using var inspector = new SetInspectViewModel(vm.Items, 0, new FilePickerService(),
            new FakeDialogs(), new StatusService(), allowExport: vm.CanExport);

        Assert.True(inspector.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void The_browser_carries_the_senders_note_so_the_recipient_can_read_it()
    {
        using var set = OpenSealed("Please do not redistribute");
        var vm = Browser(set);

        Assert.Equal("Please do not redistribute", vm.SealNote);
    }

    [Fact]
    public void A_sealed_export_with_no_note_says_nothing_rather_than_showing_an_empty_chip()
    {
        using var set = OpenSealed(note: null);
        var vm = Browser(set);

        Assert.True(vm.IsSealed);
        Assert.Equal("", vm.SealNote);
    }

    [Fact]
    public void The_engine_refuses_a_sealed_source_independently_of_the_screen()
    {
        // The screen agreeing with the engine is worth having, but the engine is what makes the
        // policy true: without this, the CLI could launder a sealed Set back into an open one in a
        // single command.
        var outDir = Directory.CreateTempSubdirectory("nfty-tin-").FullName;
        var tin = SetExporter.Export(CookedSet(), outDir,
            ExportOptions.For(ExportPreset.SealedCritique), passphrase: Pass).Path;

        Assert.Throws<SealedSetException>(() => SetExporter.Export(tin,
            Directory.CreateTempSubdirectory().FullName, ExportOptions.For(ExportPreset.AssetPack)));
    }

    [AvaloniaFact]
    public void The_export_button_in_the_rendered_browser_opens_the_dialog()
    {
        // A command with a body is not a button that reaches it. `WiringCoverageTests` proves the
        // binding resolves and the ViewModel tests prove the command works; neither can say the
        // control in the header is wired to THAT command - which is exactly how Landing shipped a
        // "+ Recipe" button whose wizard result was dropped on the floor.
        using var set = SetReader.Read(CookedSet());
        var dialogs = new FakeDialogs();
        var vm = new SetBrowserViewModel(set, new FilePickerService(), dialogs, new StatusService(),
            new NoopFolderRevealer());
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content is StackPanel p
                && p.Children.OfType<TextBlock>().Any(t => t.Text == "Export…"));

        Assert.True(button.IsEffectivelyEnabled);
        Assert.NotNull(button.Command);
        button.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<ExportDialogViewModel>(dialogs.Active);
        window.Close();
    }

    [AvaloniaFact]
    public void The_export_button_is_dead_on_a_sealed_set()
    {
        // The same control, the other state. Asserted off the rendered button rather than off
        // CanExport, because a disabled ViewModel property and a disabled CONTROL are two claims and
        // only the second one is what the user meets.
        using var set = OpenSealed();
        var vm = new SetBrowserViewModel(set, new FilePickerService(), new FakeDialogs(),
            new StatusService(), new NoopFolderRevealer());
        var view = new Views.SetBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = view.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content is StackPanel p
                && p.Children.OfType<TextBlock>().Any(t => t.Text == "Export…"));

        Assert.False(button.IsEffectivelyEnabled);
        window.Close();
    }

    [Fact]
    public void The_passphrase_prompt_says_what_the_file_is_before_asking_for_the_key()
    {
        // The reason the seal's header sits outside the encryption at all. A recipient meeting a
        // bare password box has been told nothing; this dialog names the collection, its size, the
        // sender's note and what they will be allowed to do with it.
        var outDir = Directory.CreateTempSubdirectory("nfty-tin-").FullName;
        var tin = SetExporter.Export(CookedSet(), outDir,
            ExportOptions.For(ExportPreset.SealedCritique) with { Note = "Have a look" },
            passphrase: Pass).Path;

        var vm = new PassphraseDialogViewModel(Seal.Peek(tin), new FakeDialogs());

        Assert.Equal("Chest Demo", vm.Collection);
        Assert.Equal(3, vm.Count);
        Assert.Equal("Have a look", vm.Note);
        Assert.True(vm.HasNote);
        Assert.False(vm.AllowsExport);
        Assert.Contains("View only", vm.Policy);

        // And it cannot be submitted empty, so an accidental Return does not count as an attempt.
        Assert.False(vm.OpenCommand.CanExecute(null));
        vm.Passphrase = "x";
        Assert.True(vm.OpenCommand.CanExecute(null));
    }
}
