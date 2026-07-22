using Nfty.App.Models;

namespace Nfty.App.Services;

public interface IRecentsService
{
    IReadOnlyList<RecentItem> Items { get; }
    void Add(RecentItem item);
}

/// <summary>Phase-1 recents: seeded with the mockup's sample rows so the Landing renders its list;
/// persistence lands in Phase 2.</summary>
public sealed class RecentsService : IRecentsService
{
    private readonly List<RecentItem> _items =
    [
        new("VaporPets", "3 recipes · 1000×1000", "~/art/vaporpets.cbk", false),
        new("NeonKoi", "1 recipe · 512×512", "~/art/neonkoi.cbk", false),
        new("aura.igt", "loose ingredient · 4 variants", "Kitchen", true),
    ];
    public IReadOnlyList<RecentItem> Items => _items;
    public void Add(RecentItem item) => _items.Insert(0, item);
}
