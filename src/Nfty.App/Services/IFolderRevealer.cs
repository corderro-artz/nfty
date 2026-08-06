namespace Nfty.App.Services;

/// <summary>Opens the OS file manager at a path. Head-specific; a no-op stub is used off-desktop.</summary>
public interface IFolderRevealer
{
    /// <summary>Reveals a path.</summary>
    /// <param name="path">The file or folder to show.</param>
    void Reveal(string path);
}

/// <summary>The null object for surfaces with no file manager to reach.</summary>
public sealed class NoopFolderRevealer : IFolderRevealer
{
    /// <inheritdoc />
    public void Reveal(string path) { }
}
