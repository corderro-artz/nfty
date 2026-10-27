using System;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The dirty rectangle, held against a whole combine THROUGH THE REAL PAGE: a seeded random session of strokes,
/// previews and cancels, undo and redo across layers, retargets, fill-all and rerolls, after each of which the
/// subject's depth, faces, seams and atlas must be byte-equal to combining the stack again from scratch.
/// </summary>
public class RecipeRiseIncrementalOracleTests
{
    private static void AssertMatchesWholeCombine(RiseViewModel vm, RecipeRiseSource src, string step)
    {
        using var whole = src.CombineFromScratch();
        if (whole is null) return;
        var subject = vm.Subject;
        int w = subject.Art.Width, h = subject.Art.Height;
        Assert.True(whole.Depth.ToBytes().SequenceEqual(UnionOf(subject)), $"{step}: union depth differs");
        using var atlas = FaceAtlas.Build(subject.Art, whole.Faces);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Assert.True(whole.Faces.Front[x, y] == subject.Faces!.Front[x, y], $"{step}: front face ({x},{y})");
                Assert.True(whole.Faces.Back[x, y] == subject.Faces.Back[x, y], $"{step}: back face ({x},{y})");
                Assert.True(whole.Faces.BackColored(x, y) == subject.Faces.BackColored(x, y), $"{step}: seam ({x},{y})");
            }
        for (int y = 0; y < atlas.Height; y++)
            for (int x = 0; x < atlas.Width; x++)
                Assert.True(atlas[x, y] == subject.Texture[x, y], $"{step}: atlas ({x},{y})");
    }

    private static byte[] UnionOf(RiseSubject subject)
    {
        var s = subject.Depth;
        var bytes = new byte[s.Width * s.Height * 2];
        for (int y = 0; y < s.Height; y++)
            for (int x = 0; x < s.Width; x++)
            {
                var p = s.Get(x, y);
                bytes[((y * s.Width) + x) * 2] = p.Front;
                bytes[(((y * s.Width) + x) * 2) + 1] = p.Back;
            }
        return bytes;
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void A_random_session_through_the_page_always_equals_a_whole_combine(int seed)
    {
        var rng = new Random(seed);
        var vm = RisePages.DemoWriteThroughVm(roll: seed);
        using var _ = vm;
        var src = (RecipeRiseSource)vm.Source;
        AssertMatchesWholeCombine(vm, src, "open");

        for (int i = 0; i < 60; i++)
        {
            int w = vm.Surface.Width, h = vm.Surface.Height;
            string op;
            switch (rng.Next(10))
            {
                case 0 or 1 or 2:
                {
                    op = "stroke";
                    if (src.Target is null) RetargetRandomly(vm, rng);
                    vm.BrushSize = rng.Next(1, 6);
                    var pts = Enumerable.Range(0, rng.Next(1, 8)).Select(k => (rng.Next(w), rng.Next(h))).ToList();
                    vm.ApplyDepthStroke(pts, rng.Next(2) == 0 ? DepthSide.Front : DepthSide.Back, lower: rng.Next(4) == 0);
                    break;
                }
                case 3:
                    op = "retarget";
                    RetargetRandomly(vm, rng);
                    break;
                case 4:
                    op = "undo";
                    if (vm.UndoCommand.CanExecute(null)) vm.UndoCommand.Execute(null);
                    break;
                case 5:
                    op = "redo";
                    if (vm.RedoCommand.CanExecute(null)) vm.RedoCommand.Execute(null);
                    break;
                case 6:
                {
                    op = "preview+cancel";
                    if (src.Target is null) RetargetRandomly(vm, rng);
                    vm.BrushSize = rng.Next(1, 5);
                    var pts = Enumerable.Range(0, rng.Next(1, 5)).Select(k => (rng.Next(w), rng.Next(h))).ToList();
                    vm.PreviewDepthStroke(pts, DepthSide.Front, lower: false);
                    vm.CancelDepthPreview();
                    break;
                }
                case 7:
                    op = "fill-all";
                    if (src.Target is null) RetargetRandomly(vm, rng);
                    vm.FillFront = rng.Next(0, 6);
                    vm.FillBack = rng.Next(0, 6);
                    if (vm.FillAllCommand.CanExecute(null)) vm.FillAllCommand.Execute(null);
                    break;
                case 8:
                {
                    // Two layers dirty in ONE recombine: a throttled standing preview on one layer is still
                    // unsynced when the next stroke, on another layer, first reverts it.
                    op = "preview, retarget, stroke";
                    if (src.Target is null) RetargetRandomly(vm, rng);
                    vm.BrushSize = 2;
                    if (FirstHittable(vm) is { } a)
                    {
                        vm.PreviewDepthStroke(new[] { a }, DepthSide.Front, lower: false);
                        vm.PreviewDepthStroke(new[] { a, (Math.Min(a.x + 2, w - 1), a.y) }, DepthSide.Back, lower: false);
                    }
                    RetargetRandomly(vm, rng);
                    if (FirstHittable(vm) is { } b)
                        vm.ApplyDepthStroke(new[] { b }, DepthSide.Front, lower: false);
                    break;
                }
                default:
                    op = "reroll";
                    if (vm.RerollCommand.CanExecute(null)) vm.RerollCommand.Execute(null);
                    break;
            }
            AssertMatchesWholeCombine(vm, src, $"{seed}/{i} {op}");
        }
    }

    private static (int x, int y)? FirstHittable(RiseViewModel vm)
    {
        for (int y = 0; y < vm.Surface.Height; y++)
            for (int x = 0; x < vm.Surface.Width; x++)
                if (vm.IsHittable(x, y)) return (x, y);
        return null;
    }

    private static void RetargetRandomly(RiseViewModel vm, Random rng)
    {
        var rows = vm.LayerRows.Where(r => r.IsPickable).ToList();
        if (rows.Count > 0) vm.TargetLayerCommand.Execute(rows[rng.Next(rows.Count)]);
    }
}
