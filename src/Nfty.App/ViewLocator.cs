using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Nfty.App.ViewModels;

namespace Nfty.App;

/// <summary>
/// Resolves a View for a ViewModel by convention: replace "ViewModel" with "View" in the full type
/// name (ViewModels namespace → Views namespace). Returns a labelled placeholder when no View exists,
/// so an unmapped VM is visible rather than blank.
/// </summary>
public class ViewLocator : IDataTemplate
{
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

    public bool Match(object? data) => data is ViewModelBase;
}
