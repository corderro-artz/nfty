using Avalonia;
using Avalonia.Styling;

namespace Nfty.App.Services;

/// <summary>Switches the application between the light and dark theme dictionaries.</summary>
public interface IThemeService
{
    /// <summary>Whether the dark theme is active.</summary>
    bool IsDark { get; }
    /// <summary>Switches to the other theme.</summary>
    void Toggle();
}

/// <inheritdoc cref="IThemeService"/>
public sealed class ThemeService : IThemeService
{
    /// <inheritdoc />
    public bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    /// <inheritdoc />
    public void Toggle()
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = IsDark ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
