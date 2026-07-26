using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>Real file picker over Avalonia's StorageProvider. Head-specific: it needs the window's
/// TopLevel. Save is not exercised this slice, so it stays a null stub.</summary>
public sealed class DesktopFilePicker : IFilePickerService
{
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

    public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);

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
