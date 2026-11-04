using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Rise page builds its model off the UI thread: latest wins and lands in order, a release always
/// lands the exact model, a close mid-build is safe, a superseded subject's model never lands, and the
/// EXPORT tab's size settles on the exact figure. Every wait is on the page's own handle, held back by a
/// gate the test opens, never a sleep.
/// </summary>
public class RiseMeshOffThreadTests
{
    private static (int x, int y)[] Path(int n) => Enumerable.Range(0, n).Select(i => (1 + i, 2)).ToArray();

    /// <summary>The model the subject makes right now, built here on the test thread.</summary>
    private static Mesh? Exact(RiseViewModel vm)
    {
        var s = vm.Subject;
        return s.IsRisable ? Riser.TryBuild(s.Art, s.Depth.ToDepthMap(), new RiseOptions(), vm.Source.Title, s.Faces) : null;
    }

    private static void AssertSameMesh(Mesh? expected, Mesh? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected!.Positions, actual!.Positions);
        Assert.Equal(expected.Normals, actual.Normals);
        Assert.Equal(expected.TexCoords, actual.TexCoords);
        Assert.Equal(expected.Indices, actual.Indices);
    }

    /// <summary>A page whose every preview move is due a rebuild, with its builds held at a gate.</summary>
    private static (RiseViewModel Vm, TaskCompletionSource Gate, List<long> Applied) HeldPage()
    {
        var vm = RisePages.VariantVm();
        long t = 0;
        vm.NowMs = () => t += RiseViewModel.MeshBudgetMs;
        var gate = new TaskCompletionSource();
        vm.BeforeMeshBuild = () => gate.Task;
        var applied = new List<long>();
        vm.MeshApplied = applied.Add;
        return (vm, gate, applied);
    }

    [AvaloniaFact]
    public void A_drag_builds_the_first_and_the_latest_and_lands_them_in_order()
    {
        var (vm, gate, applied) = HeldPage();
        using var _ = vm;
        var before = vm.Mesh;
        long first = vm.MeshRequested + 1;
        for (int n = 1; n <= 5; n++) vm.PreviewDepthStroke(Path(n), DepthSide.Front, false);
        long last = vm.MeshRequested;
        Assert.Equal(first + 4, last);                   // five moves, five requests
        Assert.Same(before, vm.Mesh);                    // the stroke did not wait for a model
        Assert.False(vm.PendingMeshBuild.IsCompleted);

        vm.BeforeMeshBuild = null;
        gate.SetResult();
        RisePages.Settle(vm);

        Assert.Equal(2, vm.MeshBuildsRun);               // the three between were replaced, never built
        Assert.Equal(new[] { first, last }, applied);    // in request order: nothing older lands over newer
        AssertSameMesh(Exact(vm), vm.Mesh);              // the latest preview's model is the one shown
        vm.CancelDepthPreview();
        RisePages.Settle(vm);
    }

    [AvaloniaFact]
    public void A_release_lands_the_exact_model_of_the_committed_stroke()
    {
        var (vm, gate, _) = HeldPage();
        using var _v = vm;
        var flat = vm.Mesh!.Max.Z - vm.Mesh.Min.Z;
        for (int n = 1; n <= 4; n++) vm.PreviewDepthStroke(Path(n), DepthSide.Front, false);
        vm.ApplyDepthStroke(Path(4), DepthSide.Front, false);    // released while the first build is still held

        vm.BeforeMeshBuild = null;
        gate.SetResult();
        RisePages.Settle(vm);

        Assert.Equal(1, vm.Surface.Get(4, 2).Front);
        Assert.True(vm.Mesh!.Max.Z - vm.Mesh.Min.Z > flat);
        AssertSameMesh(Exact(vm), vm.Mesh);              // no stale preview left standing after release
    }

    [AvaloniaFact]
    public void Closing_the_page_mid_build_is_safe_and_nothing_lands_after()
    {
        var (vm, gate, applied) = HeldPage();
        vm.PreviewDepthStroke(Path(3), DepthSide.Front, false);
        vm.PreviewDepthStroke(Path(4), DepthSide.Front, false);    // one running, one waiting
        var shown = vm.Mesh;
        vm.Dispose();                                    // frees the subject the builds were taken from

        gate.SetResult();
        RisePages.Settle(vm);                            // completes, and does not fault: the build read its own copies

        Assert.Empty(applied);
        Assert.Same(shown, vm.Mesh);
        Assert.Equal(1, vm.MeshBuildsRun);               // the waiting one never started
    }

    [AvaloniaFact]
    public void A_build_for_the_previous_item_never_lands_over_the_next()
    {
        var (vm, gate, applied) = HeldPage();
        using var _ = vm;
        vm.ApplyDepthStroke(Path(3), DepthSide.Front, false);      // oak's build, held
        vm.SelectItemCommand.Execute(vm.Items.Single(i => i.Id == "ghost"));
        Assert.Null(vm.Mesh);                            // a new subject is built at once: ghost cannot rise

        gate.SetResult();
        RisePages.Settle(vm);

        Assert.Null(vm.Mesh);                            // oak's late model was dropped
        Assert.Single(applied);                          // only the switch's own answer
    }

    // ------------------------------------------------------------------ the EXPORT tab's size

    [AvaloniaFact]
    public void A_run_of_changes_counts_the_size_once_and_it_settles_exact()
    {
        using var vm = RisePages.VariantVm();
        vm.SizeDelay = _ => Task.CompletedTask;
        vm.ShowExportTabCommand.Execute(null);
        UiThreadWait.For(vm.PendingSize);
        var glb = vm.ManifestSize;
        Assert.NotEqual("", glb);
        int builds = vm.SizeBuilds;

        var later = new TaskCompletionSource();
        vm.SizeDelay = _ => later.Task;
        vm.VoxelSize = 2;
        vm.ApplyDepthStroke(Path(3), DepthSide.Front, false);
        vm.Pivot = Pivot.Center;
        Assert.True(vm.IsSizePending);
        Assert.Equal(glb, vm.ManifestSize);              // the last exact figure stands, marked, never an estimate
        Assert.Equal(builds, vm.SizeBuilds);

        later.SetResult();
        UiThreadWait.For(vm.PendingSize);

        Assert.False(vm.IsSizePending);
        Assert.Equal(builds + 1, vm.SizeBuilds);         // three changes, one count
        using var snapshot = vm.Source.SnapshotExport(new[] { vm.SelectedItem });
        var input = snapshot.Open(0, "x");
        long actual = ModelBatch.Build(new[] { input }, vm.ExportOptions).Files.Sum(f => (long)f.Bytes.Length);
        Nfty.App.Rise.RiseExportSnapshot.Release(input);
        Assert.Equal(Nfty.Core.Publish.SizeText.Of(actual), vm.ManifestSize);
        RisePages.Settle(vm);
    }

    [AvaloniaFact]
    public void Closing_the_page_while_the_size_is_counted_is_safe()
    {
        var vm = RisePages.VariantVm();
        var hold = new TaskCompletionSource();
        vm.SizeDelay = _ => hold.Task;
        vm.ShowExportTabCommand.Execute(null);
        Assert.True(vm.IsSizePending);
        vm.Dispose();
        hold.SetResult();
        UiThreadWait.For(vm.PendingSize);
        Assert.Equal(0, vm.SizeBuilds);                  // the count that was due never ran on a closed page
    }
}
