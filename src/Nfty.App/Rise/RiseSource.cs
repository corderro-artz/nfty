using System.Globalization;
using Nfty.Model;

namespace Nfty.App.Rise;

/// <summary>What the Rise page is showing.</summary>
public enum RiseSourceKind
{
    /// <summary>The variants of one layer.</summary>
    Variant,

    /// <summary>One rolled recipe.</summary>
    Recipe,

    /// <summary>The assets of a cooked Set.</summary>
    Set,
}

/// <summary>A picture that cannot rise, and how many of its pixels are partially transparent.</summary>
/// <param name="Name">The item's name.</param>
/// <param name="CountText">Pixel count as text, e.g. "1 px".</param>
public sealed record PartialAlphaRow(string Name, string CountText);

/// <summary>
/// Where the Rise page's items come from. The page talks to this and nothing else, so a new kind of
/// source (a loose picture, a sheet) is a new subclass, not a change to an existing one.
/// </summary>
public abstract class RiseSource : IDisposable
{
    /// <summary>Which kind of source this is.</summary>
    public abstract RiseSourceKind Kind { get; }

    /// <summary>Left header text, e.g. "BODY · VARIANTS".</summary>
    public abstract string Kicker { get; }

    /// <summary>What is being risen, e.g. "Body / Oak"; used in messages and the leave card.</summary>
    public abstract string Title { get; }

    /// <summary>The rail's rows.</summary>
    public abstract IReadOnlyList<RiseItem> Items { get; }

    /// <summary>The row that opens on the canvas.</summary>
    public abstract RiseItem InitialItem { get; }

    /// <summary>Chips under the header.</summary>
    public abstract IReadOnlyList<string> Facts { get; }

    /// <summary>Whether the whole book is flat 2D art that cannot rise.</summary>
    public virtual bool BookIsTwoDOnly => false;

    /// <summary>Whether depth may be painted and saved.</summary>
    public virtual bool IsEditable => false;

    /// <summary>Editable, but the edit lock is closed: painting is scratch and Save writes nothing.</summary>
    public virtual bool IsLocked => false;

    /// <summary>Whether several items can be exported at once.</summary>
    public virtual bool CanBatch => false;

    /// <summary>Why nothing may be exported, or null.</summary>
    public virtual string? ExportRefusal => null;

    /// <summary>Stem of a batch export's folder or zip.</summary>
    public abstract string BatchStem { get; }

    /// <summary>Text of the read-only pill, or null for none.</summary>
    public virtual string? ReadOnlyPill => null;

    /// <summary>The explanation under the depth tools.</summary>
    public abstract string DepthNote { get; }

    /// <summary>What the status bar says when the page opens.</summary>
    public abstract string OpeningLine { get; }

    /// <summary>A note at the foot of the rail.</summary>
    public virtual string RailFootNote => "";

    /// <summary>Loaded items that cannot rise, with how many pixels are partial.</summary>
    public virtual IReadOnlyList<PartialAlphaRow> PartialAlphaRows =>
        Items.Where(i => i.Subject is { } s && s.Depth.PartialCount > 0)
            .Select(i => new PartialAlphaRow(i.Name,
                string.Create(CultureInfo.InvariantCulture, $"{i.Subject!.Depth.PartialCount:N0} px")))
            .ToList();

    /// <summary>Loads an item's subject if it is not loaded yet.</summary>
    /// <param name="item">One of <see cref="Items"/>.</param>
    /// <returns>The item's subject.</returns>
    public abstract RiseSubject Open(RiseItem item);

    /// <summary>What an export of these items should rise.</summary>
    /// <param name="items">The items to export.</param>
    /// <param name="baseNames">One file name stem per item.</param>
    /// <returns>One input per item; art and depth are borrowed from the subjects.</returns>
    public virtual IEnumerable<ModelInput> ExportInputs(IReadOnlyList<RiseItem> items, IReadOnlyList<string> baseNames)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var s = Open(items[i]);
            yield return new ModelInput(baseNames[i], s.Art, s.Depth.ToDepthMap());
        }
    }

    /// <summary>Writes the painted depth to where it came from.</summary>
    /// <returns>A task that completes when it is written.</returns>
    public virtual Task SaveAsync() => throw new NotSupportedException("This source is read-only.");

    /// <summary>Raised when something the page shows has changed.</summary>
    public event Action? Changed;

    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void RaiseChanged() => Changed?.Invoke();

    /// <inheritdoc />
    public virtual void Dispose()
    {
        foreach (var item in Items) item.Dispose();
    }
}
