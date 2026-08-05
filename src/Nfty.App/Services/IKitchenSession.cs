using Nfty.Core.Formats;
using Nfty.Core.Model;

namespace Nfty.App.Services;

/// <summary>
/// The open Kitchen — the workspace root the titlebar chip names, and the folder loose Recipes and
/// Ingredients are saved into.
///
/// At most one is open at a time, which is the mockup's own rule: the chip is "fixed for every item
/// below it; changes only when you close this Kitchen and open another". Nothing requires a Kitchen —
/// a CookBook opened from anywhere on disk works exactly as before — so this is null until one is
/// opened, and every consumer must treat that as normal rather than as an error.
/// </summary>
public interface IKitchenSession
{
    /// <summary>The open Kitchen, or null when working outside one.</summary>
    KitchenContents? Current { get; }

    /// <summary>Path to the open <c>.ktn</c>, or null.</summary>
    string? Path { get; }

    /// <summary>Raised when a Kitchen is opened, closed, or re-scanned.</summary>
    event Action? Changed;

    /// <summary>Opens a Kitchen, replacing any currently open one.</summary>
    void Open(string ktnPath);

    void Close();

    /// <summary>Re-reads the folder. Membership is discovered rather than recorded, so anything that
    /// writes into the workspace calls this instead of trying to keep a list in step.</summary>
    void Refresh();
}

public sealed class KitchenSession : IKitchenSession
{
    public KitchenContents? Current { get; private set; }
    public string? Path { get; private set; }
    public event Action? Changed;

    public void Open(string ktnPath)
    {
        Current = Kitchen.Open(ktnPath);
        Path = ktnPath;
        Changed?.Invoke();
    }

    public void Close()
    {
        Current = null;
        Path = null;
        Changed?.Invoke();
    }

    public void Refresh()
    {
        if (Path is null) return;
        Current = Kitchen.Open(Path);
        Changed?.Invoke();
    }
}
