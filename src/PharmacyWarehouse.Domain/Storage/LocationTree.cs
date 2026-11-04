namespace PharmacyWarehouse.Domain.Storage;

public sealed record LocationNode(Guid Id, Guid? ParentId, string Name, int SortOrder);

// محاسبه‌های درخت محل بدون دیتابیس، تا سرور و کلاینت یک منطق داشته باشند.
// داده‌ی خراب (پدر ناموجود یا حلقه) هیچ‌وقت حلقه‌ی بی‌پایان نمی‌سازد؛ سرور جلوی ساختنش را می‌گیرد.
public static class LocationTree
{
    public const int MaxName = 100;
    public const string Separator = " › ";

    public static IReadOnlyDictionary<Guid, string> Paths(IEnumerable<LocationNode> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        var paths = new Dictionary<Guid, string>(byId.Count);

        foreach (var node in byId.Values)
        {
            var names = new List<string>();
            var seen = new HashSet<Guid>();
            for (var current = node; current is not null && seen.Add(current.Id);)
            {
                names.Add(current.Name);
                current = current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null;
            }

            names.Reverse();
            paths[node.Id] = string.Join(Separator, names);
        }

        return paths;
    }

    // آیا گذاشتن id زیر newParentId حلقه می‌سازد؟ یعنی newParentId خود id یا یکی از نواده‌هایش است.
    public static bool WouldCreateCycle(IEnumerable<LocationNode> nodes, Guid id, Guid? newParentId)
    {
        var parentOf = nodes.ToDictionary(n => n.Id, n => n.ParentId);
        var seen = new HashSet<Guid>();

        for (var current = newParentId; current is { } at && seen.Add(at); current = parentOf.GetValueOrDefault(at))
        {
            if (at == id) return true;
        }

        return false;
    }
}
