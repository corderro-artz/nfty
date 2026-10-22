using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Save as .igt: a picture's or a sheet's depth leaves as a Custom ingredient.</summary>
public class PictureSaveAsIgtTests
{
    private const int Cell = 16;

    private static string Temp() => Directory.CreateTempSubdirectory("nfty-picture-igt-").FullName;

    private static string SheetOnDisk(string dir, string name, int cols)
    {
        using var img = new Image<Rgba32>(cols * Cell, Cell);
        for (int n = 0; n < cols; n++)
            for (int y = 4; y < 12; y++)
                for (int x = 4; x < 12; x++)
                    img[n * Cell + x, y] = new Rgba32((byte)(40 + n * 60), (byte)(200 - n * 40), 90, 255);
        var path = Path.Combine(dir, name);
        img.Save(path);
        return path;
    }

    private static string PictureOnDisk(string dir, string name)
    {
        using var img = new Image<Rgba32>(8, 8, new Rgba32(200, 30, 30, 255));
        var path = Path.Combine(dir, name);
        img.Save(path);
        return path;
    }

    private static void Paint(PictureRiseSource src, int item, int x, int y)
    {
        var subject = src.Open(src.Items[item]);
        subject.History.Do(new DepthStep(new[] { (x, y) }, DepthSide.Front, +1), subject.Depth);
        src.Items[item].RefreshDepth();
    }

    private static Func<Task<string?>> Picks(string? path) => () => Task.FromResult(path);

