using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Nfty.App.ViewModels;

namespace Nfty.App;

/// <summary>
/// Resolves a View for a ViewModel by convention: replace "ViewModel" with "View" in the full type
/// name (ViewModels namespace → Views namespace). Returns a labeled placeholder when no View exists,
/// so an unmapped VM is visible rather than blank.
/// </summary>
public class ViewLocator : IDataTemplate
{
    /// <summary>Resolves a ViewModel to its View by name convention.</summary>
    /// <param name="data">The ViewModel.</param>
    /// <returns>The matching view, or a placeholder naming the type it could not resolve.</returns>
    public Control Build(object? data)
    {
        if (data is null) return new TextBlock { Text = "No data" };
        var name = data.GetType().FullName!
            .Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = $"View not found: {name}" };
    }

    /// <summary>Whether this locator handles the object.</summary>
    /// <param name="data">The candidate.</param>
    /// <returns>True for anything deriving from <see cref="ViewModels.ViewModelBase"/>.</returns>
    public bool Match(object? data) => data is ViewModelBase;
}
