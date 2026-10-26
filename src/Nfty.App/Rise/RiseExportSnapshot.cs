using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Rise;

/// <summary>
/// Everything an export reads, taken on the UI thread before the export leaves it.
/// </summary>
/// <remarks>
/// <para><b>Why a snapshot.</b> An export is built off the UI thread while the page is still on
/// screen. Reading the page's subjects from there raced every stroke, undo and Set re-read, and
/// borrowed art a reroll or Back could dispose under the build. A snapshot owns what it hands out:
/// a loaded item's picture is CLONED and its depth copied here, so nothing the page later does can
/// reach the files being written.</para>
/// <para><b>Owned pictures, streamed.</b> An item the page never loaded (a Set asset nobody has
/// looked at) is not decoded here: its entry decodes on demand, with its depth worked out from
/// state the snapshot captured, and nothing it does writes back into the source. Every picture
/// <see cref="Open"/> hands out is the caller's to give back through <see cref="Release"/>, so a
/// batch of ten thousand holds one picture at a time.</para>
/// </remarks>
public sealed class RiseExportSnapshot : IDisposable
{
    private readonly Entry[] _entries;

    /// <summary>Creates a snapshot from its entries.</summary>
    /// <param name="entries">One per exported item, in the page's order.</param>
    internal RiseExportSnapshot(IReadOnlyList<Entry> entries) => _entries = entries.ToArray();

    /// <summary>How many items it holds.</summary>
    public int Count => _entries.Length;

    /// <summary>Hands over item <paramref name="index"/>'s picture and depth; safe on any thread. Each
    /// item may be opened once.</summary>
    /// <param name="index">The item.</param>
    /// <param name="baseName">The name the model is written under.</param>
    /// <returns>An input whose picture the caller now owns.</returns>
    public ModelInput Open(int index, string baseName)
    {
        var (art, depth, faces) = _entries[index].Take();
        return new ModelInput(baseName, art, depth, faces);
    }

    /// <summary>Gives back a picture <see cref="Open"/> handed out.</summary>
    /// <param name="input">The input.</param>
    public static void Release(ModelInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Art.Dispose();
        input.Faces?.Dispose();
    }

    /// <summary>Frees every picture no one opened.</summary>
    public void Dispose()
    {
        foreach (var e in _entries) e.Dispose();
    }

    /// <summary>A loaded item: a clone of its picture and face owners and a copy of its depth, taken now.</summary>
    /// <param name="art">The picture to clone.</param>
    /// <param name="depth">The depth, already copied out of the surface.</param>
    /// <param name="faces">The face owners to clone, or null for a single picture.</param>
    /// <returns>The entry.</returns>
    internal static Entry Frozen(Image<Rgba32> art, DepthMap? depth, StackFaces? faces = null) =>
        new FrozenEntry(art.Clone(), depth, faces?.Clone());

    /// <summary>An item not loaded: decoded on demand, by a function that must read only what it captured.</summary>
    /// <param name="load">Decodes the picture and works out its depth.</param>
    /// <returns>The entry.</returns>
    internal static Entry Lazy(Func<(Image<Rgba32> Art, DepthMap? Depth, StackFaces? Faces)> load) => new LazyEntry(load);

    /// <summary>One item's way of producing its picture.</summary>
    internal abstract class Entry : IDisposable
    {
        public abstract (Image<Rgba32> Art, DepthMap? Depth, StackFaces? Faces) Take();
        public virtual void Dispose() { }
    }

    private sealed class FrozenEntry(Image<Rgba32> art, DepthMap? depth, StackFaces? faces) : Entry
    {
        private Image<Rgba32>? _art = art;
        private StackFaces? _faces = faces;

        public override (Image<Rgba32> Art, DepthMap? Depth, StackFaces? Faces) Take()
        {
            var taken = Interlocked.Exchange(ref _art, null)
                ?? throw new InvalidOperationException("This item has already been exported from this snapshot.");
            return (taken, depth, Interlocked.Exchange(ref _faces, null));
        }

        public override void Dispose()
        {
            Interlocked.Exchange(ref _art, null)?.Dispose();
            Interlocked.Exchange(ref _faces, null)?.Dispose();
        }
    }

    private sealed class LazyEntry(Func<(Image<Rgba32> Art, DepthMap? Depth, StackFaces? Faces)> load) : Entry
    {
        public override (Image<Rgba32> Art, DepthMap? Depth, StackFaces? Faces) Take() => load();
    }
}
