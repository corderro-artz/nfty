using System.Numerics;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>Independent finite-space oracle: exact rational masses, explicit rule predicates, and
/// exhaustive selections verify both probabilities and distinct-DNA counts.</summary>
public class CoreProbabilityOracleTests
{
    private readonly record struct Fraction
    {
        private readonly BigInteger _n;
        private readonly BigInteger _d;
        public Fraction(BigInteger n, BigInteger d)
        {
            var gcd = BigInteger.GreatestCommonDivisor(n, d);
            _n = n / gcd; _d = d / gcd;
        }
        public bool Positive => _n > 0;
        public double Value => (double)_n / (double)_d;
        public static Fraction Zero => new(0, 1);
        public static Fraction operator +(Fraction a, Fraction b) => new(a._n * b._d + b._n * a._d, a._d * b._d);
        public static Fraction operator *(Fraction a, Fraction b) => new(a._n * b._n, a._d * b._d);
        public static Fraction operator /(Fraction a, Fraction b) => new(a._n * b._d, a._d * b._n);
    }

    [Fact]
    public void Weighted_optional_ruled_books_match_exact_enumeration_and_conserve_probability()
    {
        var random = new Random(817331);
        var absenceCases = new[] { 0, 1, 25, 50, 99, 100 };
        for (int sample = 0; sample < 72; sample++)
        {
            var recipes = new List<LoadedRecipe>();
            var weights = new Dictionary<string, double>(StringComparer.Ordinal);
            var expected = new List<(string Recipe, int A, int B, Fraction Mass)>();
            for (int r = 0; r < 2; r++)
            {
                string id = "r" + r;
                int recipeWeight = r == 1 && sample % 5 == 0 ? 0 : random.Next(1, 10);
                weights[id] = recipeWeight;
                int aw0 = random.Next(1, 10), aw1 = random.Next(1, 10);
                int bw0 = random.Next(1, 10), bw1 = random.Next(1, 10);
                int absentA = absenceCases[(sample + r) % absenceCases.Length];
                int absentB = absenceCases[(sample / 6 + r) % absenceCases.Length];
                if (absentA == 100 && absentB == 100) absentB = 50;
                bool ruleLayersReachable = absentA < 100 && absentB < 100;
                bool exclude = sample % 3 != 0 && ruleLayersReachable;
                bool require = sample % 4 != 0 && ruleLayersReachable;
                var rules = new List<IncompatibilityRule>();
                if (exclude) rules.Add(new(RuleType.Exclude, new("A", "a1"), [new("B", "b0")]));
                if (require) rules.Add(new(RuleType.Require, new("A", "a0"), [new("B", "b1")]));
                recipes.Add(new LoadedRecipe
                {
                    Manifest = new RecipeManifest(id, id, ["A", "B"], rules,
                        AbsentPercent: new Dictionary<string, double> { ["A"] = absentA, ["B"] = absentB }),
                    Ingredients = [Layer("A", "a", aw0, aw1), Layer("B", "b", bw0, bw1)],
                });
                // Deliberately do not use RulesEngine or any production outcome/probability helper.
                for (int a = -1; a < 2; a++)
                    for (int b = -1; b < 2; b++)
                    {
                        bool legal = !(exclude && a == 1 && b == 0) && !(require && a == 0 && b != 1);
                        var mass = legal
                            ? new Fraction(recipeWeight, 1) * Outcome(a, absentA, aw0, aw1) * Outcome(b, absentB, bw0, bw1)
                            : Fraction.Zero;
                        expected.Add((id, a, b, mass));
                    }
            }
            using var book = new LoadedCookBook
            {
                Manifest = new CookBookManifest("book", "Book", new Dimensions(1, 1),
                    new Collection("Book", "", "B"), weights),
                Recipes = recipes,
            };
            Assert.Empty(Validator.Validate(book));
            var normalizer = expected.Aggregate(Fraction.Zero, (sum, e) => sum + e.Mass);
            Assert.True(normalizer.Positive);
            var odds = SelectionOdds.Prepare(PeekedCookBook.Of(book));
            double observedTotal = 0;
            foreach (var e in expected)
            {
                var traits = new Dictionary<string, string>(StringComparer.Ordinal);
                var absent = new List<string>();
                if (e.A < 0) absent.Add("A"); else traits["A"] = "a" + e.A;
                if (e.B < 0) absent.Add("B"); else traits["B"] = "b" + e.B;
                var actual = odds.Of(e.Recipe, traits, absent);
                if (!e.Mass.Positive) { Assert.False(actual.IsKnown); continue; }
                Assert.Equal(SpaceCertainty.Exact, actual.Certainty);
                double wanted = (e.Mass / normalizer).Value;
                Assert.InRange(Math.Abs(actual.Probability - wanted), 0, 1e-13);
                Assert.InRange(actual.Probability, double.Epsilon, 1);
                observedTotal += actual.Probability;
            }
            Assert.InRange(Math.Abs(observedTotal - 1), 0, 1e-13);
            var count = UniqueSpace.Count(book);
            Assert.Equal(SpaceCertainty.Exact, count.Certainty);
            Assert.Equal(new BigInteger(expected.Count(e => e.Mass.Positive)), count.Total);
        }
    }

    private static Fraction Outcome(int value, int absent, int w0, int w1) => value < 0
        ? new Fraction(absent, 100)
        : new Fraction(100 - absent, 100) * new Fraction(value == 0 ? w0 : w1, w0 + w1);

    private static LoadedIngredient Layer(string id, string prefix, int w0, int w1) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            [new(prefix + "0", prefix + "0", w0), new(prefix + "1", prefix + "1", w1)]),
        VariantImages = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
        {
            [prefix + "0"] = new(1, 1, new Rgba32(10, 20, 30, 255)),
            [prefix + "1"] = new(1, 1, new Rgba32(40, 50, 60, 255)),
        },
    };
}
