using System.Globalization;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Time;

// تاریخ انقضا: همیشه به صورت DateOnly میلادی ذخیره می‌شود؛ دقت و تقویم ورودی برای نمایش نگه داشته می‌شود.
public readonly record struct ExpiryValue(DateOnly Date, ExpiryPrecision Precision, CalendarKind Calendar);

public enum ExpiryWarning { ConvertedFromJalali, MonthOnly, DayClampedToMonthEnd }

// Error متن فارسی برای کاربر است. ورودی خالی خطا نیست: Value و Error هر دو null.
public sealed record ExpiryParse(ExpiryValue? Value, IReadOnlyList<ExpiryWarning> Warnings, string? Error);

public static class ExpiryDate
{
    private const string FormatError = "تاریخ انقضا نامعتبر است؛ قالب درست: روز/ماه/سال یا ماه/سال";
    private const int MinJalaliYear = 1300;
    private const int MaxJalaliYear = 1499; // سال < ۱۵۰۰ شمسی است، وگرنه میلادی
    private const int MinGregorianYear = 1500;
    private const int MaxGregorianYear = 9999;

    private static readonly PersianCalendar Persian = new();
    private static readonly GregorianCalendar Gregorian_ = new();

    public static ExpiryParse Parse(string? text)
    {
        var s = TextNormalizer.ToAsciiDigits(text ?? "");
        s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).Trim('*');
        if (s.Length == 0) return new ExpiryParse(null, [], null);

        var parts = s.Split('/');
        if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || !p.All(c => c is >= '0' and <= '9')))
            return Fail();

        var yearText = parts[^1];
        if (yearText.Length != 4) return Fail(); // سال ۴ رقمی الزامی است

        var year = int.Parse(yearText, CultureInfo.InvariantCulture);
        CalendarKind calendar;
        if (year is >= MinJalaliYear and <= MaxJalaliYear) calendar = CalendarKind.Jalali;
        else if (year is >= MinGregorianYear and <= MaxGregorianYear) calendar = CalendarKind.Gregorian;
        else return Fail();

        var monthText = parts[^2];
        if (monthText.Length > 2) return Fail();
        var month = int.Parse(monthText, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12) return Fail();

        int? day = null;
        if (parts.Length == 3)
        {
            if (parts[0].Length > 2) return Fail();
            day = int.Parse(parts[0], CultureInfo.InvariantCulture);
            if (day < 1 || day > 31) return Fail();
        }

        var warnings = new List<ExpiryWarning>();
        if (calendar == CalendarKind.Jalali) warnings.Add(ExpiryWarning.ConvertedFromJalali);
        if (day is null) warnings.Add(ExpiryWarning.MonthOnly);
        else
        {
            var length = DaysInMonth(year, month, calendar);
            if (day > length)
            {
                day = length;
                warnings.Add(ExpiryWarning.DayClampedToMonthEnd);
            }
        }

        var value = calendar == CalendarKind.Jalali ? FromJalali(year, month, day) : FromGregorian(year, month, day);
        return new ExpiryParse(value, warnings, null);
    }

    // day == null ← دقت ماه، آخر ماه. مقدار نامعتبر استثنا می‌دهد (ورودی کاربر باید از Parse بگذرد).
    public static ExpiryValue FromGregorian(int year, int month, int? day)
    {
        var date = new DateOnly(year, month, day ?? Gregorian_.GetDaysInMonth(year, month));
        return new ExpiryValue(date, day is null ? ExpiryPrecision.Month : ExpiryPrecision.Day, CalendarKind.Gregorian);
    }

    public static ExpiryValue FromJalali(int year, int month, int? day)
    {
        var d = day ?? Persian.GetDaysInMonth(year, month);
        var date = DateOnly.FromDateTime(Persian.ToDateTime(year, month, d, 0, 0, 0, 0));
        return new ExpiryValue(date, day is null ? ExpiryPrecision.Month : ExpiryPrecision.Day, CalendarKind.Jalali);
    }

    // نمایش به همان تقویم و دقت ورودی.
    public static string Display(ExpiryValue v) => (v.Calendar, v.Precision) switch
    {
        (CalendarKind.Jalali, ExpiryPrecision.Month) => JalaliDate.ToPersianDigits(JalaliParts(v.Date, withDay: false)),
        (CalendarKind.Jalali, _) => Jalali(v),
        (_, ExpiryPrecision.Month) => $"{v.Date.Month:00}/{v.Date.Year:0000}",
        _ => Gregorian(v)
    };

    public static string Gregorian(ExpiryValue v) => $"{v.Date.Day:00}/{v.Date.Month:00}/{v.Date.Year:0000}";

    public static string Jalali(ExpiryValue v) => JalaliDate.ToPersianDigits(JalaliParts(v.Date, withDay: true));

    private static string JalaliParts(DateOnly date, bool withDay)
    {
        var dt = date.ToDateTime(TimeOnly.MinValue);
        var y = Persian.GetYear(dt);
        var m = Persian.GetMonth(dt);
        return withDay ? $"{Persian.GetDayOfMonth(dt):00}/{m:00}/{y:0000}" : $"{m:00}/{y:0000}";
    }

    private static int DaysInMonth(int year, int month, CalendarKind calendar) =>
        calendar == CalendarKind.Jalali ? Persian.GetDaysInMonth(year, month) : Gregorian_.GetDaysInMonth(year, month);

    private static ExpiryParse Fail() => new(null, [], FormatError);
}
