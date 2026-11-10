using System.Collections;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nfty.App.Controls;

/// <summary>
/// What a file about to be written holds: its name and size over a hairline, then its parts as an aligned grid -
/// a glyph, the part's name, and its count on the right (<c>images/ … 100 files</c>). The one drawing of a
/// manifest, used by the Export card, the Share card and the Rise page's export rail.
/// </summary>
/// <remarks>
/// <para><b>Parts are Core's strings, read, not reworded.</b> <c>ExportPlan.Parts()</c> writes
/// <c>images/  100 files</c>, <c>set.json</c>, <c>3D models (100)</c>, <c>spritesheet.png  8 x 4 cells, 1 MB</c>; the
/// card splits each at its double space (or its trailing count in brackets) into a name and a detail, so the CLI and
/// the card still say the identical thing - only the layout differs. A part it cannot split is a name alone.</para>
/// <para><b>A grid, not a run.</b> The wrapped run it replaces broke between parts wherever the width ran out,
/// so the same plan drew differently on each screen and nothing lined up. Columns are stated
/// (<see cref="Columns"/>), every cell is one height, and a long name trims with the whole part on its tooltip.</para>
/// <para>Anything a host needs under the parts (the Export card's consequences, the Rise rail's problem line) goes
/// in <see cref="Footer"/>.</para>
/// </remarks>
public partial class PackageCard : Border
{
    /// <summary>The file's name: <c>chest-demo.set</c>.</summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PackageCard, string?>(nameof(Title));

    /// <summary>The size, as the plan words it.</summary>
    public static readonly StyledProperty<string?> SizeProperty = AvaloniaProperty.Register<PackageCard, string?>(nameof(Size));

    /// <summary>A note on the size, for its tooltip (what the figure counts).</summary>
    public static readonly StyledProperty<string?> SizeTipProperty = AvaloniaProperty.Register<PackageCard, string?>(nameof(SizeTip));

    /// <summary>Whether the size is being counted again (the last figure stays, fainter).</summary>
    public static readonly StyledProperty<bool> SizePendingProperty = AvaloniaProperty.Register<PackageCard, bool>(nameof(SizePending));

    /// <summary>The parts, as Core words them.</summary>
    public static readonly StyledProperty<IEnumerable?> PartsProperty = AvaloniaProperty.Register<PackageCard, IEnumerable?>(nameof(Parts));

    /// <summary>How many parts sit side by side.</summary>
    public static readonly StyledProperty<int> ColumnsProperty = AvaloniaProperty.Register<PackageCard, int>(nameof(Columns), 2);

    /// <summary>What sits under the parts, if anything.</summary>
    public static readonly StyledProperty<Control?> FooterProperty = AvaloniaProperty.Register<PackageCard, Control?>(nameof(Footer));

    /// <inheritdoc cref="TitleProperty"/>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <inheritdoc cref="SizeProperty"/>
    public string? Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <inheritdoc cref="SizeTipProperty"/>
    public string? SizeTip { get => GetValue(SizeTipProperty); set => SetValue(SizeTipProperty, value); }

    /// <inheritdoc cref="SizePendingProperty"/>
    public bool SizePending { get => GetValue(SizePendingProperty); set => SetValue(SizePendingProperty, value); }

    /// <inheritdoc cref="PartsProperty"/>
    public IEnumerable? Parts { get => GetValue(PartsProperty); set => SetValue(PartsProperty, value); }

