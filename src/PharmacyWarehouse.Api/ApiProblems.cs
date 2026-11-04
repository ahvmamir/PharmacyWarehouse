using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api;

// پاسخ‌های خطای مشترک و تنظیم توکن هم‌زمانی.
public static class ApiProblems
{
    public static IResult Validation(string field, string message) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [message] },
            title: message);

    public static IResult Validation(IEnumerable<ValidationError> errors) =>
        Results.ValidationProblem(
            errors.GroupBy(e => e.Field).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()),
            title: "ورودی‌ها را بررسی کنید.");

    public static IResult NotFound() =>
        Results.Problem(title: "پیدا نشد.", statusCode: StatusCodes.Status404NotFound);

    public static IResult Forbidden(string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status403Forbidden);

    public static IResult Conflict(string title) =>
        Results.Problem(title: title, statusCode: StatusCodes.Status409Conflict);

    public static IResult VersionConflict() =>
        Results.Problem(
            title: "این رکورد در این فاصله تغییر کرده است. دوباره بارگذاری کنید.",
            type: ProblemTypes.VersionConflict,
            statusCode: StatusCodes.Status409Conflict);

    // Version دیده‌شده‌ی Client را در شرط WHERE می‌گذارد و رکورد را حتماً «تغییرکرده» علامت می‌زند،
    // تا تغییر فقط در قطعات یا فرزندان هم نسخه را جلو ببرد و ناسازگاری ۴۰۹ شود.
    public static void ExpectVersion<T>(this DbContext db, T entity, int version) where T : class, IAudited
    {
        var property = db.Entry(entity).Property(e => e.Version);
        property.OriginalValue = version;
        property.IsModified = true;
    }

    // نقض یکتایی (خطای ۱۹ SQLite): دو درخواست هم‌زمان که هر دو از بررسی کد رد شده‌اند.
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19 };
}