    private static Dictionary<string, byte[]> Entries(string igt)
    {
        using var zip = ZipFile.OpenRead(igt);
        return zip.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }, StringComparer.Ordinal);
    }

    [AvaloniaFact]
    public async Task Save_as_igt_writes_a_custom_ingredient_with_every_cell_and_its_depth()
    {
        var dir = Temp();
        var png = SheetOnDisk(dir, "crew.png", 3);
        var target = Path.Combine(dir, "crew.igt");
        using var src = PictureRiseSource.Open(png, new ImageBridge(), Picks(target));
        Paint(src, 1, 5, 5);
        Paint(src, 1, 5, 5);
        Assert.True(src.IsDirty);
        var painted = src.Open(src.Items[1]).Depth.ToDepthMap()!.ToBytes();

        await src.SaveAsync();

        using var read = await IngredientArchive.ReadAsync(target);
        Assert.Equal(LayerKind.Custom, read.Manifest.Kind);
        Assert.Null(read.Manifest.Colorization);
        Assert.Equal("crew", read.Manifest.Name);
        Assert.Equal(new[] { "cell-1", "cell-2", "cell-3" }, read.Manifest.Variants.Select(v => v.Id).ToArray());
        Assert.Equal(new[] { "Cell 1", "Cell 2", "Cell 3" }, read.Manifest.Variants.Select(v => v.Name).ToArray());
        Assert.All(read.Manifest.Variants, v => Assert.Equal(1, v.Weight));
        Assert.Equal(new[] { "cell-2" }, read.Depths.Keys.ToArray());          // paper cells carry no depth entry
        Assert.Equal(painted, read.Depths["cell-2"].ToBytes());
        Assert.False(src.IsDirty);                                              // every subject marked saved
        Assert.Contains("crew.igt", src.SaveResultNote);
    }

    [AvaloniaFact]
    public async Task Save_as_igt_for_one_picture_names_the_variant_after_the_file()
    {
        var dir = Temp();
        var png = PictureOnDisk(dir, "Red Gem.png");
        var target = Path.Combine(dir, "gem.igt");
        using var src = PictureRiseSource.Open(png, new ImageBridge(), Picks(target));
        Paint(src, 0, 3, 3);

        await src.SaveAsync();

        using var read = await IngredientArchive.ReadAsync(target);
        Assert.Equal("red-gem", read.Manifest.Id);
        Assert.Equal("Red Gem", read.Manifest.Name);
        var only = Assert.Single(read.Manifest.Variants);
        Assert.Equal("red-gem", only.Id);
        Assert.Equal("Red Gem", only.Name);
        Assert.Single(read.Depths);
    }

    [AvaloniaFact]
    public async Task A_canceled_save_dialog_writes_nothing_and_keeps_the_depth_dirty()
    {
        var dir = Temp();
        var png = SheetOnDisk(dir, "crew.png", 2);
        using var src = PictureRiseSource.Open(png, new ImageBridge(), Picks(null));
        Paint(src, 0, 5, 5);
        int filesBefore = Directory.GetFiles(dir).Length;

        await src.SaveAsync();

        Assert.True(src.IsDirty);
        Assert.Null(src.SaveResultNote);
        Assert.Equal(filesBefore, Directory.GetFiles(dir).Length);
    }

    [AvaloniaFact]
    public async Task Save_as_igt_is_byte_identical_twice()
    {
        var dir = Temp();
        var png = SheetOnDisk(dir, "crew.png", 3);
        var first = Path.Combine(dir, "a.igt");
        var second = Path.Combine(dir, "b.igt");
        var path = first;
        using var src = PictureRiseSource.Open(png, new ImageBridge(), () => Task.FromResult<string?>(path));
        Paint(src, 2, 6, 6);

        await src.SaveAsync();
        path = second;
        await src.SaveAsync();

        // Entry CONTENTS, not the zip's own bytes: each entry carries the time it was written.
        var a = Entries(first);
        var b = Entries(second);
        Assert.Equal(a.Keys.OrderBy(k => k, StringComparer.Ordinal), b.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var key in a.Keys) Assert.Equal(a[key], b[key]);
        Assert.Contains("depth/cell-3.dep", a.Keys);
    }

    [AvaloniaFact]
    public async Task The_saved_ingredient_rises_to_the_same_model_as_the_picture()
    {
        var dir = Temp();
        var png = SheetOnDisk(dir, "crew.png", 2);
        var target = Path.Combine(dir, "crew.igt");
        using var src = PictureRiseSource.Open(png, new ImageBridge(), Picks(target));
        Paint(src, 1, 5, 5);
        Paint(src, 1, 6, 5);
        Paint(src, 1, 6, 5);
        var cell = src.Open(src.Items[1]);
        var options = new Nfty.Model.RiseOptions();

        // The page's own rise: Riser over the finished art and its painted depth, written as the
        // export writes it.
        var fromPicture = Nfty.Model.ModelWriter.Write(
            Nfty.Model.Riser.TryBuild(cell.Art, cell.Depth.ToDepthMap(), options, "crew")!,
            cell.Art, options, "crew");

        await src.SaveAsync();
        using var read = await IngredientArchive.ReadAsync(target);
        var art = read.VariantImages["cell-2"];
        read.Depths.TryGetValue("cell-2", out var depth);
        var fromIgt = Nfty.Model.ModelWriter.Write(
            Nfty.Model.Riser.TryBuild(art, depth, options, "crew")!, art, options, "crew");

        Assert.Equal(fromPicture.Count, fromIgt.Count);
        for (int i = 0; i < fromPicture.Count; i++)
        {
            Assert.Equal(fromPicture[i].FileName, fromIgt[i].FileName);
            Assert.Equal(fromPicture[i].Bytes, fromIgt[i].Bytes);
        }
    }

    [AvaloniaFact]
    public async Task A_name_without_the_extension_gets_it()
    {
        var dir = Temp();
        var png = PictureOnDisk(dir, "gem.png");
        using var src = PictureRiseSource.Open(png, new ImageBridge(), Picks(Path.Combine(dir, "chosen")));

        await src.SaveAsync();

        Assert.True(File.Exists(Path.Combine(dir, "chosen.igt")));
    }

    [AvaloniaFact]
    public void A_source_with_no_way_to_ask_for_a_path_cannot_save()
    {
        var png = PictureOnDisk(Temp(), "gem.png");
        using var src = PictureRiseSource.Open(png, new ImageBridge());

        Assert.False(src.CanSave);
    }

    [AvaloniaFact]
    public async Task The_footer_button_and_status_name_the_ingredient_file()
    {
        var dir = Temp();
        var png = SheetOnDisk(dir, "crew.png", 2);
        var target = Path.Combine(dir, "crew.igt");
        var status = new StatusService();
        using var vm = new Nfty.App.ViewModels.RiseViewModel(
            PictureRiseSource.Open(png, new ImageBridge(), Picks(target)), new ImageBridge(),
            new FakeNav(), new FakeDialogs(), new FilePickerService(), status);
        var subject = vm.Source.Open(vm.Items[0]);
        subject.History.Do(new DepthStep(new[] { (5, 5) }, DepthSide.Front, +1), subject.Depth);
        vm.Items[0].RefreshDepth();

        Assert.Equal("Save as .igt…", vm.Source.SaveLabel);
        Assert.True(vm.CanSave);

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.False(vm.HasUnsavedDepth);
        Assert.Contains("crew.igt", status.Last);
    }
}
