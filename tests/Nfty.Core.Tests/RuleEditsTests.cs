using Nfty.Core.Editing;
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

/// <summary>
/// Rule authoring. Before <see cref="RuleEdits"/> a rule could not be created or edited anywhere in
/// the product, so these are the first tests of writing one at all — and the ones that matter most
/// assert what the class REFUSES, since every refusal here is a rule that would have looked fine in
/// the manifest and done something the author did not ask for.
/// </summary>
public class RuleEditsTests
{
    private static RuleTarget T(string ing, string v) => new(ing, v);

    private static IncompatibilityRule Rule(RuleType type, string whenIng, string whenVar,
        params (string ing, string v)[] targets) =>
        new(type, T(whenIng, whenVar), targets.Select(t => T(t.ing, t.v)).ToArray());

    private static RecipeManifest Recipe(params IncompatibilityRule[] rules) =>
        new("cat", "Cat", new[] { "bg", "aura", "hat" }, rules);

    [Fact]
    public void Adding_a_rule_leaves_the_original_manifest_untouched()
    {
        var before = Recipe();
        var after = RuleEdits.Add(before, Rule(RuleType.Exclude, "bg", "day", ("aura", "none")));

        Assert.Empty(before.Rules);          // pure: the caller can still hold the old one for undo
        Assert.Single(after.Rules);
        Assert.Equal("cat", after.Id);       // and nothing else about the recipe moved
        Assert.Equal(before.LayerOrder, after.LayerOrder);
    }

    [Fact]
    public void A_rule_that_already_exists_is_refused_however_its_targets_are_ordered()
    {
        var recipe = RuleEdits.Add(Recipe(),
            Rule(RuleType.Exclude, "bg", "day", ("aura", "none"), ("hat", "crown")));

        // Targets are a conjunction, so the same two written the other way round is the SAME rule.
        // Duplicate detection that compared lists positionally would let this through.
        var flipped = Rule(RuleType.Exclude, "bg", "day", ("hat", "crown"), ("aura", "none"));
        Assert.True(RuleEdits.AreSame(recipe.Rules[0], flipped));

        var ex = Assert.Throws<ArgumentException>(() => RuleEdits.Add(recipe, flipped));
        Assert.Contains("already carries this rule", ex.Message);
    }

    [Fact]
    public void The_same_pair_under_the_other_type_is_a_different_rule_and_is_allowed()
    {
        var recipe = RuleEdits.Add(Recipe(), Rule(RuleType.Exclude, "bg", "day", ("aura", "none")));
        // Allowed to WRITE — it is Validator's job to notice the contradiction across the pair, and
        // it does. Add only refuses an exact repeat.
        var both = RuleEdits.Add(recipe, Rule(RuleType.Require, "bg", "day", ("aura", "none")));
        Assert.Equal(2, both.Rules.Count);
    }

    [Fact]
    public void A_rule_with_no_targets_is_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleEdits.Add(Recipe(), Rule(RuleType.Exclude, "bg", "day")));
        Assert.Contains("at least one target", ex.Message);
    }

    [Theory]
    [InlineData(RuleType.Exclude, "day")]     // bans bg:day from the entire collection
    [InlineData(RuleType.Exclude, "night")]   // can never fire
    [InlineData(RuleType.Require, "day")]     // tautology
    [InlineData(RuleType.Require, "night")]   // makes bg:day unrollable
    public void A_rule_pointing_a_layer_at_itself_is_refused(RuleType type, string targetVariant)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleEdits.Add(Recipe(), Rule(type, "bg", "day", ("bg", targetVariant))));
        Assert.Contains("against itself", ex.Message);
    }

    /// <summary>The refusal above is not a matter of taste: each of those four shapes changes what
    /// the engine will roll, and two of them silently delete a variant from the space.</summary>
    [Fact]
    public void The_self_rule_this_refuses_really_would_ban_a_variant()
    {
        var banned = new[] { Rule(RuleType.Exclude, "bg", "day", ("bg", "day")) };
        Assert.False(RulesEngine.IsLegal(
            new Dictionary<string, string> { ["bg"] = "day" }, banned));
        Assert.True(RulesEngine.IsLegal(
            new Dictionary<string, string> { ["bg"] = "night" }, banned));
    }

    [Fact]
    public void A_target_listed_twice_in_one_rule_is_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RuleEdits.Add(Recipe(), Rule(RuleType.Exclude, "bg", "day",
                ("aura", "none"), ("aura", "none"))));
        Assert.Contains("listed twice", ex.Message);
    }

    [Fact]
    public void Replacing_a_rule_keeps_every_other_rule_where_it_was()
    {
        var recipe = Recipe(
            Rule(RuleType.Exclude, "bg", "day", ("aura", "none")),
            Rule(RuleType.Require, "bg", "night", ("aura", "glow")),
            Rule(RuleType.Exclude, "hat", "crown", ("aura", "glow")));

        var after = RuleEdits.ReplaceAt(recipe, 1,
            Rule(RuleType.Require, "bg", "night", ("hat", "crown")));

        Assert.Equal(3, after.Rules.Count);
        Assert.Equal(recipe.Rules[0], after.Rules[0]);
        Assert.Equal(recipe.Rules[2], after.Rules[2]);
        Assert.Equal("crown", after.Rules[1].Targets[0].VariantId);
    }

    [Fact]
    public void Replacing_a_rule_with_itself_is_allowed_but_duplicating_another_is_not()
    {
        var recipe = Recipe(
            Rule(RuleType.Exclude, "bg", "day", ("aura", "none")),
            Rule(RuleType.Require, "bg", "night", ("aura", "glow")));

        // An edit that changes nothing must not be refused as "a duplicate of itself".
        var same = RuleEdits.ReplaceAt(recipe, 0, Rule(RuleType.Exclude, "bg", "day", ("aura", "none")));
        Assert.Equal(2, same.Rules.Count);

        var ex = Assert.Throws<ArgumentException>(() =>
            RuleEdits.ReplaceAt(recipe, 0, Rule(RuleType.Require, "bg", "night", ("aura", "glow"))));
        Assert.Contains("position 2", ex.Message);
    }

    [Fact]
    public void Removing_a_rule_shifts_the_ones_after_it_down()
    {
        var recipe = Recipe(
            Rule(RuleType.Exclude, "bg", "day", ("aura", "none")),
            Rule(RuleType.Require, "bg", "night", ("aura", "glow")),
            Rule(RuleType.Exclude, "hat", "crown", ("aura", "glow")));

        var after = RuleEdits.RemoveAt(recipe, 0);
        Assert.Equal(2, after.Rules.Count);
        Assert.Equal(recipe.Rules[1], after.Rules[0]);   // index 1 is now index 0
        Assert.Equal(recipe.Rules[2], after.Rules[1]);
        Assert.Equal(3, recipe.Rules.Count);             // and the original is untouched
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void An_index_no_rule_lives_at_is_refused_with_the_count(int index)
    {
        var recipe = Recipe(
            Rule(RuleType.Exclude, "bg", "day", ("aura", "none")),
            Rule(RuleType.Require, "bg", "night", ("aura", "glow")));

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => RuleEdits.RemoveAt(recipe, index));
        Assert.Contains("2 rule(s)", ex.Message);
    }
}
