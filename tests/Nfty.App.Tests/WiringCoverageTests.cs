using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Every command a ViewModel exposes must be reachable from the UI.
///
/// <para>This file used to assert something much weaker — that a ViewModel <em>had</em> a property of
/// type <see cref="ICommand"/> — which cannot detect an unwired command and so could not fail for
/// the thing its name promises. It asserted <c>EnlargePreviewCommand</c> while that command was
/// bound in no view at all, and stayed green. Four features shipped unreachable behind it: the
/// theme toggle, the report dialog's Copy, closing a CookBook, and enlarging the editor preview.
/// A ViewModel is not a user interface; having the command is not offering it.</para>
///
/// <para>So the expected set is derived from the markup, the same way
/// <c>ThemeResourceTests.Every_dynamic_resource_the_markup_references_resolves_in_both_themes</c>
/// derives its key list: a hand-written list can only contain what someone remembered, and what was
/// forgotten is exactly what breaks.</para>
/// </summary>
public class WiringCoverageTests
{
    /// <summary>Commands deliberately not bound in markup, each with the reason it is exempt. Adding
    /// a name here is a decision to be defended, not a way to silence the test.</summary>
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        // Nothing is currently exempt. Kept so the mechanism, and the standard for using it, are
        // visible at the point where someone will be tempted to reach for it.
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// Every way a command can be invoked from the shipped UI: a <c>Command="{Binding X}"</c> in any
    /// form the codebase uses (including the <c>$parent[…]</c> and <c>#element</c> variants), a
    /// <c>KeyBinding</c> gesture, and a direct <c>.Execute(</c> from code-behind.
    /// </summary>
    private static HashSet<string> CommandsReachableFromTheUi()
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in new[] { "Nfty.App", "Nfty.Desktop" })
        {
            var root = Path.Combine(RepoRoot(), "src", dir);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
            {
                // Comments first: a commented-out binding is not a binding, and prose about a
                // command would otherwise count as wiring it.
                var markup = Regex.Replace(File.ReadAllText(file), "<!--.*?-->", "", RegexOptions.Singleline);
                foreach (Match m in Regex.Matches(markup,
                    @"Command\s*=\s*""\{\s*(?:Compiled)?Binding\s+(?:[^}""]*?[\].#]\s*)?([A-Za-z0-9_]+Command)"))
                    found.Add(m.Groups[1].Value);
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"([A-Za-z0-9_]+Command)\s*\.\s*Execute\s*\("))
                    found.Add(m.Groups[1].Value);
        }
        return found;
    }

    private static IEnumerable<(Type Vm, string Command)> EveryDeclaredCommand() =>
        typeof(ViewModelBase).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true } && t.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => typeof(ICommand).IsAssignableFrom(p.PropertyType))
                .Select(p => (Vm: t, Command: p.Name)));

    [Fact]
    public void Every_command_a_view_model_exposes_is_reachable_from_the_ui()
    {
        var reachable = CommandsReachableFromTheUi();

        // A regex that matched nothing would make this vacuously green — the precise failure this
        // file exists to stop repeating.
        Assert.True(reachable.Count > 10,
            $"only found {reachable.Count} bound commands in the markup; the scan is probably broken");

        var unwired = EveryDeclaredCommand()
            .Where(x => !reachable.Contains(x.Command) && !Exempt.ContainsKey(x.Command))
            .Select(x => $"{x.Vm.Name}.{x.Command}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(unwired.Count == 0,
            "These commands exist on a ViewModel but nothing in the UI can invoke them — no binding, "
            + "no KeyBinding, no code-behind call. Either wire them, or add them to Exempt with a "
            + "reason:\n  " + string.Join("\n  ", unwired));
    }

    /// <summary>The exemption list must not outlive its reasons: a name left there after the command
    /// is wired (or deleted) quietly re-opens the hole this test closes.</summary>
    [Fact]
    public void No_exemption_is_stale()
    {
        var reachable = CommandsReachableFromTheUi();
        var declared = EveryDeclaredCommand().Select(x => x.Command).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, reason) in Exempt)
        {
            Assert.True(declared.Contains(name),
                $"'{name}' is exempt but no ViewModel declares it any more — drop the exemption. ({reason})");
            Assert.False(reachable.Contains(name),
                $"'{name}' is exempt but IS bound in the UI now — drop the exemption. ({reason})");
        }
    }
}
