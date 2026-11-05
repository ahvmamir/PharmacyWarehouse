using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Tests;

// باگ ۲۰۲۶-۱۱-۰۵: در «بچ تازه» با شماره‌ی موجود و تاریخ متفاوت، صفحه بی‌صدا به بچ موجود می‌رفت و تاریخ واردشده دور ریخته می‌شد.
public class ReceiptBatchMatcherTests
{
    private static BatchItem Batch(string number, DateOnly? expiry, string? text = null) =>
        new(Guid.NewGuid(), number, expiry, ExpiryPrecision.Day, CalendarKind.Gregorian,
            text ?? (expiry is { } d ? $"{d.Day:00}/{d.Month:00}/{d.Year}" : null), null, 5, false, 1);

    private static readonly BatchItem Existing = Batch("LOT-A1", new DateOnly(2029, 7, 21));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  * ")]
    public void EmptyNumber_IsNoMatch(string? typed) =>
        Assert.Equal(BatchMatchKind.NoMatch, ReceiptBatchMatcher.Resolve(typed, null, [Existing]).Kind);

    [Fact]
    public void UnknownNumber_IsNoMatch() =>
        Assert.Equal(BatchMatchKind.NoMatch, ReceiptBatchMatcher.Resolve("LOT-ZZ", "21/07/2029", [Existing]).Kind);

    [Fact]
    public void SameNumber_EmptyExpiry_UsesExisting()
    {
        var match = ReceiptBatchMatcher.Resolve("lot-a1", null, [Existing]);
        Assert.Equal(BatchMatchKind.UseExisting, match.Kind);
        Assert.Same(Existing, match.Batch);
    }

    [Fact]
    public void SameNumber_SameDate_UsesExisting() =>
        Assert.Equal(BatchMatchKind.UseExisting, ReceiptBatchMatcher.Resolve("LOT-A1", "21/07/2029", [Existing]).Kind);

    [Fact]
    public void SameNumber_SameDateInJalali_UsesExisting() =>
        // 31/04/1408 شمسی = 21/07/2029 میلادی؛ تقویم ورودی نباید تفاوت بسازد.
        Assert.Equal(BatchMatchKind.UseExisting, ReceiptBatchMatcher.Resolve("LOT-A1", "31/04/1408", [Existing]).Kind);

    [Fact]
    public void PersianKeyboardNumber_MatchesViaScanKey() =>
        Assert.Equal(BatchMatchKind.UseExisting, ReceiptBatchMatcher.Resolve("مخف-ش1", null, [Existing]).Kind);

    [Fact]
    public void SameNumber_DifferentDate_IsMismatchAndNamesBothDates()
    {
        var match = ReceiptBatchMatcher.Resolve("LOT-A1", "01/08/2029", [Existing]);
        Assert.Equal(BatchMatchKind.DateMismatch, match.Kind);
        Assert.Null(match.Batch);
        Assert.Contains("21/07/2029", match.Message);
        Assert.Contains("01/08/2029", match.Message);
        Assert.Contains("بچ‌نامبر را بررسی کنید", match.Message);
    }

    [Fact]
    public void SameNumber_ExistingHasNoDate_TypedHasDate_IsMismatch()
    {
        // سرور هم در این حالت ۴۰۹ می‌دهد (هر تاریخ دیگر، حتی وقتی بچ تاریخ ندارد).
        var noDate = Batch("LOT-N", null);
        var match = ReceiptBatchMatcher.Resolve("LOT-N", "01/08/2029", [noDate]);
        Assert.Equal(BatchMatchKind.DateMismatch, match.Kind);
        Assert.Contains("بدون تاریخ", match.Message);
    }

    [Fact]
    public void SameNumber_InvalidExpiryText_ReportsParseError()
    {
        var match = ReceiptBatchMatcher.Resolve("LOT-A1", "13/45/2029", [Existing]);
        Assert.Equal(BatchMatchKind.InvalidExpiry, match.Kind);
        Assert.False(string.IsNullOrEmpty(match.Message));
    }
}
