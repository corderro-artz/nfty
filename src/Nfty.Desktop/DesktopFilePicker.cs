using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>Real file picker over Avalonia's StorageProvider. Head-specific: it needs the window's
/// TopLevel, so it is not headless-testable — verified by manual smoke.</summary>
public sealed class DesktopFilePicker : IFilePickerService
{
    /// <summary>Shows an open dialog filtered to <paramref name="extensions"/>.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="extensions">Accepted extensions, each including the leading dot.</param>
    /// <returns>The chosen path, or null if the user cancelled.</returns>
    public async Task<string?> OpenFileAsync(string title, params string[] extensions)
    {
        var top = TopLevel;
        if (top is null) return null;

        var filter = new FilePickerFileType("nfty")
        {
            Patterns = extensions.Select(e => "*" + (e.StartsWith('.') ? e : "." + e)).ToArray(),
        };
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { filter },
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Shows a save dialog defaulting to <paramref name="defaultExtension"/>.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="defaultExtension">Extension applied when the user types none, including the dot.</param>
    /// <returns>The chosen path, or null if the user cancelled.</returns>
    public async Task<string?> SaveFileAsync(string title, string defaultExtension)
    {
        var top = TopLevel;
        if (top is null) return null;

        var ext = defaultExtension.StartsWith('.') ? defaultExtension : "." + defaultExtension;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = ext.TrimStart('.'),
            FileTypeChoices = new[] { new FilePickerFileType("nfty") { Patterns = new[] { "*" + ext } } },
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>Shows a folder picker.</summary>
    /// <param name="title">Dialog title.</param>
    /// <returns>The chosen folder, or null if the user cancelled.</returns>
    public async Task<string?> PickFolderAsync(string title)
    {
        var top = TopLevel;
        if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private static TopLevel? TopLevel =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
