using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Tests;

/// <summary>A Rise page on screen: the view in a window, the view model behind it, and whatever else
/// the source borrows (a Set), all released together.</summary>
internal sealed class RisePage : IDisposable
{
    private readonly List<IDisposable> _owned = new();

    internal RisePage(RiseViewModel vm, Window window, RiseView view, IEnumerable<IDisposable>? owned = null)
    {
        Vm = vm;
        Window = window;
        View = view;
        if (owned is not null) _owned.AddRange(owned);
    }

    internal RiseViewModel Vm { get; }
    internal Window Window { get; }
    internal RiseView View { get; }

    public void Dispose()
    {
        Window.Close();
        Vm.Dispose();
        foreach (var o in _owned) o.Dispose();
    }
}

/// <summary>Builds Rise pages over the three sources, from the same in-memory Chest book the sources'
/// own tests use. Shared by the layout tests and the captures, so a frame and an assertion look at the
/// same page.</summary>
internal static class RisePages
{
    internal const string Pass = "correct-horse-battery-staple";

    /// <summary>The page area the smallest window gives a page, derived and not typed.</summary>
    internal static Avalonia.Size MinArea => new(
        (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
        (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale);

    internal static RiseViewModel Vm(RiseSource source) =>
        new(source, new ImageBridge(), new FakeNav(), new FakeDialogs(), new FilePickerService(), new StatusService());

    /// <summary>Oak with a front/back pattern: the top half one thick, the bottom half three thick.</summary>
    internal static DepthMap PaintedOak()
    {
        var bytes = new byte[8 * 8 * 2];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int i = (y * 8 + x) * 2;
                bytes[i] = (byte)(y < 4 ? 1 : 2);        // front
                bytes[i + 1] = (byte)(x < 4 ? 0 : 1);    // back
            }
        return DepthMap.FromBytes(8, 8, bytes);
    }

    internal static RiseViewModel VariantVm(string variant = "oak", Func<bool>? isEditing = null,
        DepthMap? oakDepth = null, bool withPine = false)
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(oakDepth, withPine: withPine, withSecondGhost: true));
        return Vm(RiseFixtures.Variant(s, variant, isEditing ?? (() => true)));
    }

    internal static RiseViewModel RecipeVm()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(PaintedOak()));
        return Vm(new RecipeRiseSource(s.Current!, s.Current!.Recipes[0], 1, new ImageBridge()));
    }

    /// <summary>Twenty assets cooked beside their book, opened as a Set source.</summary>
    internal static (RiseViewModel Vm, IDisposable Owner) SetVm()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(PaintedOak()));
        var setDir = Path.Combine(Path.GetDirectoryName(path)!, "Chest Set");
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(60, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, setDir, pack: false);
        s.Dispose();
        var set = SetReader.Read(setDir);
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        return (Vm(src), set);
    }

    /// <summary>A sealed Set: viewable, and refusing every export.</summary>
    internal static (RiseViewModel Vm, IDisposable Owner) SealedSetVm()
    {
        var ids = new[] { "a", "b", "c", "d" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v, v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255))),
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "d", "CHST"), new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-seal-").FullName, "Chest Demo");
        using (var cooked = Generator.Generate(book, new GenerateOptions(4, "launch")))
            SetWriter.Write(cooked, dir, pack: false);
        var outDir = Directory.CreateTempSubdirectory("nfty-tin-").FullName;
        var result = SetExporter.Export(dir, outDir, ExportOptions.For(ExportPreset.SealedCritique), passphrase: Pass);
        var set = SealedSetReader.Open(result.Path, Pass);
        var src = new SetRiseSource(set, set.Items[0].Number, null, new ImageBridge());
        return (Vm(src), set);
    }

    /// <summary>Shows a page in a themed window of the given size and lets layout settle.</summary>
    internal static RisePage Show(RiseViewModel vm, double width, double height,
        ThemeVariant? theme = null, IDisposable? owner = null)
    {
        var view = new RiseView { DataContext = vm };
        var window = new Window { Content = view, Width = width, Height = height };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new RisePage(vm, window, view, owner is null ? null : new[] { owner });
    }
}
