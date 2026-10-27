using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Painted depth is the SESSION's unsaved work, not the current roll's: a reroll into a roll that cannot rise
/// (partial alpha on a layer, or a book that cannot cook) must not turn the page read-only over it — Save stays
/// inked and Back still asks.
/// </summary>
public class RiseUnsavedAcrossRerollTests
{
    private sealed class Rig
    {
        public required RiseViewModel Vm { get; init; }
        public required RecipeRiseSource Src { get; init; }
        public required CookBookSession Session { get; init; }
        public required FakeNav Nav { get; init; }
        public required FakeDialogs Dialogs { get; init; }
        public required LoadedIngredient Body { get; init; }
        public required string PaintedIngredient { get; init; }
        public required string PaintedVariant { get; init; }
        public required byte[] DepthBefore { get; init; }
    }

    private static Rig PaintedLock()
    {
        var (s, _) = RisePages.DemoSession();
        var book = s.Current!;
        var recipe = book.Recipes.Single(r => r.Manifest.Id == "chest");
        var src = new RecipeRiseSource(book, recipe, 1, new ImageBridge(), s, () => true);
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var vm = new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), new StatusService());
        RisePages.TargetLayer(vm, 4);                        // Lock: a layer the next roll will still hold
        RisePages.PaintSolidPixel(vm);
        var t = src.Target!;
        var map = recipe.Ingredients.Single(i => i.Manifest.Id == t.IngredientId).Depths.GetValueOrDefault(t.VariantId);
        Assert.Equal(1, vm.DirtyCount);
        return new Rig
        {
            Vm = vm, Src = src, Session = s, Nav = nav, Dialogs = dialogs,
            Body = recipe.Ingredients.Single(i => i.Manifest.Id == "chestbody"),
            PaintedIngredient = t.IngredientId, PaintedVariant = t.VariantId,
            DepthBefore = map?.ToBytes() ?? System.Array.Empty<byte>(),
        };
    }

    private static void MakeEveryBodyPartial(Rig r)
    {
        foreach (var image in r.Body.VariantImages.Values) image[0, 0] = new Rgba32(10, 10, 10, 128);
    }

    private static void MakeBookUncookable(Rig r) => ((System.Collections.Generic.Dictionary<string, SixLabors.ImageSharp.Image<Rgba32>>)r.Body.VariantImages).Clear();

    private static void AssertStillUnsavedWork(Rig r)
    {
        Assert.False(r.Src.IsEditable);                      // the roll cannot be painted ...
        Assert.Equal(1, r.Vm.DirtyCount);                    // ... but the work painted before it is still there
        Assert.True(r.Vm.ShowSave);
        Assert.True(r.Vm.SaveCommand.CanExecute(null));
        Assert.Equal("unsaved depth on 1 variant", r.Vm.FooterText);
    }

    [AvaloniaFact]
    public async Task Rerolling_into_a_partial_alpha_roll_keeps_Save_and_makes_Back_ask()
    {
        var r = PaintedLock();
        MakeEveryBodyPartial(r);
        r.Vm.RerollCommand.Execute(null);
        AssertStillUnsavedWork(r);

        await r.Vm.BackCommand.ExecuteAsync(null);
        Assert.IsType<LeaveDialogViewModel>(r.Dialogs.Active);
        Assert.Equal(0, r.Nav.BackCount);
    }

    [AvaloniaFact]
    public async Task Rerolling_into_a_roll_that_cannot_cook_keeps_Save_and_makes_Back_ask()
    {
        var r = PaintedLock();
        MakeBookUncookable(r);
        r.Vm.RerollCommand.Execute(null);
        Assert.NotNull(r.Src.Problem);
        AssertStillUnsavedWork(r);

        await r.Vm.BackCommand.ExecuteAsync(null);
        Assert.IsType<LeaveDialogViewModel>(r.Dialogs.Active);
        Assert.Equal(0, r.Nav.BackCount);
    }

    [AvaloniaFact]
    public async Task Save_after_such_a_reroll_still_writes_the_painted_variant()
    {
        var r = PaintedLock();
        MakeEveryBodyPartial(r);
        r.Vm.RerollCommand.Execute(null);

        await r.Vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, r.Vm.DirtyCount);
        var live = r.Session.Current!.Recipes.Single(x => x.Manifest.Id == "chest")
            .Ingredients.Single(i => i.Manifest.Id == r.PaintedIngredient);
        Assert.NotEqual(r.DepthBefore, live.Depths[r.PaintedVariant].ToBytes());
    }

    [AvaloniaFact]
    public void A_locked_book_still_refuses_that_save()
    {
        var r = PaintedLock();
        MakeEveryBodyPartial(r);
        r.Vm.RerollCommand.Execute(null);
        var locked = new RecipeRiseSource(r.Session.Current!, r.Session.Current!.Recipes.Single(x => x.Manifest.Id == "chest"),
            1, new ImageBridge(), r.Session, () => false);
        Assert.False(locked.CanSave);
        locked.Dispose();
    }
}
