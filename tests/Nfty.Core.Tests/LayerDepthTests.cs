using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Depth is a 1-based projection of <c>layerOrder</c>, bottom-first — not a stored field. Most of
/// what follows is mechanics; the last five are the ones that carry the design decision: that no
/// archive, identity or report changes meaning because of it.
/// </summary>
public class LayerDepthTests
{
    private static readonly Rgba32 Red = new(255, 0, 0, 255);
    private static readonly Rgba32 Blue = new(0, 0, 255, 255);
    private static readonly Rgba32 Clear = new(0, 0, 0, 0);

    // A recipe that is nothing but a stack of ids: everything LayerDepth reads, and nothing else.
    private static RecipeManifest Stack(params string[] ids) =>
        new("cat", "Cat", ids, Array.Empty<IncompatibilityRule>());

    // A custom layer whose every variant is the same solid 2x2 fill. Custom so no colorization is
    // rolled and the DNA is a function of the variant ids alone.
    private static LoadedIngredient Solid(string id, Rgba32 fill, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, fill)),
    };

    private static LoadedCookBook Book(params LoadedIngredient[] ings) => new()
    {
        Manifest = new CookBookManifest("cb", "Depth", new Dimensions(2, 2),
            new Collection("Depth", "d", "DPT"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", ings.Select(i => i.Manifest.Id).ToList(),
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = ings,
            },
        },
    };

    // ---- mechanics ----------------------------------------------------------------------------

    [Fact]
    public void Depth_is_dense_from_one_to_the_top()
    {
        var r = Stack("a", "b", "c");

        Assert.Equal(3, LayerDepth.Count(r));
        Assert.Equal(
            new[] { (Depth: 1, IngredientId: "a"), (Depth: 2, IngredientId: "b"), (Depth: 3, IngredientId: "c") },
            LayerDepth.Ordered(r));
        Assert.Equal(new[] { 1, 2, 3 }, new[] { "a", "b", "c" }.Select(id => LayerDepth.DepthOf(r, id)));
        Assert.Equal(new[] { "a", "b", "c" }, new[] { 1, 2, 3 }.Select(d => LayerDepth.At(r, d)));
    }

    [Fact]
    public void An_empty_stack_has_no_depths()
    {
        Assert.Equal(0, LayerDepth.Count(Stack()));
        Assert.Empty(LayerDepth.Ordered(Stack()));
    }

    [Fact]
    public void Move_to_shifts_the_layers_between()
    {
        // Down: 'd' from the top to depth 2 pushes b and c up one each, and 'a' does not move.
        Assert.Equal(new[] { "a", "d", "b", "c" },
            LayerDepth.MoveTo(Stack("a", "b", "c", "d"), "d", 2).LayerOrder);

        // Up: 'a' from the bottom to depth 3 pulls b and c down one each.
        Assert.Equal(new[] { "b", "c", "a", "d" },
            LayerDepth.MoveTo(Stack("a", "b", "c", "d"), "a", 3).LayerOrder);

        // And the requested depth is the depth it lands at, which is the whole contract.
        Assert.Equal(3, LayerDepth.DepthOf(LayerDepth.MoveTo(Stack("a", "b", "c", "d"), "a", 3), "a"));
    }

    [Fact]
    public void Move_to_clamps_instead_of_throwing_at_both_ends()
    {
        var r = Stack("a", "b", "c");

        Assert.Equal(new[] { "b", "c", "a" }, LayerDepth.MoveTo(r, "a", 99).LayerOrder);
        Assert.Equal(new[] { "c", "a", "b" }, LayerDepth.MoveTo(r, "c", -4).LayerOrder);
        // Already at the top and asked to go higher: a no-op, not an error.
        Assert.Equal(new[] { "a", "b", "c" }, LayerDepth.MoveTo(r, "c", int.MaxValue).LayerOrder);
    }

    [Fact]
    public void Move_by_walks_up_and_down_and_clamps_at_the_ends()
    {
        var r = Stack("a", "b", "c");

        Assert.Equal(new[] { "b", "a", "c" }, LayerDepth.MoveBy(r, "a", 1).LayerOrder);
        Assert.Equal(new[] { "a", "c", "b" }, LayerDepth.MoveBy(r, "c", -1).LayerOrder);
        Assert.Equal(new[] { "b", "c", "a" }, LayerDepth.MoveBy(r, "a", 2).LayerOrder);

        // A nudge off either end is a no-op, which is what lets a UI wire up/down buttons unguarded.
        Assert.Equal(new[] { "a", "b", "c" }, LayerDepth.MoveBy(r, "c", 1).LayerOrder);
        Assert.Equal(new[] { "a", "b", "c" }, LayerDepth.MoveBy(r, "a", -1).LayerOrder);

        // The target depth is widened before it is clamped: added to a depth as an int, int.MaxValue
        // wraps negative and would send a move to the TOP all the way to the bottom instead.
        Assert.Equal(new[] { "b", "c", "a" }, LayerDepth.MoveBy(r, "a", int.MaxValue).LayerOrder);
        Assert.Equal(new[] { "c", "a", "b" }, LayerDepth.MoveBy(r, "c", int.MinValue).LayerOrder);
    }

    [Fact]
    public void An_unknown_ingredient_is_a_key_not_found_naming_the_recipe_and_the_id()
    {
        var r = Stack("a", "b");

        var ex = Assert.Throws<KeyNotFoundException>(() => LayerDepth.DepthOf(r, "ghost"));
        Assert.Contains("ghost", ex.Message);
        Assert.Contains("cat", ex.Message);

        Assert.Throws<KeyNotFoundException>(() => LayerDepth.MoveTo(r, "ghost", 1));
        Assert.Throws<KeyNotFoundException>(() => LayerDepth.MoveBy(r, "ghost", 1));
    }

    [Fact]
    public void At_rejects_a_depth_outside_the_stack()
    {
        // Reading throws where moving clamps: "what is at depth 3?" of a two-layer stack is a bug,
        // whereas nudging the top layer upwards is an ordinary no-op.
        var r = Stack("a", "b");

        Assert.Throws<ArgumentOutOfRangeException>(() => LayerDepth.At(r, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => LayerDepth.At(r, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => LayerDepth.At(Stack(), 1));
    }

    [Fact]
    public void Every_move_preserves_the_exact_set_of_ids()
    {
        // The bijection between layerOrder and the ingredients is what Validator enforces, and a
        // move must not be able to break it: drop an id and that layer is never drawn again,
        // duplicate one and it is rolled twice per asset.
        var start = Stack("a", "b", "c", "d");
        var ids = new[] { "a", "b", "c", "d" };

        foreach (var id in ids)
            for (int depth = -2; depth <= 7; depth++)
            {
                var moved = LayerDepth.MoveTo(start, id, depth);

                Assert.Equal(ids, moved.LayerOrder.Order(StringComparer.Ordinal));
                Assert.Equal(4, moved.LayerOrder.Count);
                Assert.Equal(Math.Clamp(depth, 1, 4), LayerDepth.DepthOf(moved, id));
            }
    }

    [Fact]
    public void Moving_never_mutates_the_manifest_it_was_given()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude, new RuleTarget("a", "v"),
                new[] { new RuleTarget("b", "v") }),
        };
        var original = new RecipeManifest("cat", "Cat", new[] { "a", "b", "c" }, rules);

        var moved = LayerDepth.MoveTo(original, "a", 3);

        Assert.Equal(new[] { "a", "b", "c" }, original.LayerOrder);
        Assert.Equal(new[] { "b", "c", "a" }, moved.LayerOrder);
        Assert.NotSame(original.LayerOrder, moved.LayerOrder);

        // Everything that is not the order is carried through by reference, untouched.
        Assert.Equal(original.Id, moved.Id);
        Assert.Equal(original.Name, moved.Name);
        Assert.Same(original.Rules, moved.Rules);
        Assert.Equal(original.SchemaVersion, moved.SchemaVersion);
    }

    // ---- the five that carry the design ---------------------------------------------------------

    /// <summary>
    /// Identity is order-independent — <c>Dna.Compute</c> sorts its selections ordinally before
    /// hashing — so a reorder cannot invalidate a Set that has already been minted.
    ///
    /// <para><b>Read the construction before generalizing this.</b> It holds because the two
    /// reordered layers have ONE variant each, so no reorder can change what they roll. The general
    /// claim — "a reorder changes the pixels but not the DNA" — is FALSE, and
    /// <see cref="Reordering_layers_that_each_have_several_variants_reassigns_the_rolls"/> disproves
    /// it: <c>Generator.RollOne</c> walks the layers in <c>layerOrder</c> consuming one roll each, so
    /// moving a layer moves which draw reaches it. What is actually pinned here is the narrower fact
    /// the compatibility argument needs — a GIVEN selection hashes the same in any order.</para>
    /// </summary>
    [Fact]
    public void Reordering_the_stack_changes_the_pixels_but_not_the_dna()
    {
        // 'bg' and 'fg' have one variant each, so which draw of the RNG reaches them cannot change
        // what they roll — moving them past each other changes the paint order and nothing else.
        // 'deco' keeps its position (so it keeps its draw) and is fully transparent, so it supplies
        // the two-asset space without ever reaching a pixel.
        using var before = Book(
            Solid("bg", Red, "only"),
            Solid("fg", Blue, "only"),
            Solid("deco", Clear, "d1", "d2"));

        // Shares every image with `before`, which is why only `before` is disposed.
        var after = CookBookEdits.MoveLayer(before, "cat", "fg", 1);

        var opts = new GenerateOptions(2, "depth-seed");
        using var first = Generator.Generate(before, opts);
        using var second = Generator.Generate(after, opts);

        // Identical asset-for-asset, not merely as a set.
        Assert.Equal(first.Assets.Select(a => a.Dna), second.Assets.Select(a => a.Dna));
        Assert.Equal(
            first.Assets.Select(a => a.Dna).ToHashSet(StringComparer.Ordinal),
            second.Assets.Select(a => a.Dna).ToHashSet(StringComparer.Ordinal));

        // ...over different pixels: blue covered red, and now red covers blue.
        Assert.Equal(Blue, first.Assets[0].Image[0, 0]);
        Assert.Equal(Red, second.Assets[0].Image[0, 0]);
    }

    /// <summary>
    /// The load-bearing half of the test above, asserted directly rather than through a generation:
    /// the SAME selection hashes the same whatever order it arrives in, because <c>Dna.Compute</c>
    /// sorts ordinally before hashing. This is what makes depth safe to add — reordering a book
    /// cannot invalidate a Set already minted from it, and putting depth INTO the hash would
    /// invalidate every Set ever minted.
    /// </summary>
    [Fact]
    public void Dna_is_the_same_whichever_order_the_layers_are_hashed_in()
    {
        var bottomFirst = new[]
        {
            new LayerSelection("bg", "a", null, null, 1, 1),
            new LayerSelection("fg", "x", 200, 0.5, 10, 10),
        };
        var topFirst = new[] { bottomFirst[1], bottomFirst[0] };

        Assert.Equal(Dna.Compute("cat", bottomFirst), Dna.Compute("cat", topFirst));
    }

    /// <summary>
    /// The boundary of that invariance, pinned because it is easy to over-read. Identity is
    /// order-independent, but the SELECTION being hashed need not survive a reorder: <c>RollOne</c>
    /// walks <c>layerOrder</c> consuming one draw per layer, so moving a layer moves which draw
    /// reaches it. Where every layer has one variant that changes nothing (the test above); where
    /// layers have several, a regenerated collection is genuinely a different collection, not merely
    /// a repainted one. Seeded, like every other determinism test here.
    /// </summary>
    [Fact]
    public void Reordering_layers_that_each_have_several_variants_reassigns_the_rolls()
    {
        using var before = Book(Solid("bg", Red, "a", "b"), Solid("fg", Blue, "x", "y"));
        var after = CookBookEdits.MoveLayer(before, "cat", "fg", 1);

        var opts = new GenerateOptions(2, "probe-seed");
        using var first = Generator.Generate(before, opts);
        using var second = Generator.Generate(after, opts);

        Assert.NotEqual(first.Assets.Select(a => a.Dna), second.Assets.Select(a => a.Dna));

        // Both runs are still perfectly well-formed collections — the point is that they differ,
        // not that either is broken.
        Assert.Equal(2, first.Assets.Select(a => a.Dna).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, second.Assets.Select(a => a.Dna).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>Both reports walk <c>layerOrder</c> and are documented as format-invariant, so a book
    /// that was not reordered must render byte-identical text after this work as before it.</summary>
    [Fact]
    public void A_clamped_no_op_move_leaves_the_report_text_byte_identical()
    {
        using var book = Book(Solid("bg", Red, "only"), Solid("fg", Blue, "only"));
        string identity = IdentityReport.Render(book);
        string collection = CollectionReport.Render(book);

        // Depth 0 clamps to 1, which is where 'bg' already sits: a move that moves nothing.
        var unmoved = CookBookEdits.MoveLayer(book, "cat", "bg", 0);

        Assert.Equal(identity, IdentityReport.Render(unmoved));
        Assert.Equal(collection, CollectionReport.Render(unmoved));

        // And not vacuously so: the identity report follows layer order, so a REAL move moves the
        // text. Without this the assertions above would pass on a MoveLayer that did nothing at all.
        var moved = CookBookEdits.MoveLayer(book, "cat", "bg", 2);
        Assert.NotEqual(identity, IdentityReport.Render(moved));
    }

    /// <summary>The fixture an older build wrote still reads, and its depths project off the same
    /// layerOrder it already shipped. Never regenerate it: its whole value is that it predates this
    /// change. See tests/fixtures/README.md.</summary>
    [Fact]
    public void The_cookbook_fixture_still_reads_and_projects_its_depths()
    {
        using var book = CookBookArchive.Read(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "VaporPets.cbk"));
        var recipe = Assert.Single(book.Recipes).Manifest;

        Assert.Equal(new[] { "bg", "skin", "aura" }, recipe.LayerOrder);
        Assert.Equal(
            new[]
            {
                (Depth: 1, IngredientId: "bg"),
                (Depth: 2, IngredientId: "skin"),
                (Depth: 3, IngredientId: "aura"),
            },
            LayerDepth.Ordered(recipe));
        Assert.Equal(1, LayerDepth.DepthOf(recipe, "bg"));
        Assert.Equal(3, LayerDepth.DepthOf(recipe, "aura"));
        Assert.Equal("skin", LayerDepth.At(recipe, 2));
    }

    /// <summary>The whole compatibility argument rests on this one number: depth is a projection of a
    /// field that already ships, so no manifest gained a property and no version had to move. A
    /// bumped Current here would mean a stored depth crept in somewhere.</summary>
    [Fact]
    public void Schema_current_is_still_one() => Assert.Equal(1, Schema.Current);

    /// <summary>Depth is not decorative: it is the paint order, exactly.</summary>
    [Fact]
    public void Composite_order_follows_depth()
    {
        using var book = Book(Solid("bg", Red, "only"), Solid("fg", Blue, "only"));
        var moved = CookBookEdits.MoveLayer(book, "cat", "bg", 2);   // red to the top

        var opts = new GenerateOptions(1, "s");
        using var painted = Generator.Generate(book, opts);
        using var repainted = Generator.Generate(moved, opts);

        // Every pixel of the 2x2, not just one: a layer that failed to cover would show at a corner.
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            Assert.Equal(Blue, painted.Assets[0].Image[x, y]);
            Assert.Equal(Red, repainted.Assets[0].Image[x, y]);
        }
    }
}
