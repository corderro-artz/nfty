using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Xunit;

namespace Nfty.App.Tests;

public class RiseDepthRailTests
{
    private static RiseViewModel Vm(RiseSource src) =>
        new(src, new ImageBridge(), new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());

    private static RiseViewModel OakVm(string variant = "oak")
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        return Vm(RiseFixtures.Variant(s, variant));
    }

    [AvaloniaFact]
    public void Fill_all_gives_every_solid_pixel_the_depth_in_one_undo_step()
    {
        using var vm = OakVm();
        vm.FillFront = 2; vm.FillBack = 3;
        vm.FillAllCommand.Execute(null);
        var s = vm.Surface;
        for (int y = 0; y < s.Height; y++)
            for (int x = 0; x < s.Width; x++)
                if (s.IsSolid(x, y)) Assert.Equal(new DepthPixel(2, 3), s.Get(x, y));
        vm.UndoCommand.Execute(null);
        Assert.Equal(new DepthPixel(0, 0), vm.Surface.Get(2, 1));
        Assert.False(vm.UndoCommand.CanExecute(null));      // one step, not one per pixel
    }

    [AvaloniaFact]
    public void Fill_all_is_disabled_and_inert_for_a_recipe_and_a_two_d_only_item()
    {
        using var ghost = OakVm("ghost");
        Assert.False(ghost.FillAllCommand.CanExecute(null));
        ghost.FillAllCommand.Execute(null);                 // generated commands do not consult CanExecute
        Assert.Equal(new DepthPixel(0, 0), ghost.Surface.Get(2, 1));
        Assert.False(ghost.UndoCommand.CanExecute(null));

        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var recipe = Vm(new RecipeRiseSource(s.Current!, s.Current!.Recipes[0], 1, new ImageBridge()));
        Assert.False(recipe.FillAllCommand.CanExecute(null));
        recipe.FillAllCommand.Execute(null);
        Assert.False(recipe.UndoCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData(300, 255)]
    [InlineData(-1, 0)]
    [InlineData(7, 7)]
    public void The_fill_depths_clamp_at_the_property(int asked, int expected)
    {
        using var vm = OakVm();
        vm.FillFront = asked; vm.FillBack = asked;
        Assert.Equal(expected, vm.FillFront);
        Assert.Equal(expected, vm.FillBack);
    }

    [AvaloniaFact]
    public void The_histogram_follows_a_fill_and_its_labels_say_what_they_count()
    {
        using var vm = OakVm();
        Assert.Equal("Apply to 32 solid pixels", vm.FillAllLabel);
        Assert.Equal("32 solid px", vm.SolidHeader);
        Assert.Equal("deepest 1", vm.DeepestText);
        vm.FillFront = 1; vm.FillBack = 1;
        vm.FillAllCommand.Execute(null);
        var row = Assert.Single(vm.Thickness);
        Assert.Equal("3 voxels", row.Label);
        Assert.Equal(32, row.Count);
        Assert.Equal("32", row.CountText);
        Assert.Equal(1.0, row.Share);
        Assert.Equal("deepest 3", vm.DeepestText);
    }

    [AvaloniaFact]
    public void A_mixed_histogram_has_one_row_per_thickness_with_shares_of_the_solid()
    {
        using var vm = OakVm();
        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        Assert.Equal(new[] { "1 voxel", "2 voxels" }, vm.Thickness.Select(r => r.Label));
        Assert.Equal(new[] { 31, 1 }, vm.Thickness.Select(r => r.Count));
        Assert.Equal(1.0 / 32, vm.Thickness[1].Share, 6);
    }

    [AvaloniaFact]
    public void Tabs_switch_and_the_ghost_is_listed_with_its_own_note()
    {
        using var vm = OakVm();
        Assert.True(vm.IsDepthTab); Assert.False(vm.IsExportTab);
        vm.ShowExportTabCommand.Execute(null);
        Assert.True(vm.IsExportTab);
        vm.ShowDepthTabCommand.Execute(null);
        Assert.True(vm.IsDepthTab);

        Assert.True(vm.HasPartialAlphaRows);
        Assert.Equal("1 item", vm.PartialAlphaCountText);
        Assert.Contains(vm.PartialAlphaRows, r => r.Name == "Ghost");
        var oakNote = vm.DepthNote;
        vm.SelectedItem = vm.Items.Single(i => i.Id == "ghost");
        Assert.NotEqual(oakNote, vm.DepthNote);
        Assert.Equal("This item has partial alpha, so it stays 2D: a voxel is solid or empty.", vm.DepthNote);
    }

    [AvaloniaFact]
    public void Two_ghosts_are_two_items_and_one_solid_pixel_is_singular()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(withSecondGhost: true));
        using var two = Vm(RiseFixtures.Variant(s));
        Assert.Equal("2 items", two.PartialAlphaCountText);

        var (s2, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var recipe = s2.Current!.Recipes[0];
        var lck = recipe.Ingredients.Single(i => i.Manifest.Id == "lock");
        var img = lck.VariantImages["gold"];
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if ((x, y) != (3, 3)) img[x, y] = default;
        using var one = Vm(new VariantRiseSource(recipe, lck, "gold", new ImageBridge(), s2, () => true));
        Assert.Equal("Apply to 1 solid pixel", one.FillAllLabel);
    }
}
