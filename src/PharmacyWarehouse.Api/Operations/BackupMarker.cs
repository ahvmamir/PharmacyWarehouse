using System.Globalization;

namespace PharmacyWarehouse.Api.Operations;

// مسیر فایل دیتابیس؛ برای فضای دیسک و نشانه‌ی آخرین پشتیبان.
public sealed record DatabaseLocation(string FilePath);

// زمان آخرین پشتیبان موفق، کنار فایل دیتابیس. پشتیبان شبانه (فرمان backup، با حساب دیگری) و
// دانلود از برنامه هر دو آن را می‌نویسند؛ اعلان «پشتیبان قدیمی است» از همین می‌خواند.
public static class BackupMarker
{
    public static string PathFor(string databaseFile) => databaseFile + ".last-backup";

    public static void Write(string databaseFile, string where)
    {
        try
        {
            File.WriteAllText(PathFor(databaseFile), DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine + where);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // پشتیبان ساخته شده؛ نبودِ نشانه فقط یک اعلان اضافه است، نه شکست پشتیبان.
        }
    }

    public static (DateTime AtUtc, string Where)? Read(string databaseFile)
    {
        try
        {
            var path = PathFor(databaseFile);
            if (!File.Exists(path)) return null;

            var lines = File.ReadAllLines(path);
            if (lines.Length == 0 || !DateTime.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) return null;
            return (at.ToUniversalTime(), lines.Length > 1 ? lines[1] : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
