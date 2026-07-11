using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Tests;

public class RulesEngineTests
{
    private static Dictionary<string, string> Sel(params (string, string)[] xs) =>
        xs.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void Exclude_rule_blocks_forbidden_pair()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Exclude,
                new RuleTarget("body", "fox"),
                new[] { new RuleTarget("hat", "visor") }),
        };
        Assert.False(RulesEngine.IsLegal(Sel(("body", "fox"), ("hat", "visor")), rules));
        Assert.True(RulesEngine.IsLegal(Sel(("body", "fox"), ("hat", "none")), rules));
        Assert.True(RulesEngine.IsLegal(Sel(("body", "cat"), ("hat", "visor")), rules));
    }

    [Fact]
    public void Require_rule_forces_pair()
    {
        var rules = new[]
        {
            new IncompatibilityRule(RuleType.Require,
                new RuleTarget("body", "robot"),
                new[] { new RuleTarget("eyes", "chrome") }),
        };
        Assert.True(RulesEngine.IsLegal(Sel(("body", "robot"), ("eyes", "chrome")), rules));
        Assert.False(RulesEngine.IsLegal(Sel(("body", "robot"), ("eyes", "sleepy")), rules));
        Assert.True(RulesEngine.IsLegal(Sel(("body", "cat"), ("eyes", "sleepy")), rules));
    }
}
