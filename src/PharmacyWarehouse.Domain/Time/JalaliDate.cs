using System.Globalization;

namespace PharmacyWarehouse.Domain.Time;

// تاریخ شمسی با PersianCalendar، نه فرهنگ fa-IR، تا به داده‌ی فرهنگ مرورگر تکیه نکنیم.
public static class JalaliDate
{
    private static readonly PersianCalendar Calendar = new();

    // مثال: ۱۴۰۵/۰۸/۱۳
    public static string FormatDate(DateTime utc)
    {
        var local = ToLocal(utc);
        return ToPersianDigits(
            $"{Calendar.GetYear(local):0000}/{Calendar.GetMonth(local):00}/{Calendar.GetDayOfMonth(local):00}");
    }

    // مثال: ۱۴۰۵/۰۸/۱۳ ۱۴:۳۰
    public static string Format(DateTime utc)
    {
        return FormatDate(utc) + " " + FormatTime(utc);
    }

    // مثال: ۱۴:۳۰
    public static string FormatTime(DateTime utc)
    {
        var local = ToLocal(utc);
        return ToPersianDigits($"{local.Hour:00}:{local.Minute:00}");
    }

    public static string ToPersianDigits(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9') chars[i] = (char)('۰' + (chars[i] - '0'));
        }

        return new string(chars);
    }

    private static DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) + LocalDay.OffsetFromUtc;
}
