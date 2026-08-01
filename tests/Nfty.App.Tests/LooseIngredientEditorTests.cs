using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class LooseIngredientEditorTests
{
    // Write a small .igt to a temp dir; return (path, freshly-read ingredient).
    private static (string path, LoadedIngredient ing) OnDiskIgt(LayerKind kind = LayerKind.Dynamic)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "aura.igt");
        var coloriz = kind == LayerKind.Custom ? null
            : new Colorization(ColorModel.Hsv, 12, 4, new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });
        var manifest = new IngredientManifest("aura", "Aura", kind, coloriz, new[] { new Variant("glow", "Glow", 1) });
        var images = new Dictionary<string, Image<Rgba32>> { ["glow"] = new(8, 8) };
        IngredientArchive.Write(path, manifest, images);
        foreach (var i in images.Values) i.Dispose();
        return (path, IngredientArchive.Read(path));
    }

    private static IngredientEditorViewModel LooseEditor(LoadedIngredient ing, string path)
    {
        var book = LooseWorkspace.WrapIngredient(ing);
        return new IngredientEditorViewModel(ing, book.Recipes[0], book, new ImageBridge(),
            new FakeNav(), new FakeNotYetWired(), new CookBookSession(), new FakeDialogs(), looseSavePath: path);
    }

    [AvaloniaFact]
    public async Task Loose_save_writes_the_painted_value_back_to_the_igt()
    {
        var (path, ing) = OnDiskIgt();
        try
        {
            var vm = LooseEditor(ing, path);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.CanSave);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.False(vm.IsDirty);
            Assert.False(File.Exists(path + ".tmp"));
            using var reread = IngredientArchive.Read(path);
            Assert.Equal(200, ValueMap.FromImage(reread.VariantImages["glow"]).GetValue(4, 4));
            vm.Dispose();   // disposes the owned synthetic book (→ ing)
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Loose_CanSave_needs_dirty_and_not_custom()
    {
        var (path, ing) = OnDiskIgt();
        try
        {
            var vm = LooseEditor(ing, path);
            Assert.False(vm.CanSave);                       // clean
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 30;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.CanSave);                        // dirty dynamic loose → enabled
            vm.Dispose();
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Loose_save_does_not_disturb_a_separately_open_cookbook_session()
    {
        // A real cookbook is open in the session (its own temp dir + .cbk).
        (var cbkPath, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var (igtPath, ing) = OnDiskIgt();
        try
        {
            var openBook = session.Current!;
            int changed = 0; session.Changed += () => changed++;

            // Open a loose .igt editor built with the SAME (live) session.
            var book = LooseWorkspace.WrapIngredient(ing);
            var vm = new IngredientEditorViewModel(ing, book.Recipes[0], book, new ImageBridge(),
                new FakeNav(), new FakeNotYetWired(), session, new FakeDialogs(), looseSavePath: igtPath);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 111;
            vm.ApplyToolStroke(new[] { (0, 0) });
            await vm.SaveCommand.ExecuteAsync(null);
            vm.Dispose();   // disposes only the loose wrapper book, not the session's cookbook

            Assert.Same(openBook, session.Current);   // session's book untouched
            Assert.Equal(0, changed);                 // no Changed raised
            Assert.True(openBook.Recipes[0].Ingredients[0].VariantImages.Values.First().Width > 0);  // not disposed
        }
        finally
        {
            session.Dispose();
            Directory.Delete(Path.GetDirectoryName(cbkPath)!, recursive: true);
            Directory.Delete(Path.GetDirectoryName(igtPath)!, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Loose_custom_cannot_save()
    {
        var (path, ing) = OnDiskIgt(LayerKind.Custom);
        try
        {
            var vm = LooseEditor(ing, path);
            vm.ApplyToolStroke(new[] { (0, 0) });           // dirty
            Assert.False(vm.CanSave);                       // custom blocked even when dirty
            vm.Dispose();
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
