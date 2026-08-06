using System.Collections;
using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>View-only helpers for count-driven visibility (e.g. an empty-state note next to an
/// <see cref="Avalonia.Controls.ItemsControl"/>). Kept out of the ViewModels — they stay pure data.</summary>
public static class CollectionConverters
{
    /// <summary>True when a bound collection has no items — for the empty-state panels, which a
    /// plain null check cannot distinguish from "loaded and empty".</summary>
    public static readonly IValueConverter IsEmpty =
        new FuncValueConverter<ICollection?, bool>(c => c is null || c.Count == 0);
}
