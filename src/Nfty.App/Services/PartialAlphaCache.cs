using System.Runtime.CompilerServices;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Services;

/// <summary>
/// Whether an ingredient carries a partially transparent pixel, remembered per decoded image.
/// </summary>
/// <remarks>
/// <para>The Explorer tree asks on every rebuild, including each keystroke of a layer reorder, which
/// <c>ApplyBook(revalidate: false)</c> exists to keep cheap. A reorder, a rename or a removed variant
/// builds a new graph out of the SAME decoded images, so the answer is keyed on the image's identity:
/// a rebuild that changes no pixels scans nothing. An edit that does change pixels arrives as a new
/// image (the editor and Rise write new ones), so it is scanned once and remembered too.</para>
/// <para>Weak by key, so a closed book's entries go with its images.</para>
/// </remarks>
internal static class PartialAlphaCache
{
    private static readonly ConditionalWeakTable<Image<Rgba32>, object> Known = new();
    private static readonly object Yes = new(), No = new();
    private static long _scans;

    /// <summary>How many pixel scans have actually run, for the test that proves a rebuild runs none.</summary>
    internal static long Scans => Interlocked.Read(ref _scans);

    /// <summary>Whether any variant of <paramref name="ingredient"/> has a partly transparent pixel.</summary>
    /// <param name="ingredient">The ingredient.</param>
    /// <returns>True when one does.</returns>
    internal static bool HasPartialAlpha(LoadedIngredient ingredient)
    {
        foreach (var image in ingredient.VariantImages.Values)
            if (HasPartialAlpha(image)) return true;
        return false;
    }

    /// <summary>
    /// The count <c>VoxelReport.Scan(book).Count(v =&gt; !v.IsClean)</c> would give, if and only if every
    /// image in the book has been looked at already. Scans nothing: when any image is unknown it says
    /// so and the caller scans (off the UI thread) instead.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="count">The number of variants carrying partial alpha, when this returns true.</param>
    /// <returns>True when the cache answered for every image.</returns>
    internal static bool TryCountPartialVariants(LoadedCookBook book, out int count)
    {
        count = 0;
        foreach (var recipe in book.Recipes)
        {
            var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
            foreach (var i in recipe.Ingredients) byId[i.Manifest.Id] = i;
            // Walked in the same order and with the same tolerance as VoxelReport, so the two agree.
            foreach (var id in recipe.Manifest.LayerOrder)
            {
                if (!byId.TryGetValue(id, out var ing)) continue;
                foreach (var v in ing.Manifest.Variants)
                {
                    if (!ing.VariantImages.TryGetValue(v.Id, out var image)) continue;
                    if (!Known.TryGetValue(image, out var known)) { count = 0; return false; }
                    if (ReferenceEquals(known, Yes)) count++;
                }
            }
        }
        return true;
    }

    private static bool HasPartialAlpha(Image<Rgba32> image)
    {
        if (Known.TryGetValue(image, out var known)) return ReferenceEquals(known, Yes);
        Interlocked.Increment(ref _scans);
        bool partial = Opacity.HasPartialAlpha(image);
        Known.AddOrUpdate(image, partial ? Yes : No);
        return partial;
    }
}
