namespace Nfty.App.Services;

/// <summary>File open/save dialogs. Phase-1 desktop impl returns null (no picker wired yet); the
/// commands that use it are stubs until Phase 2, so a null result is never dereferenced.</summary>
public interface IFilePickerService
{
    /// <summary>Shows an open dialog filtered to the given extensions.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="extensions">Accepted extensions, each including the leading dot.</param>
    /// <returns>The chosen path, or null if canceled.</returns>
    Task<string?> OpenFileAsync(string title, params string[] extensions);
    /// <summary>Shows a save dialog.</summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="defaultExtension">Extension applied when the user types none.</param>
    /// <returns>The chosen path, or null if canceled.</returns>
    Task<string?> SaveFileAsync(string title, string defaultExtension);
    /// <summary>Shows a folder picker.</summary>
    /// <param name="title">Dialog title.</param>
    /// <returns>The chosen folder, or null if canceled.</returns>
    Task<string?> PickFolderAsync(string title);
}

/// <summary>The null object used by the headless test host and any surface without a window.
/// Every call returns null, which every caller already treats as "the user canceled".</summary>
public sealed class FilePickerService : IFilePickerService
{
    /// <inheritdoc />
    public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
    /// <inheritdoc />
    public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
    /// <inheritdoc />
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
}
