using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Tests;

/// <summary>Throwaway: one side of a rule.</summary>
public record ZSide(string Ingredient, string Variant);

/// <summary>Throwaway: one rule.</summary>
public record ZRule(bool IsExclude, ZSide A, IReadOnlyList<ZSide> Targets)
{
    /// <summary>Throwaway.</summary>
    public string ConnectorText => IsExclude ? "never with" : "always with";
    /// <summary>Throwaway.</summary>
    public string ShortText => IsExclude ? "never" : "always";
}

/// <summary>Throwaway: the panel's whole data.</summary>
public class ZRuleBag
{
    /// <summary>Throwaway.</summary>
    public IReadOnlyList<ZRule> All { get; }
    /// <summary>Throwaway.</summary>
    public IReadOnlyList<ZRule> Excludes => All.Where(r => r.IsExclude).ToList();
    /// <summary>Throwaway.</summary>
    public IReadOnlyList<ZRule> Requires => All.Where(r => !r.IsExclude).ToList();
    /// <summary>Throwaway.</summary>
    public bool HasExcludes => Excludes.Count > 0;
    /// <summary>Throwaway.</summary>
    public bool HasRequires => Requires.Count > 0;
    /// <summary>Throwaway.</summary>
    public string CountText => All.Count.ToString();
    /// <summary>Throwaway.</summary>
    public string ExcludeCount => Excludes.Count.ToString();
    /// <summary>Throwaway.</summary>
    public string RequireCount => Requires.Count.ToString();

    /// <summary>Throwaway.</summary>
    public ZRuleBag(IReadOnlyList<ZRule> all) => All = all;
}

/// <summary>Throwaway design comparison.</summary>
public partial class ZRulesDesigns : UserControl
{
    /// <summary>Throwaway.</summary>
    public ZRulesDesigns() => AvaloniaXamlLoader.Load(this);
}
