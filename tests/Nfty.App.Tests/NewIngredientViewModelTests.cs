using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class NewIngredientViewModelTests
{
    private static NewIngredientViewModel Make(out FakeDialogs d)
    { d = new FakeDialogs(); return new NewIngredientViewModel(d); }

    [Fact]
    public void Kind_selects_the_matching_colour_zone()
    {
        var vm = Make(out _);
        vm.Kind = LayerKind.Dynamic; Assert.True(vm.ShowColourRange); Assert.False(vm.ShowFixedColour);
        vm.Kind = LayerKind.Static;  Assert.True(vm.ShowFixedColour); Assert.False(vm.ShowColourRange);
        vm.Kind = LayerKind.Custom;  Assert.False(vm.ShowColourRange); Assert.False(vm.ShowFixedColour);
    }

    [Fact]
    public void Canvas_field_shows_only_when_loose()
    {
        var vm = Make(out _);
        vm.Destination = RecipeDestination.LooseKitchen; Assert.True(vm.ShowCanvas);
        vm.Destination = RecipeDestination.IntoCookBook; Assert.False(vm.ShowCanvas);
    }

    [Fact]
    public void DerivedId_slugs_the_name()
    {
        var vm = Make(out _); vm.Name = "Left Ear";
        Assert.Equal("left-ear", vm.DerivedId);
    }

    [Fact]
    public void BuildColorization_matches_the_kind()
    {
        var vm = Make(out _);
        vm.Kind = LayerKind.Dynamic; vm.HueMin = 10; vm.HueMax = 200; vm.SatMin = 30; vm.SatMax = 90;
        var dyn = vm.BuildColorization()!;
        Assert.Equal(ColorModel.Hsv, dyn.Model);
        Assert.Equal(12, dyn.HueQuantize); Assert.Equal(4, dyn.SatQuantize);
        var range = dyn.Entries.Single().Range!;
        Assert.Equal(10, range.HueMin); Assert.Equal(200, range.HueMax);
        Assert.Equal(30, range.SatMin); Assert.Equal(90, range.SatMax);

        vm.Kind = LayerKind.Static; vm.FixedColor = "hex:d6249f";
        Assert.Equal("hex:d6249f", vm.BuildColorization()!.Entries.Single().Fixed);

        vm.Kind = LayerKind.Custom;
        Assert.Null(vm.BuildColorization());
    }

    [AvaloniaFact]
    public void Build_makes_an_ingredient_with_one_blank_starter_variant()
    {
        var vm = Make(out _); vm.Name = "Hat"; vm.Kind = LayerKind.Dynamic;
        using var ing = vm.Build(new Dimensions(8, 8));
        Assert.Equal("hat", ing.Manifest.Id);
        var v = Assert.Single(ing.Manifest.Variants);
        Assert.Equal("variant-1", v.Id);
        Assert.Equal(8, ing.VariantImages["variant-1"].Width);
        Assert.Equal(0, ValueMap.FromImage(ing.VariantImages["variant-1"]).GetValue(4, 4));  // blank
        Assert.NotNull(ing.Manifest.Colorization);
    }

    [Fact]
    public void Create_is_disabled_until_the_name_yields_a_non_blank_id()
    {
        var vm = Make(out _);
        Assert.False(vm.CreateCommand.CanExecute(null));   // empty name
        vm.Name = "   ";
        Assert.False(vm.CreateCommand.CanExecute(null));   // whitespace → blank id
        vm.Name = "Hat";
        Assert.True(vm.CreateCommand.CanExecute(null));
    }

    [Fact]
    public async System.Threading.Tasks.Task Create_closes_the_dialog_with_the_vm()
    {
        var real = new DialogService();
        var vm = new NewIngredientViewModel(real) { Name = "Hat" };
        var task = real.ShowAsync<NewIngredientViewModel>(vm);
        vm.CreateCommand.Execute(null);
        Assert.Same(vm, await task);
    }

    [Fact]
    public void TryGetCanvas_parses_WxH_and_rejects_bad_input()
    {
        var vm = Make(out _);
        vm.CanvasSize = "512x512";
        Assert.True(vm.TryGetCanvas(out var c));
        Assert.Equal(512, c.Width); Assert.Equal(512, c.Height);

        vm.CanvasSize = " 8 x 8 ";
        Assert.True(vm.TryGetCanvas(out var c2));
        Assert.Equal(8, c2.Width); Assert.Equal(8, c2.Height);

        foreach (var bad in new[] { "", "abc", "0x8", "8", "8x", "-4x4", "8xY" })
        {
            vm.CanvasSize = bad;
            Assert.False(vm.TryGetCanvas(out _), $"expected '{bad}' to be rejected");
        }
    }
}