    /// <inheritdoc cref="ColumnsProperty"/>
    public int Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }

    /// <inheritdoc cref="FooterProperty"/>
    public Control? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>One cell's height; every cell is this tall, so rows line up across columns and hosts.</summary>
    public const double CellHeight = 18;

    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _size = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly UniformGrid _grid = new();
    private readonly ContentControl _footer = new();
    private INotifyCollectionChanged? _watched;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Border);

    static PackageCard()
    {
        TitleProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        SizeProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        SizeTipProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        SizePendingProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        PartsProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.Watch());
        ColumnsProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.Rebuild());
        FooterProperty.Changed.AddClassHandler<PackageCard>((c, e) => c._footer.Content = e.NewValue);
    }

    /// <summary>Makes an empty card.</summary>
    public PackageCard()
    {
        Classes.Add("ship");
        Classes.Add("pkg");
        _title.Classes.Add("fn");
        _size.Classes.Add("part");

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        head.Children.Add(_title);
        Grid.SetColumn(_size, 1);
        head.Children.Add(_size);

        var rule = new Border();
        rule.Classes.Add("pkgrule");

        // Rows, not a stack: the footer takes the STAR row, so a host that pins the card's height (the Export card)
        // hands its footer a real height to scroll inside, rather than an infinite one.
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*") };
        layout.Children.Add(head);
        Grid.SetRow(rule, 1);
        layout.Children.Add(rule);
        Grid.SetRow(_grid, 2);
        layout.Children.Add(_grid);
        Grid.SetRow(_footer, 3);
        layout.Children.Add(_footer);
        Child = layout;
        Rebuild();
    }

    private void UpdateHeader()
    {
        _title.Text = Title;
        ToolTip.SetTip(_title, string.IsNullOrEmpty(Title) ? null : Title);
        _size.Text = Size;
        ToolTip.SetTip(_size, string.IsNullOrEmpty(SizeTip) ? null : SizeTip);
        _size.Classes.Set("pending", SizePending);
    }

    private void Watch()
    {
        if (_watched is not null) _watched.CollectionChanged -= OnPartsChanged;
        _watched = Parts as INotifyCollectionChanged;
        if (_watched is not null) _watched.CollectionChanged += OnPartsChanged;
        Rebuild();
    }

    private void OnPartsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _grid.Children.Clear();
        _grid.Columns = Math.Max(1, Columns);
        if (Parts is null) return;
        foreach (var item in Parts)
        {
            if (item?.ToString() is not { Length: > 0 } text) continue;
            _grid.Children.Add(Cell(text));
        }
    }

    private static Control Cell(string text)
    {
        var (name, detail) = Split(text);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, Height = CellHeight };
        grid.Classes.Add("pkgcell");
        ToolTip.SetTip(grid, text);

        var glyph = new Avalonia.Controls.Shapes.Path();
        glyph.Classes.Add("ico");
        glyph.Classes.Add("xs");
        if (GlyphOf(name) is { } key && Application.Current?.TryFindResource(key, out var geo) == true && geo is Geometry g)
            glyph.Data = g;
        grid.Children.Add(glyph);

        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis };
        label.Classes.Add("pkgname");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var count = new TextBlock { Text = detail };
        count.Classes.Add("pkgdetail");
        Grid.SetColumn(count, 2);
        grid.Children.Add(count);
        return grid;
    }

    /// <summary>Splits a part into its name and its detail, as Core words them.</summary>
    /// <param name="part">The part: <c>images/  100 files</c>, <c>3D models (100)</c>, <c>set.json</c>.</param>
    /// <returns>The name and the detail (empty when there is none).</returns>
    public static (string Name, string Detail) Split(string part)
    {
        int gap = part.IndexOf("  ", StringComparison.Ordinal);
        if (gap > 0) return (part[..gap].Trim(), part[gap..].Trim());
        var m = BracketCount().Match(part);
        if (m.Success) return (m.Groups[1].Value, m.Groups[2].Value);
        return (part.Trim(), "");
    }

    /// <summary>Which glyph a part draws: a folder for a folder, the Rise mark for models, the type mark for a book.</summary>
    /// <param name="name">The part's name.</param>
    /// <returns>The icon key, or null for a plain file.</returns>
    public static string? GlyphOf(string name) =>
        name.EndsWith('/') ? "IconFolderOpen"
        : name.StartsWith("3D models", StringComparison.Ordinal) ? "IconRise"
        : name.EndsWith(".cbk", StringComparison.OrdinalIgnoreCase) ? "IconTypeCookBook"
        : name.EndsWith(".set", StringComparison.OrdinalIgnoreCase) ? "IconTypeSet"
        : name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "IconGrid"
        : name.Contains('.', StringComparison.Ordinal) ? "IconSymbol"
        : null;   // a fact rather than a file ("pivot bottom-center"): no glyph, the column stays

    [GeneratedRegex(@"^(.*?)\s*\((\d[\d,]*)\)$")]
    private static partial Regex BracketCount();
}
