namespace PharmacyWarehouse.Domain.Stock;

// وضعیت هر بچ در آمار دوره‌ی انبارگردانی (بخش ۱۵.۳ spec).
public enum PeriodRowStatus { NotCounted, Pending, Matched, Discrepancy, Adjusted, Dismissed }

// قاعده‌های دوره و نوبت انبارگردانی؛ سرور مرجع است و این‌جا فقط منطق خالص و آزمودنی است.
public static class StocktakeRules
{
    public const int MaxPeriodName = 80;

    // دو دامنه هم‌پوشانی دارند اگر هم محل‌هایشان و هم دسته‌هایشان هم‌پوشان باشد. فهرست خالی یعنی «همه».
    // subtree(id) = خود محل و همه‌ی زیرشاخه‌هایش.
    public static bool ScopesOverlap(
        IReadOnlyCollection<Guid> locationsA, IReadOnlyCollection<Guid> categoriesA,
        IReadOnlyCollection<Guid> locationsB, IReadOnlyCollection<Guid> categoriesB,
        Func<Guid, IReadOnlySet<Guid>> subtree)
    {
        var locations = locationsA.Count == 0 || locationsB.Count == 0
            || locationsA.Any(a => locationsB.Any(b => subtree(a).Contains(b) || subtree(b).Contains(a)));
        var categories = categoriesA.Count == 0 || categoriesB.Count == 0 || categoriesA.Intersect(categoriesB).Any();
        return locations && categories;
    }

    // محل نوبت باید زیر یکی از محل‌های دوره باشد (دوره‌ی بی‌محل همه را می‌پوشاند).
    public static bool LocationInPeriod(Guid location, IReadOnlyCollection<Guid> periodLocations, Func<Guid, IReadOnlySet<Guid>> subtree) =>
        periodLocations.Count == 0 || periodLocations.Any(p => subtree(p).Contains(location));

    // بچ در دامنه است اگر محل مؤثرش زیر یکی از محل‌ها و دسته‌ی قلمش در دسته‌ها باشد.
    public static bool InScope(Guid? effectiveLocation, Guid? category, IReadOnlySet<Guid>? locationScope, IReadOnlyCollection<Guid> categories) =>
        effectiveLocation is { } location
        && (locationScope is null || locationScope.Contains(location))
        && (categories.Count == 0 || (category is { } c && categories.Contains(c)));

    public static PeriodRowStatus Status(bool counted, bool sessionClosed, int difference, bool adjusted, bool dismissed) =>
        !counted ? PeriodRowStatus.NotCounted
        : adjusted ? PeriodRowStatus.Adjusted
        : dismissed ? PeriodRowStatus.Dismissed
        : !sessionClosed ? PeriodRowStatus.Pending
        : difference == 0 ? PeriodRowStatus.Matched
        : PeriodRowStatus.Discrepancy;

    public static string Label(PeriodRowStatus status) => status switch
    {
        PeriodRowStatus.NotCounted => "شمرده‌نشده",
        PeriodRowStatus.Pending => "در نوبت باز",
        PeriodRowStatus.Matched => "بی‌اختلاف",
        PeriodRowStatus.Discrepancy => "اختلاف",
        PeriodRowStatus.Adjusted => "اصلاح‌شده",
        PeriodRowStatus.Dismissed => "صرف‌نظرشده",
        _ => status.ToString()
    };
}
