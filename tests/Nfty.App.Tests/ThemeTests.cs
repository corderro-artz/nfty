using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace Nfty.App.Tests;

public class ThemeTests
{
    private static Avalonia.Controls.ResourceDictionary LoadTokens()
    {
        var uri = new Uri("avares://Nfty.App/Themes/Tokens.axaml");
        return (Avalonia.Controls.ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(uri)!;
    }

    [AvaloniaFact]
    public void Tokens_expose_the_accent_brush_in_both_variants()
    {
        var dict = LoadTokens();
        Assert.True(dict.TryGetResource("AccentBrush", ThemeVariant.Light, out var light));
        Assert.True(dict.TryGetResource("AccentBrush", ThemeVariant.Dark, out var dark));
        Assert.IsAssignableFrom<IBrush>(light);
        Assert.IsAssignableFrom<IBrush>(dark);
    }
}
