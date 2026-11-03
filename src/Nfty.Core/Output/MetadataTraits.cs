using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Core.Output;

/// <summary>
/// Which traits an asset publishes, and in which SHAPE of metadata.
/// </summary>
/// <remarks>
/// <para><b>Shape 1</b> (what every Set cooked before this existed carries): one attribute per
/// present layer plus <c>Type</c>. A rolled color is invisible, and a layer the roll left out is
/// invisible too, so two assets differing only in a hue publish identical traits.</para>
///
/// <para><b>Shape 2</b> (new cooks): additionally a <c>"{Layer} Color"</c> attribute right after each
/// Dynamic or Static layer's own (named by <see cref="ColorName"/> from the UNROUNDED rolled color;
/// Custom layers carry none), and an explicit <see cref="Absent"/> value for each OPTIONAL layer the
/// roll left out. One function builds this list, so the OpenSea file, the rich file's rarity rows and
/// the collection rarity table cannot spell a trait two ways.</para>
///
/// <para><b>The shape is recorded</b> in <c>set.json</c> (<see cref="SetManifest.MetadataShape"/>,
/// absent = 1) so <c>extend</c> writes new items in the Set's own shape and never rewrites an
/// already-published file's attributes: a Set is one shape throughout, which keeps its rarity table
/// consistent. Neither names nor shapes ever enter the DNA.</para>
/// </remarks>
public static class MetadataTraits
{
    /// <summary>The value an optional layer is published under when the roll left it out.</summary>
    public const string Absent = "None";

    /// <summary>The shape every Set cooked before color traits carries; also what a missing field means.</summary>
    public const int LegacyShape = 1;

    /// <summary>The shape new cooks write: color traits and explicit <see cref="Absent"/>.</summary>
    public const int CurrentShape = 2;

    /// <summary>The attributes an asset publishes, in order, excluding the leading <c>Type</c>.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="shape">The Set's metadata shape.</param>
    /// <returns>One per present layer; in shape 2 also color traits and <see cref="Absent"/> entries.</returns>
    public static IReadOnlyList<MetadataAttribute> Attributes(GeneratedAsset asset, int shape)
    {
        var list = new List<MetadataAttribute>(asset.Traits.Count * (shape >= CurrentShape ? 2 : 1));
        foreach (var t in asset.Traits)
        {
            list.Add(new MetadataAttribute(t.IngredientName, t.VariantName));
            if (shape < CurrentShape) continue;

            var roll = asset.ColorRolls.FirstOrDefault(c => string.Equals(c.LayerId, t.IngredientId, StringComparison.Ordinal));
            if (roll is { Kind: LayerKind.Dynamic or LayerKind.Static, H: { } h, S: { } s })
                list.Add(new MetadataAttribute(ColorName.TraitTypeOf(t.IngredientName), ColorName.Of(h, s)));
        }

        // Only layers the recipe made optional can be absent, so this is exactly "optional layers the
        // roll left out"; a layer the recipe does not stack is never listed here.
        if (shape >= CurrentShape)
            foreach (var a in asset.AbsentLayers)
                list.Add(new MetadataAttribute(a.IngredientName, Absent));

        return list;
    }
}
