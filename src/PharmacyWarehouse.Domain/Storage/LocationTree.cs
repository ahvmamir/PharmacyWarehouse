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

    // خود گره و همه‌ی نواده‌هایش؛ دامنه‌ی یک نوبت انبارگردانی.
    public static HashSet<Guid> Subtree(IEnumerable<LocationNode> nodes, Guid root)
    {
        var children = nodes.Where(n => n.ParentId is not null).ToLookup(n => n.ParentId!.Value, n => n.Id);
        var result = new HashSet<Guid> { root };
        var pending = new Stack<Guid>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            foreach (var child in children[pending.Pop()])
            {
                if (result.Add(child)) pending.Push(child);
            }
        }

        return result;
    }

    // محل سطح اولِ هر گره. پدر ناموجود یا حلقه یعنی خود آخرین گره‌ی دیده‌شده ریشه حساب می‌شود.
    public static Guid RootOf(IReadOnlyDictionary<Guid, Guid?> parentOf, Guid id)
    {
        var seen = new HashSet<Guid>();
        var current = id;
        while (seen.Add(current) && parentOf.TryGetValue(current, out var parent) && parent is { } next && parentOf.ContainsKey(next))
        {
            current = next;
        }

        return current;
    }

    // پیمایش درخت: ریشه‌ها، سپس فرزندان هر گره؛ هم‌سطح‌ها با SortOrder و بعد Name (Ordinal).
    // مرتب‌سازی روی مسیر متنی اشتباه است («قفسه یخچال» بین «قفسه» و «قفسه › طبقه» می‌افتد).
    // گره با پدر ناموجود در سطح اول می‌آید و گره‌ی داخل حلقه بعد از بقیه با عمق ۰، تا پنهان نشود.
    public static IReadOnlyList<(LocationNode Node, int Depth)> Ordered(IEnumerable<LocationNode> nodes)
    {
        var all = nodes.ToList();
        var ids = all.Select(n => n.Id).ToHashSet();
        var children = all.ToLookup(n => n.ParentId is { } p && ids.Contains(p) ? p : (Guid?)null);
        var seen = new HashSet<Guid>();
        var result = new List<(LocationNode, int)>(all.Count);

        void Walk(Guid? parent, int depth)
        {
            foreach (var node in children[parent].OrderBy(n => n.SortOrder).ThenBy(n => n.Name, StringComparer.Ordinal))
            {
                if (!seen.Add(node.Id)) continue;
                result.Add((node, depth));
                Walk(node.Id, depth + 1);
            }
        }

        Walk(null, 0);

        foreach (var node in all.OrderBy(n => n.SortOrder).ThenBy(n => n.Name, StringComparer.Ordinal))
        {
            if (!seen.Add(node.Id)) continue;
            result.Add((node, 0));
            Walk(node.Id, 1);
        }

        return result;
    }

    // گره‌هایی که خودشان و همه‌ی نیاکانشان فعال‌اند؛ زیرشاخه‌ی غیرفعال از انتخاب‌گرها بیرون است.
    public static IReadOnlySet<Guid> EffectivelyActive(IEnumerable<(LocationNode Node, bool IsActive)> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Node.Id);
        var result = new HashSet<Guid>();

        foreach (var (id, entry) in byId)
        {
            var seen = new HashSet<Guid>();
            var active = true;
            for (var current = ((LocationNode Node, bool IsActive)?)entry; current is { } at && seen.Add(at.Node.Id);)
            {
                if (!at.IsActive) { active = false; break; }
                current = at.Node.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null;
            }

            if (active) result.Add(id);
        }

        return result;
    }
}
