using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Nfty.App.ViewModels;

/// <summary>
/// Click-to-sort for one table. Shared so the three sortable tables in this app behave identically
/// rather than each inventing a rule.
///
/// <para><b>ONE RULE, NO EXCEPTIONS: the first click on a column sorts ascending, and clicking the
/// column that is already active reverses it.</b> Numeric columns are not special-cased into
/// descending-first, which several data tables do — it is more useful on a rarity column and it is
/// a second rule to learn, and a table whose direction depends on the type of the column you clicked
/// is exactly the "scattered and random" this replaced. The variant table's old half-implementation
/// sorted weight descending and name ascending with no way to reverse either.</para>
///
/// <para><b>Which tables get one is a judgement, not a default.</b> A table's row order either
/// belongs to the DATA or belongs to the READER, and only the second kind sorts. The Recipe's layer
/// table is the standing counter-example: its order IS <c>layerOrder</c>, which is the paint order,
/// and <c>Generator.RollOne</c> walks it consuming one RNG draw per layer — so a sorted view would
/// show a stack that is not the stack, beside a drag handle that writes the real one.</para>
/// </summary>
public partial class TableSort : ObservableObject
{
    private readonly string _default;
    private readonly Action? _changed;

    /// <summary>The column being sorted by. Never empty — a table always has an order.</summary>
    [ObservableProperty] private string _column;

    /// <summary>Whether the sort is reversed.</summary>
    [ObservableProperty] private bool _descending;

    /// <summary>Creates a sort, seated on the column a table opens in.</summary>
    /// <param name="defaultColumn">The column to sort by before anything is clicked. Clicking it
    /// twice returns to it ascending, so the opening view is always reachable again.</param>
    /// <param name="changed">Raised whenever the order changes. Required at the DECLARATION rather
    /// than left to the owner to remember, because the failure without it is silent: the list
    /// property re-evaluates correctly whenever anything reads it, so every ViewModel test passes
    /// and only the running app fails to repaint.</param>
    public TableSort(string defaultColumn, Action? changed = null)
    {
        _default = defaultColumn;
        _column = defaultColumn;
        _changed = changed;
    }

    partial void OnColumnChanged(string value) => _changed?.Invoke();
    partial void OnDescendingChanged(bool value) => _changed?.Invoke();

    /// <summary>Sorts by a column, or reverses it when it is already the active one.</summary>
    /// <param name="column">The column key, matching what the header passes.</param>
    [RelayCommand]
    public void By(string column)
    {
        if (string.IsNullOrEmpty(column)) return;
        if (string.Equals(column, Column, StringComparison.Ordinal)) Descending = !Descending;
        else { Column = column; Descending = false; }
    }

    /// <summary>Whether <paramref name="column"/> is the one being sorted by — what a header's arrow
    /// binds to, so the indicator is on exactly one column at a time.</summary>
    /// <param name="column">The column key to test.</param>
    /// <returns>True when it is the active column.</returns>
    public bool IsOn(string column) => string.Equals(column, Column, StringComparison.Ordinal);

    /// <summary>
    /// Orders a list by the active column.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="items">The rows, in their natural order.</param>
    /// <param name="key">Maps a row and a column key to the value to sort on. Returning null for an
    /// unknown column falls back to the natural order rather than throwing, because a column key is
    /// a string from markup and a typo there must not take the pane down.</param>
    /// <returns>A new ordered list; the input is not touched.</returns>
    public IReadOnlyList<T> Order<T>(IReadOnlyList<T> items, Func<T, string, object?> key)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(key);

        // OrderBy is STABLE, so rows with equal keys keep the order they came in — which is what
        // makes a sort on a coarse column (a trait name, a relation) still readable underneath.
        var ordered = Descending
            ? items.OrderByDescending(i => key(i, Column), SortKeyComparer.Instance)
            : items.OrderBy(i => key(i, Column), SortKeyComparer.Instance);
        return ordered.ToList();
    }

    /// <summary>Returns to the opening view.</summary>
    public void Reset() { Column = _default; Descending = false; }
}

/// <summary>
/// Compares the values <see cref="TableSort.Order"/> is handed. Strings compare ORDINALLY, like
/// every other sort in this product: a default string comparison sorts by the current culture, so
/// the same collection would list in a different order on a different machine — and these tables are
/// screenshotted into the manual.
/// </summary>
public sealed class SortKeyComparer : IComparer<object?>
{
    /// <summary>The shared instance.</summary>
    public static readonly SortKeyComparer Instance = new();

    private SortKeyComparer() { }

    /// <summary>Compares two sort keys.</summary>
    /// <param name="x">One key.</param>
    /// <param name="y">The other.</param>
    /// <returns>The usual -1 / 0 / 1.</returns>
    public int Compare(object? x, object? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        // Nulls sort first ascending, which puts "no value" at one end rather than scattered
        // through the middle. A null column key means "unknown column": every row returns null,
        // every comparison is 0, and the stable sort leaves the natural order alone.
        if (x is null) return -1;
        if (y is null) return 1;
        if (x is string sx && y is string sy) return string.CompareOrdinal(sx, sy);
        return Comparer<object>.Default.Compare(x, y);
    }
}
