using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class ViewerThreeDTests
{
    private sealed class Picker(string? path, string? folder = null) : IFilePickerService
    {
        public int Asked { get; private set; }
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) { Asked++; return Task.FromResult(path); }
        public Task<string?> PickFolderAsync(string title) { Asked++; return Task.FromResult(folder); }
    }

    private static DepthMap FlatDepth(byte front) =>
        DepthMap.FromBytes(8, 8, Enumerable.Range(0, 64).SelectMany(_ => new[] { front, (byte)0 }).ToArray());

    // A Chest book on disk with oak at depth 2, a Set of 12 cooked into a folder beside it (or elsewhere).
    private static string Cooked(bool withModels, bool beside = true)
    {
        var (s, bookPath) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(FlatDepth(2)));
        string parent = beside ? Path.GetDirectoryName(bookPath)! : Directory.CreateTempSubdirectory("nfty-3d-set-").FullName;
        string setDir = Path.Combine(parent, "Chest Set");
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(12, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, setDir, pack: false);
        s.Dispose();
        if (withModels) RecordModels(setDir, skipped: [2]);
        return setDir;
    }

    // The fixture book has partly transparent art, which SetModeler refuses, so the models a modeled
    // cook would have written are written by hand: one real .glb per asset (but the skipped ones) and
    // the models block in set.json that says so.
    private static void RecordModels(string setDir, int[] skipped)
    {
        var manifestPath = SetLayout.ManifestPath(setDir);
        var manifest = System.Text.Json.JsonSerializer.Deserialize<SetManifest>(
            File.ReadAllText(manifestPath), Nfty.Core.Formats.Json.Options)!;
        Directory.CreateDirectory(Path.Combine(setDir, SetLayout.ModelsDir));
        using var art = Art();
        var mesh = Riser.TryBuild(art, null, new RiseOptions(), "t")!;
        var glb = ModelWriter.Write(mesh, art, new RiseOptions(), "x")[0].Bytes;
        for (int n = 1; n <= manifest.Count; n++)
            if (!skipped.Contains(n))
                File.WriteAllBytes(Path.Combine(setDir, SetLayout.ModelsDir, SetLayout.Stem(n) + ".glb"), glb);
        File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(
            manifest with { Models = new SetModels("glb", "bottom-center", 1f, skipped) }, Nfty.Core.Formats.Json.Options));
    }

    private static ViewerViewModel Vm(ViewerSubject s, IFilePickerService picker) =>
        new(s, picker, new NoopClipboardService(), new DialogService());

    private static Image<Rgba32> Art() => ViewerViewTests.Art();

    // A mesh risen from real art, with the atlas ModelWriter would build for it.
    private static (Mesh Mesh, Image<Rgba32> Atlas, Image<Rgba32> Art) Risen()
    {
        var art = Art();
        var mesh = Riser.TryBuild(art, null, new RiseOptions(), "t")!;
        return (mesh, FaceAtlas.Build(art), art);
    }

    // ---- sources ----

    [AvaloniaFact]
    public async Task A_set_asset_with_a_recorded_model_opens_with_that_model()
    {
        var dir = Cooked(withModels: true);
        using var set = SetReader.Read(dir);
        var skipped = set.Manifest.Models!.Skipped ?? [];
        var item = set.Items.First(i => !skipped.Contains(i.Number));

        using var subject = await ModelSubjects.ForSetAssetAsync(set, item);

        Assert.NotNull(subject.Model);
        var file = Assert.Single(subject.Model!.Files!);
        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "models", file.FileName)), file.Bytes);
        Assert.Contains(subject.Facts, f => f.Value == "recorded in the Set");
    }

    [AvaloniaFact]
    public async Task A_set_asset_without_models_rises_from_the_book_found_by_hash()
    {
        var dir = Cooked(withModels: false);
        using var set = SetReader.Read(dir);

        using var subject = await ModelSubjects.ForSetAssetAsync(set, set.Items[0]);

        Assert.NotNull(subject.Model);
        Assert.Null(subject.Model!.Files);
        Assert.Contains(subject.Facts, f => f.Value.StartsWith("risen from the CookBook", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task A_set_asset_with_no_book_and_no_model_is_2d_only()
    {
        var dir = Cooked(withModels: false, beside: false);
        using var set = SetReader.Read(dir);

        using var subject = await ModelSubjects.ForSetAssetAsync(set, set.Items[0], () => Array.Empty<string>());

        Assert.Null(subject.Model);
        Assert.NotNull(subject.Art);
    }

    // ---- camera ----

    [Fact]
    public void Turntable_at_medium_turns_45_degrees_in_one_second()
    {
        var vm = new ModelPreviewViewModel(-Vector3.One, Vector3.One) { Turntable = TurntableSpeed.Medium };
        float before = vm.CameraState.Yaw;
        vm.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(45f, (vm.CameraState.Yaw - before) * 180f / MathF.PI, 3);
    }

    [Fact]
    public void Turntable_off_does_not_turn()
    {
        var vm = new ModelPreviewViewModel(-Vector3.One, Vector3.One);
        var before = vm.CameraState;
        vm.Tick(TimeSpan.FromSeconds(5));
        Assert.Same(before, vm.CameraState);
    }

    [Fact]
    public void The_front_preset_looks_down_negative_z()
    {
        var vm = new ModelPreviewViewModel(-Vector3.One, Vector3.One);
        vm.Preset(ViewAngle.Side);   // somewhere else first
        vm.Preset(ViewAngle.Front);
        var look = Vector3.Normalize(vm.CameraState.Target - vm.CameraState.Eye);
        Assert.True(Vector3.Distance(look, -Vector3.UnitZ) < 1e-4f, look.ToString());
        vm.Preset(ViewAngle.Top);
        var down = Vector3.Normalize(vm.CameraState.Target - vm.CameraState.Eye);
        Assert.True(down.Y < -0.99f, down.ToString());
    }

    // ---- export ----

    [AvaloniaFact]
    public async Task Exporting_a_recorded_model_copies_its_bytes_exactly()
    {
        var dir = Cooked(withModels: true);
        using var set = SetReader.Read(dir);
        var skipped = set.Manifest.Models!.Skipped ?? [];
        var item = set.Items.First(i => !skipped.Contains(i.Number));
        using var subject = await ModelSubjects.ForSetAssetAsync(set, item);
        string target = Path.Combine(Directory.CreateTempSubdirectory().FullName, "out.glb");
        var vm = Vm(subject, new Picker(target));

        await vm.ExportModelCommand.ExecuteAsync(null);

        Assert.Equal(subject.Model!.Files![0].Bytes, File.ReadAllBytes(target));
    }

    [AvaloniaFact]
    public async Task An_in_memory_model_exports_the_bytes_the_writer_makes_whatever_the_outline_display_says()
    {
        var (mesh, atlas, art) = Risen();
        using var _a = atlas;
        using var _b = art;
        using var subject = new SnapshotSubject("t", [], art, new ViewerModel(mesh, atlas, null));
        string dir = Directory.CreateTempSubdirectory().FullName;
        var vm = Vm(subject, new Picker(Path.Combine(dir, "a.glb")));
        var expected = ModelWriter.Write(mesh, art, new RiseOptions(), "t")[0].Bytes;

        vm.Preview.Wireframe = false;
        await vm.ExportModelCommand.ExecuteAsync(null);
        var off = File.ReadAllBytes(Path.Combine(dir, "a.glb"));

        vm = Vm(subject, new Picker(Path.Combine(dir, "b.glb")));
        vm.Preview.Wireframe = true;
        vm.Preview.Turntable = TurntableSpeed.Fast;
        await vm.ExportModelCommand.ExecuteAsync(null);
        var on = File.ReadAllBytes(Path.Combine(dir, "b.glb"));

        Assert.Equal(expected, off);
        Assert.Equal(off, on);
    }

    [AvaloniaFact]
    public async Task A_sealed_subject_cannot_save_a_still_or_export_a_model()
    {
        var (mesh, atlas, art) = Risen();
        using var _a = atlas;
        using var _b = art;
        using var subject = new SnapshotSubject("t", [], art, new ViewerModel(mesh, atlas, null)) { AllowExport = false };
        var picker = new Picker(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".out"));
        var vm = Vm(subject, picker);
        vm.StillSource = () => [1, 2, 3];

        Assert.False(vm.ExportModelCommand.CanExecute(null));
        Assert.False(vm.SaveStillCommand.CanExecute(null));
        // Invoked around CanExecute, as a keyboard shortcut or a test would: still nothing.
        await vm.ExportModelCommand.ExecuteAsync(null);
        await vm.SaveStillCommand.ExecuteAsync(null);
        Assert.Equal(0, picker.Asked);
    }

    // ---- the outline display and the still ----

    private static (Window Window, ModelPreviewViewModel Vm, ModelPreviewView View) Shown()
    {
        var (mesh, atlas, art) = Risen();
        art.Dispose();
        var vm = new ModelPreviewViewModel(mesh.Min, mesh.Max);
        var view = new ModelPreviewView { DataContext = vm, Mesh = mesh, Texture = atlas };
        var window = new Window { Content = view, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        view.RepaintNow();
        return (window, vm, view);
    }

    [AvaloniaFact]
    public void The_outline_display_changes_the_frame_and_a_still_carries_it()
    {
        var (window, vm, view) = Shown();
        try
        {
            var plain = view.EncodeStill();
            Assert.NotNull(plain);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, plain![..4]);

            vm.Wireframe = true;
            view.RepaintNow();
            var edged = view.EncodeStill();
            Assert.NotEqual(plain, edged);

            vm.Wireframe = false;
            view.RepaintNow();
            Assert.Equal(plain, view.EncodeStill());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_turntable_timer_runs_only_while_a_speed_is_chosen()
    {
        var (window, vm, view) = Shown();
        try
        {
            Assert.False(view.TurntableRunning);
            vm.Turntable = TurntableSpeed.Slow;
            Assert.True(view.TurntableRunning);
            vm.Turntable = TurntableSpeed.Off;
            Assert.False(view.TurntableRunning);
        }
        finally { window.Close(); }
    }
}
