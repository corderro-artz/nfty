namespace Nfty.App.Services;

/// <summary>File open/save dialogs. Phase-1 desktop impl returns null (no picker wired yet); the
/// commands that use it are stubs until Phase 2, so a null result is never dereferenced.</summary>
public interface IFilePickerService
{
    Task<string?> OpenFileAsync(string title, params string[] extensions);
    Task<string?> SaveFileAsync(string title, string defaultExtension);
    Task<string?> PickFolderAsync(string title);
}

public sealed class FilePickerService : IFilePickerService
{
    public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
    public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
}
