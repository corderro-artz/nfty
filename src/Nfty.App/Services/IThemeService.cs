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

    /// <summary>Applies a remembered choice. A no-op by default, for stand-ins with no application.</summary>
    /// <param name="choice">Follow the system, or one theme always.</param>
    void Apply(ThemeChoice choice) { }
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

    /// <inheritdoc />
    public void Apply(ThemeChoice choice)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = choice switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }
}
