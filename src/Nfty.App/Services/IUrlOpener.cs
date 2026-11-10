namespace Nfty.App.Services;

/// <summary>Opens an https address in the person's browser. Head-specific; a recording stub is used off-desktop.</summary>
public interface IUrlOpener
{
    /// <summary>Opens an address.</summary>
    /// <param name="uri">An absolute https address.</param>
    void Open(Uri uri);
}

/// <summary>The null object for surfaces with no browser to reach; remembers what it was asked to open.</summary>
public sealed class NoopUrlOpener : IUrlOpener
{
    /// <summary>The last address asked for, so a test can assert it.</summary>
    public Uri? Last { get; private set; }

    /// <inheritdoc />
    public void Open(Uri uri) => Last = uri;
}
