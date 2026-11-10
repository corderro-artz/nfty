using Nfty.App.ViewModels;

namespace Nfty.App.Tests;

/// <summary>Fixtures still spell a canvas as <c>WxH</c>; the form takes two numbers, set unlinked.</summary>
internal static class CanvasTestExtensions
{
    public static void SetCanvas(this NewIngredientViewModel vm, string wxh)
    {
        var parts = wxh.Split('x');
        vm.CanvasLinked = false;
        vm.CanvasWidth = int.Parse(parts[0]);
        vm.CanvasHeight = int.Parse(parts[1]);
    }
}
