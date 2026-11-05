using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Stock;

// رتبه‌ی یک نتیجه؛ عدد کوچک‌تر بالاتر می‌آید. انطباق کامل بارکد جداست و سرور با ScanKey می‌سنجد.
public enum SearchMatch { NamePrefix = 1, AllWords = 2, Similar = 3 }

// NameKey و SearchText هر دو از TextNormalizer.SearchKey آمده‌اند.
public sealed record SearchCandidate(Guid Id, string NameKey, string SearchText);

// قاعده‌ی جست‌وجوی مشترک قلم و محل (بخش ۱۵.۱ spec). فقط روی متن یکدست‌شده کار می‌کند،
// پس مقایسه‌ی مستقیم متن فارسی در SQLite لازم نیست.
public static class ProductSearch
{
    private static readonly char[] Separators = [' ', '›', '/', '-', '،', ','];

    public static IReadOnlyList<SearchCandidate> Rank(string? query, IEnumerable<SearchCandidate> candidates, int limit)
    {
        var key = TextNormalizer.SearchKey(query);
        if (key is null) return [];

        return candidates
            .Select(c => (Candidate: c, Match: Classify(key, c.NameKey, c.SearchText)))
            .Where(x => x.Match is not null)
            .OrderBy(x => x.Match)
            .ThenBy(x => x.Candidate.NameKey, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.Id)
            .Take(limit)
            .Select(x => x.Candidate)
            .ToList();
    }

    public static SearchMatch? Classify(string queryKey, string nameKey, string searchText)
    {
        if (nameKey.StartsWith(queryKey, StringComparison.Ordinal)) return SearchMatch.NamePrefix;

        var words = Words(queryKey);
        if (words.Length == 0) return null;
        if (words.All(w => searchText.Contains(w, StringComparison.Ordinal))) return SearchMatch.AllWords;

        // «آموکسی سیلین» و «آموکسی‌سیلین» فقط در فاصله فرق دارند؛ غلط املایی نیستند.
        var squeezedQuery = queryKey.Replace(" ", "");
        if (squeezedQuery.Length > 0 && searchText.Replace(" ", "").Contains(squeezedQuery, StringComparison.Ordinal)) return SearchMatch.AllWords;

        var textWords = Words(searchText);
        return words.All(w => textWords.Any(t => WordIsSimilar(w, t))) ? SearchMatch.Similar : null;
    }

    // تعداد غلط مجاز به طول کلمه بستگی دارد؛ کلمه‌ی کوتاه با یک غلط کلمه‌ی دیگری می‌شود.
    public static int AllowedEdits(int length) => length switch
    {
        <= 3 => 0,
        <= 6 => 1,
        _ => 2
    };

    private static string[] Words(string text) => text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    // شروع کلمه هم سنجیده می‌شود تا کاربر پیش از تمام کردن کلمه هم نتیجه ببیند.
    private static bool WordIsSimilar(string word, string target)
    {
        var allowed = AllowedEdits(word.Length);
        if (allowed == 0) return target.StartsWith(word, StringComparison.Ordinal);
        if (EditDistance(word, target) <= allowed) return true;
        return target.Length > word.Length && EditDistance(word, target[..word.Length]) <= allowed;
    }

    // فاصله‌ی ویرایش با جابه‌جایی دو حرف کنار هم به‌عنوان یک غلط (Optimal String Alignment).
    public static int EditDistance(string a, string b)
    {
        var previous2 = new int[b.Length + 1];
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                var best = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) best = Math.Min(best, previous2[j - 2] + 1);
                current[j] = best;
            }

            (previous2, previous, current) = (previous, current, previous2);
        }

        return previous[b.Length];
    }
}
