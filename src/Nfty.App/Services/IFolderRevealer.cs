namespace Nfty.App.Services;

/// <summary>Opens the OS file manager at a path. Head-specific; a no-op stub is used off-desktop.</summary>
public interface IFolderRevealer { void Reveal(string path); }

public sealed class NoopFolderRevealer : IFolderRevealer { public void Reveal(string path) { } }
