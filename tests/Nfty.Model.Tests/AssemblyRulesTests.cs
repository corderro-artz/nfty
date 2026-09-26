using System.Reflection;
using System.Runtime.CompilerServices;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Model.Tests;

/// <summary>
/// Rules about the assembly as a whole, checked by reflection so a later file cannot quietly break
/// them.
/// </summary>
public class AssemblyRulesTests
{
    /// <summary>
    /// D7. The reference implementation cached a value lazily into a nullable field — a benign race
    /// while it ran on one thread, and not benign once a Set's models are built in parallel. The
    /// cheapest way to make "nothing is cached lazily" a property of the code rather than a habit:
    /// no field anywhere in this assembly can be written after construction. Compiler-generated
    /// closure types are exempt (their fields are the captured locals of one call); enums are exempt
    /// (their one instance field is the value).
    /// </summary>
    [Fact]
    public void No_field_in_the_assembly_can_be_written_after_construction()
    {
        var writable = typeof(Mesh).Assembly.GetTypes()
            .Where(t => !t.IsEnum && !IsCompilerGenerated(t))
            .SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                         | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToList();

        Assert.Empty(writable);
    }

    /// <summary>
    /// The arrow runs one way. A consumer that only generates a collection must never pull mesh code
    /// into its graph, which is the whole reason this is its own assembly.
    /// </summary>
    [Fact]
    public void Neither_the_engine_nor_the_roll_references_nfty_model()
    {
        foreach (var assembly in new[] { typeof(Opacity).Assembly, typeof(DepthMap).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name == "Nfty.Model");
    }

    private static bool IsCompilerGenerated(Type t) =>
        t.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || (t.DeclaringType is { } outer && IsCompilerGenerated(outer));
}
