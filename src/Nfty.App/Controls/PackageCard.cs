using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nfty.App.Controls;

/// <summary>
/// What a file about to be written holds: its name and size, then its parts as TILES - a glyph, the part's name, its
/// count as a badge (<c>images/ · 100 files</c>). The one drawing of a manifest, used by the Export card, the Share card
/// and the Rise page's export rail.
/// </summary>
/// <remarks>
/// <para><b>Parts are Core's strings, read, not reworded.</b> <c>ExportPlan.Parts()</c> writes
/// <c>images/  100 files</c>, <c>set.json</c>, <c>3D models (100)</c>, <c>spritesheet.png  8 x 4 cells, 1 MB</c>; the
/// card splits each at its double space (or its trailing count in brackets) into a name and a detail, so the CLI and
/// the card still say the identical thing - only the layout differs. A part it cannot split is a name alone.</para>
/// <para><b>The width decides the columns; the host decides the rows.</b> Tiles are never narrower than
/// <see cref="MinTileWidth"/>, and as many sit side by side as fit, so the same card is two columns in a narrow rail
/// and five in a wide window, every tile one height, organized the same way. A host short of height sets
/// <see cref="MaxRows"/>: the card is then exactly that many rows tall whichever page shows, and the header carries the
/// app's pager (<c>1/2 ‹ ›</c>, present and disabled at one page, never absent). <c>0</c> shows every part.</para>
/// <para>Anything a host needs under the parts (the Export card's consequences, the Rise rail's problem line) goes
/// in <see cref="Footer"/>, which takes the remaining height.</para>
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

    /// <summary>The narrowest a tile may be; the width fits as many columns as this allows.</summary>
    public static readonly StyledProperty<double> MinTileWidthProperty =
        AvaloniaProperty.Register<PackageCard, double>(nameof(MinTileWidth), 160);

    /// <summary>The most rows shown at once; more parts than fit page behind the header's pager. 0 shows every part.</summary>
    public static readonly StyledProperty<int> MaxRowsProperty = AvaloniaProperty.Register<PackageCard, int>(nameof(MaxRows));

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

    /// <inheritdoc cref="MinTileWidthProperty"/>
    public double MinTileWidth { get => GetValue(MinTileWidthProperty); set => SetValue(MinTileWidthProperty, value); }

    /// <inheritdoc cref="MaxRowsProperty"/>
    public int MaxRows { get => GetValue(MaxRowsProperty); set => SetValue(MaxRowsProperty, value); }

    /// <inheritdoc cref="FooterProperty"/>
    public Control? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    /// <summary>One tile's height; every tile is this tall, so rows line up across columns and hosts.</summary>
    public const double TileHeight = 20;

    /// <summary>The gap between tiles, both ways.</summary>
    public const double TileGap = 5;

    // Title and size BOTTOM-aligned: two sizes centered in the pager-tall row miss one baseline by 2px.
    private readonly TextBlock _title = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly TextBlock _size = new() { VerticalAlignment = VerticalAlignment.Bottom };
    private readonly StackPanel _pager = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _prev = new() { Content = "‹" };
    private readonly Button _next = new() { Content = "›" };
    private readonly TilePanel _tiles = new();
    private readonly ContentControl _footer = new();
    private INotifyCollectionChanged? _watched;
    private int _page;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Border);

    static PackageCard()
    {
        TitleProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        SizeProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        SizeTipProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        SizePendingProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.UpdateHeader());
        PartsProperty.Changed.AddClassHandler<PackageCard>((c, _) => c.Watch());
        MinTileWidthProperty.Changed.AddClassHandler<PackageCard>((c, _) => c._tiles.MinTileWidth = c.MinTileWidth);
        MaxRowsProperty.Changed.AddClassHandler<PackageCard>((c, _) =>
        {
            c._tiles.MaxRows = c.MaxRows;
            c._pager.IsVisible = c.MaxRows > 0;   // a property of the host, set once: never toggles at run time
        });
        FooterProperty.Changed.AddClassHandler<PackageCard>((c, e) => c._footer.Content = e.NewValue);
    }

    /// <summary>Makes an empty card.</summary>
    public PackageCard()
    {
        Classes.Add("ship");
        Classes.Add("pkg");
        _title.Classes.Add("fn");
        _size.Classes.Add("part");
        _pageLabel.Classes.Add("pglabel");
        _prev.Classes.Add("pgbtn");
        _next.Classes.Add("pgbtn");
        ToolTip.SetTip(_prev, "Previous parts");
        ToolTip.SetTip(_next, "More parts");
        _prev.Click += (_, _) => Turn(-1);
        _next.Click += (_, _) => Turn(+1);
        _pager.Children.Add(_pageLabel);
        _pager.Children.Add(_prev);
        _pager.Children.Add(_next);
        _pager.IsVisible = false;

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12, Margin = new Thickness(0, 0, 0, 5) };
        head.Children.Add(_title);
        Grid.SetColumn(_pager, 1);
        head.Children.Add(_pager);
        Grid.SetColumn(_size, 2);
        head.Children.Add(_size);

        _tiles.MinTileWidth = MinTileWidth;
        _tiles.CapacityChanged += (_, _) => UpdatePager();

        // Rows, not a stack: the footer takes the STAR row, so a host that pins the card's height (the Export card)
        // hands its footer a real height to scroll inside, rather than an infinite one.
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        layout.Children.Add(head);
        Grid.SetRow(_tiles, 1);
        layout.Children.Add(_tiles);
        Grid.SetRow(_footer, 2);
        layout.Children.Add(_footer);
        Child = layout;
        Rebuild();
    }

    /// <summary>Which page of parts shows, from 0.</summary>
    public int Page => _page;

    /// <summary>How many pages the parts take at the current width (1 when every part shows).</summary>
    public int PageCount => _tiles.Capacity <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(_tiles.Children.Count / (double)_tiles.Capacity));

    /// <summary>Shows the next (+1) or previous (-1) page, within the pages there are.</summary>
    /// <param name="by">How many pages to move.</param>
    public void Turn(int by)
    {
        int page = Math.Clamp(_page + by, 0, PageCount - 1);
        if (page == _page) return;
        _page = page;
        _tiles.First = _page * _tiles.Capacity;
        UpdatePager();
    }

    private void UpdatePager()
    {
        int pages = PageCount;
        if (_page > pages - 1)
        {
            _page = pages - 1;
            _tiles.First = _page * _tiles.Capacity;
        }
        _pageLabel.Text = string.Create(CultureInfo.InvariantCulture, $"{_page + 1}/{pages}");
        _prev.IsEnabled = _page > 0;
        _next.IsEnabled = _page < pages - 1;
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
        _tiles.Children.Clear();
        _page = 0;
        _tiles.First = 0;
        if (Parts is not null)
            foreach (var item in Parts)
                if (item?.ToString() is { Length: > 0 } text)
                    _tiles.Children.Add(Tile(text));
        UpdatePager();
    }

    private static Control Tile(string text)
    {
        var (name, detail) = Split(text);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 7 };

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

        // The NAME is what identifies a part, so the count gives way first: capped, trimmed, whole on the tooltip.
        var count = new TextBlock { Text = detail, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 74, Margin = new Thickness(6, 0, 0, 0) };
        count.Classes.Add("pkgdetail");
        Grid.SetColumn(count, 2);
        grid.Children.Add(count);

        var tile = new Border { Child = grid };
        tile.Classes.Add("pkgtile");
        ToolTip.SetTip(tile, text);
        return tile;
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
    /// <returns>The icon key, or null for a fact rather than a file.</returns>
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

    /// <summary>
    /// The tiles: as many columns as the width allows at <see cref="MinTileWidth"/>, each tile <see cref="TileHeight"/>
    /// tall; with <see cref="MaxRows"/> set, a window of that many rows starting at <see cref="First"/>, the rest
    /// arranged at nothing. Its height depends on the part COUNT and the width only, never on which page shows.
    /// </summary>
    internal sealed class TilePanel : Panel
    {
        private double _minTile = 140;
        private int _maxRows;
        private int _first;

        public double MinTileWidth { get => _minTile; set { _minTile = Math.Max(40, value); InvalidateMeasure(); } }

        public int MaxRows { get => _maxRows; set { _maxRows = Math.Max(0, value); InvalidateMeasure(); } }

        public int First { get => _first; set { _first = Math.Max(0, value); InvalidateArrange(); } }

        /// <summary>How many tiles show at once (every one when <see cref="MaxRows"/> is 0).</summary>
        public int Capacity { get; private set; }

        public int Columns { get; private set; } = 1;

        public event EventHandler? CapacityChanged;

        private (int Cols, int Rows) Shape(double width)
        {
            int n = Children.Count;
            int cols = double.IsFinite(width) && width > 0
                ? Math.Max(1, (int)((width + TileGap) / (_minTile + TileGap)))
                : Math.Max(1, n);
            cols = Math.Min(cols, Math.Max(1, n));
            int rows = n == 0 ? 0 : (int)Math.Ceiling(n / (double)cols);
            if (_maxRows > 0) rows = Math.Min(rows, _maxRows);
            return (cols, rows);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var (cols, rows) = Shape(availableSize.Width);
            double tileW = double.IsFinite(availableSize.Width)
                ? (availableSize.Width - TileGap * (cols - 1)) / cols
                : _minTile;
            foreach (var c in Children) c.Measure(new Size(tileW, TileHeight));
            double width = double.IsFinite(availableSize.Width) ? availableSize.Width : cols * _minTile + (cols - 1) * TileGap;
            return new Size(width, rows == 0 ? 0 : rows * TileHeight + (rows - 1) * TileGap);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var (cols, rows) = Shape(finalSize.Width);
            int capacity = _maxRows > 0 ? cols * rows : Children.Count;
            double tileW = (finalSize.Width - TileGap * (cols - 1)) / cols;
            for (int i = 0; i < Children.Count; i++)
            {
                int k = i - (_maxRows > 0 ? _first : 0);
                if (k < 0 || k >= capacity) { Children[i].Arrange(default); Children[i].IsVisible = false; continue; }
                Children[i].IsVisible = true;
                int row = k / cols, col = k % cols;
                Children[i].Arrange(new Rect(col * (tileW + TileGap), row * (TileHeight + TileGap), tileW, TileHeight));
            }
            Columns = cols;
            if (capacity != Capacity)
            {
                Capacity = capacity;
                CapacityChanged?.Invoke(this, EventArgs.Empty);
            }
            return finalSize;
        }
    }
}
