using Avalonia.Headless.XUnit;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class VariantImageryTests
{
    private static LoadedIngredient Ing(LayerKind kind, Colorization? c) => new()
    {
        Manifest = new IngredientManifest("aura", "Aura", kind, c,
            new[] { new Variant("glow", "Glow", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["glow"] = new Image<Rgba32>(4, 4) },
    };

    private static Colorization Dyn() => new(ColorModel.Hsv, 12, 4,
        new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) });

    private static Colorization Fixed() => new(ColorModel.Hsv, 1, 1,
        new[] { new ColorEntry(1, null, "hex:d6249f") });

    [AvaloniaFact]
    public void Render_custom_returns_a_bitmap_of_the_value_map_size()
    {
        using var ing = Ing(LayerKind.Custom, null);
        var bmp = VariantImagery.Render(new ImageBridge(), ing, "glow");
        Assert.Equal(4, bmp.PixelSize.Width);
        bmp.Dispose();
    }

    [AvaloniaFact]
    public void Render_dynamic_is_stable_for_the_same_salt()
    {
        using var ing = Ing(LayerKind.Dynamic, Dyn());
        var a = VariantImagery.Render(new ImageBridge(), ing, "glow", salt: 0);
        var b = VariantImagery.Render(new ImageBridge(), ing, "glow", salt: 0);
        Assert.Equal(a.PixelSize, b.PixelSize);   // deterministic seed → same dims, no throw
        a.Dispose(); b.Dispose();
    }

    [AvaloniaFact]
    public void Colorways_dynamic_yields_the_requested_sample_count()
    {
        using var ing = Ing(LayerKind.Dynamic, Dyn());
        var swatches = VariantImagery.Colorways(new ImageBridge(), ing, samples: 6);
        Assert.Equal(6, swatches.Count);
        foreach (var s in swatches) s.Dispose();
    }

    [AvaloniaFact]
    public void Colorways_static_yields_one_swatch()
    {
        using var ing = Ing(LayerKind.Static, Fixed());
        var swatches = VariantImagery.Colorways(new ImageBridge(), ing, samples: 6);
        Assert.Single(swatches);
        foreach (var s in swatches) s.Dispose();
    }

    [AvaloniaFact]
    public void RenderWith_bad_fixed_colour_falls_back_instead_of_throwing()
    {
        using var map = new Image<Rgba32>(4, 4);
        var bmp = VariantImagery.RenderWith(new ImageBridge(), map, dynamic: false,
            0, 360, 40, 100, fixedColor: "not-a-colour");
        Assert.Equal(4, bmp.PixelSize.Width);   // fell back to the raw map, no exception
        bmp.Dispose();
    }
}
