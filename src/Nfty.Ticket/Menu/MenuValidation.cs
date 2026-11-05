using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.Ticket;

/// <summary>Checks the Menu wire boundary before lookup caches or rolling can consume it.</summary>
internal static class MenuValidation
{
    internal static void Validate(MenuBook book)
    {
        if (book.Canvas.Width <= 0 || book.Canvas.Height <= 0) Fail("canvas", "dimensions must be positive");
        var cells = new Dictionary<string, MenuCell>(StringComparer.Ordinal);
        var art = book.Art;
        if (!Enum.IsDefined(typeof(MenuArtMode), art.Mode)) Fail("art mode", "must name a supported art mode");
        if (art.Mode == MenuArtMode.Atlas)
        {
            if (art.AtlasWidth <= 0 || art.AtlasHeight <= 0)
                Fail("art", "atlasWidth and atlasHeight must be positive");
            if (art.Cells.Count == 0) Fail("art", "atlas cells must not be empty");
        }
        foreach (var cell in art.Cells)
        {
            if (string.IsNullOrWhiteSpace(cell.Key)) Fail("art cell", "key must not be empty");
            if (!cells.TryAdd(cell.Key, cell)) Fail("art cell", $"has duplicate key '{cell.Key}'");
            if (cell.X < 0 || cell.Y < 0 || cell.Width <= 0 || cell.Height <= 0)
                Fail($"art cell '{cell.Key}'", "coordinates must be nonnegative and dimensions positive");
            if (art.Mode == MenuArtMode.Atlas
                && ((long)cell.X + cell.Width > art.AtlasWidth || (long)cell.Y + cell.Height > art.AtlasHeight))
                Fail($"art cell '{cell.Key}'", "rectangle exceeds atlas bounds");
            if (art.Mode == MenuArtMode.Loose && (cell.X != 0 || cell.Y != 0))
                Fail($"art cell '{cell.Key}'", "loose art must start at the origin");
        }

        var recipeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in book.Recipes)
        {
            string where = $"Recipe '{recipe.Id}'";
            Id(recipe.Id, where);
            if (!recipeIds.Add(recipe.Id)) Fail(where, "has a duplicate recipe id");
            Weight(recipe.Weight, where);
            var layers = new Dictionary<string, MenuLayer>(StringComparer.Ordinal);
            foreach (var layer in recipe.Layers)
            {
                string l = where + $" layer '{layer.Id}'";
                if (!Enum.IsDefined(typeof(LayerKind), layer.Kind)) Fail(l + " kind", "must name a supported layer kind");
                Id(layer.Id, l);
                if (layer.Id.Contains('=')) Fail(l, "ingredient id contains '=', which makes DNA ambiguous");
                if (!layers.TryAdd(layer.Id, layer)) Fail(l, "has a duplicate layer id");
                var variants = new HashSet<string>(StringComparer.Ordinal);
                foreach (var variant in layer.Variants)
                {
                    Id(variant.Id, l + " variant");
                    if (!variants.Add(variant.Id)) Fail(l, $"has duplicate variant id '{variant.Id}'");
                    Weight(variant.Weight, l + $" variant '{variant.Id}'");
                    string key = MenuBook.CellKey(recipe.Id, layer.Id, variant.Id);
                    if (!cells.TryGetValue(key, out var cell)) Fail(l, $"has no art cell '{key}'");
                    else if (cell.Width != book.Canvas.Width || cell.Height != book.Canvas.Height)
                        Fail(l, $"art cell '{key}' dimensions differ from canvas");
                }
                Total(layer.Variants.OrderBy(v => v.Id, StringComparer.Ordinal).Select(v => v.Weight), l + " variant weight");
                Color(layer, l);
            }
            var ordered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in recipe.LayerOrder)
            {
                if (id is null || !layers.ContainsKey(id)) Fail(where + " layerOrder", $"references unknown layer '{id}'");
                if (!ordered.Add(id!)) Fail(where + " layerOrder", $"has duplicate layer '{id}'");
            }
            if (ordered.Count != layers.Count) Fail(where + " layerOrder", "must contain every layer exactly once");
            foreach (var (id, percent) in recipe.AbsentPercent)
                if (!layers.ContainsKey(id) || !double.IsFinite(percent) || percent < 0 || percent > 100)
                    Fail(where + " absentPercent", $"has an unknown layer or invalid percentage for '{id}'");
            foreach (var rule in recipe.Rules)
            {
                if (!Enum.IsDefined(typeof(RuleType), rule.Type)) Fail(where + " rule type", "must name a supported rule type");
                foreach (var target in rule.Targets.Append(rule.When))
                    if (!layers.TryGetValue(target.IngredientId, out var layer)
                        || !layer.Variants.Any(v => string.Equals(v.Id, target.VariantId, StringComparison.Ordinal)))
                        Fail(where + " rule", $"references unknown variant '{target.IngredientId}:{target.VariantId}'");
            }
        }
        if (book.Recipes.Count > 0)
            Total(book.Recipes.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => r.Weight), "Menu recipe weight");
    }

    private static void Color(MenuLayer layer, string where)
    {
        var c = layer.Colorization;
        if (layer.Kind == LayerKind.Custom)
        {
            if (c is not null) Fail(where, "custom layers must have no colorization");
            return;
        }
        if (c is null) { Fail(where, "colorization is required"); return; }
        if (!Enum.IsDefined(typeof(ColorModel), c.Model)) Fail(where + " colorization model", "must name a supported color model");
        if (c.HueQuantize <= 0) Fail(where, "hueQuantize must be positive");
        if (c.SatQuantize <= 0) Fail(where, "satQuantize must be positive");
        if (layer.Kind == LayerKind.Static
            && (c.Entries.Count != 1 || c.Entries[0].Fixed is null || c.Entries[0].Range is not null))
            Fail(where, "static colorization requires one fixed entry");
        foreach (var entry in c.Entries)
        {
            Weight(entry.Weight, where + " colorization entry");
            if ((entry.Range is null) == (entry.Fixed is null))
                Fail(where, "colorization entry requires exactly one of range or fixed");
            if (entry.Range is { } range
                && (!double.IsFinite(range.HueMin) || !double.IsFinite(range.HueMax)
                    || !double.IsFinite(range.SatMin) || !double.IsFinite(range.SatMax)
                    || range.HueMin < 0 || range.HueMax > 360 || range.HueMin > range.HueMax
                    || range.SatMin < 0 || range.SatMax > 100 || range.SatMin > range.SatMax))
                Fail(where, "colorization range must ascend within hue 0..360 and saturation 0..100");
            if (entry.Fixed is { } spec)
            {
                try { ColorSpec.Parse(spec); }
                catch (FormatException ex) { throw new InvalidDataException($"{where} has invalid fixed color: {ex.Message}", ex); }
            }
        }
        if (layer.Kind == LayerKind.Dynamic) Total(c.Entries.Select(e => e.Weight), where + " colorization entry weight");
    }

    private static void Id(string id, string where)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains("..", StringComparison.Ordinal)
            || id.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 || id.Any(char.IsControl))
            Fail(where, "has an invalid id");
        for (int i = 0; i < id.Length; i++)
        {
            if (char.IsHighSurrogate(id[i]) && i + 1 < id.Length && char.IsLowSurrogate(id[i + 1])) { i++; continue; }
            if (char.IsSurrogate(id[i])) Fail(where, "id contains an unpaired UTF-16 surrogate");
        }
    }

    private static void Weight(double weight, string where)
    {
        if (!double.IsFinite(weight) || weight < 0) Fail(where, "weight must be finite and nonnegative");
    }

    private static void Total(IEnumerable<double> weights, string where)
    {
        double total = weights.Sum();
        if (!double.IsFinite(total) || total <= 0) Fail(where, "total weight must be positive and finite");
    }

    private static void Fail(string where, string problem) => throw new InvalidDataException($"{where}: {problem}.");
}
