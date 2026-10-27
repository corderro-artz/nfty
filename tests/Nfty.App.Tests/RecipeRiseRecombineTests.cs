using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>A stroke on the write-through page recombines the stack once, over the rectangle it touched.</summary>
public class RecipeRiseRecombineTests
{
    private static (Nfty.App.ViewModels.RiseViewModel Vm, RecipeRiseSource Src) Page()
    {
        var vm = RisePages.DemoWriteThroughVm();
        RisePages.TargetLayer(vm, 2);
        return (vm, (RecipeRiseSource)vm.Source);
    }

    [AvaloniaFact]
    public void One_stroke_recombines_exactly_once()
    {
        var (vm, src) = Page();
        using var _ = vm;
        int before = src.RecombineCount;
        RisePages.PaintSolidPixel(vm);   // goes through the page: the stroke, then the 3D tile's rebuild
        Assert.Equal(before + 1, src.RecombineCount);
    }

    [AvaloniaFact]
    public void Undo_and_redo_recombine_once_each_and_a_pick_recombines_nothing()
    {
        var (vm, src) = Page();
        using var _ = vm;
        RisePages.PaintSolidPixel(vm);
        int painted = src.RecombineCount;
        vm.UndoCommand.Execute(null);
        Assert.Equal(painted + 1, src.RecombineCount);
        vm.RedoCommand.Execute(null);
        Assert.Equal(painted + 2, src.RecombineCount);
        RisePages.TargetLayer(vm, 0);
        Assert.Equal(painted + 2, src.RecombineCount);
    }

    [AvaloniaFact]
    public void The_texture_updated_in_place_is_the_atlas_of_the_faces()
    {
        var (vm, _) = Page();
        using var _ = vm;
        RisePages.PaintSolidPixel(vm);
        var subject = vm.Subject;
        using var fresh = FaceAtlas.Build(subject.Art, subject.Faces);
        for (int y = 0; y < fresh.Height; y++)
            for (int x = 0; x < fresh.Width; x++)
                Assert.Equal(fresh[x, y], subject.Texture[x, y]);
    }
}
