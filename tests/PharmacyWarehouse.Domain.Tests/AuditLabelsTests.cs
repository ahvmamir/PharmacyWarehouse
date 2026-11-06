using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Tests;

public class AuditLabelsTests
{
    // فهرست صریح ستون‌های تاریخچه‌ای هر موجودیت (Domain به موجودیت‌های Api دسترسی ندارد).
    // با Entities.cs هم‌گام نگه داشته شود: ستون‌هایی که Id، Version، SearchText، تاریخ ساخت، OnHand یا *Key نیستند.
    public static TheoryData<string, string> AuditedFields => new()
    {
        { "Product", "Name" }, { "Product", "Brand" }, { "Product", "PackDescription" }, { "Product", "LocationId" },
        { "Product", "MinStock" }, { "Product", "UnitsPerPack" }, { "Product", "RequiresExpiry" },
        { "Product", "AllowExpiredWithdrawal" }, { "Product", "IsActive" }, { "Product", "IsProvisional" },
        { "Receipt", "UserId" }, { "Receipt", "AtUtc" }, { "Receipt", "Note" }, { "Receipt", "Status" }, { "Receipt", "ReviewedById" },
        { "Receipt", "ReviewedAtUtc" }, { "Receipt", "ReviewNote" },
        { "ReceiptLine", "ReceiptId" }, { "ReceiptLine", "Position" }, { "ReceiptLine", "ProductId" }, { "ReceiptLine", "BatchId" },
        { "ReceiptLine", "MovementId" }, { "ReceiptLine", "Quantity" },
        { "ProductBarcode", "ProductId" }, { "ProductBarcode", "Code" },
        { "Batch", "ProductId" }, { "Batch", "BatchNumber" }, { "Batch", "ExpiryDate" }, { "Batch", "ExpiryPrecision" },
        { "Batch", "ExpiryCalendar" }, { "Batch", "LocationId" }, { "Batch", "UnitCost" },
        { "StorageLocation", "ParentId" }, { "StorageLocation", "Name" }, { "StorageLocation", "SortOrder" }, { "StorageLocation", "IsActive" },
        { "Withdrawal", "UserId" }, { "Withdrawal", "AtUtc" }, { "Withdrawal", "OccurredAtUtc" }, { "Withdrawal", "IsLate" },
        { "Withdrawal", "AfterStocktake" }, { "Withdrawal", "Status" }, { "Withdrawal", "ReviewedById" },
        { "Withdrawal", "ReviewedAtUtc" }, { "Withdrawal", "ReviewNote" }, { "Withdrawal", "SelfReviewed" },
        { "Stocktake", "LocationId" }, { "Stocktake", "StartedById" }, { "Stocktake", "StartedAtUtc" },
        { "Stocktake", "ClosedAtUtc" }, { "Stocktake", "ClosedById" },
        { "StocktakeCount", "StocktakeId" }, { "StocktakeCount", "BatchId" }, { "StocktakeCount", "ExpectedOnHand" },
        { "StocktakeCount", "Counted" }, { "StocktakeCount", "AtUtc" }, { "StocktakeCount", "UserId" }, { "StocktakeCount", "AdjustmentId" }
    };

    [Theory]
    [MemberData(nameof(AuditedFields))]
    public void EveryAuditedField_HasPersianLabel(string entity, string field)
    {
        var label = AuditLabels.Field(field);

        Assert.NotEqual(field, label);
        Assert.Contains(label, l => l is >= '؀' and <= 'ۿ');
        Assert.NotEqual(entity, AuditLabels.Entity(entity));
    }

    [Theory]
    [InlineData("Product", "قلم")]
    [InlineData("Batch", "بچ")]
    [InlineData("ProductBarcode", "بارکد")]
    [InlineData("StorageLocation", "محل")]
    [InlineData("Unknown", "Unknown")]
    public void Entity_MapsToPersian(string type, string expected) => Assert.Equal(expected, AuditLabels.Entity(type));

    [Theory]
    [InlineData("Created", "ثبت")]
    [InlineData("Updated", "ویرایش")]
    [InlineData("Deleted", "حذف")]
    public void Action_MapsToPersian(string action, string expected) => Assert.Equal(expected, AuditLabels.Action(action));

    [Theory]
    [InlineData("IsActive", "True", "بله")]
    [InlineData("RequiresExpiry", "False", "خیر")]
    [InlineData("Name", null, "—")]
    [InlineData("Name", "", "—")]
    [InlineData("Brand", "   ", "—")]
    [InlineData("ExpiryPrecision", "Day", "روز")]
    [InlineData("ExpiryPrecision", "Month", "ماه")]
    [InlineData("ExpiryCalendar", "Jalali", "شمسی")]
    [InlineData("ExpiryCalendar", "Gregorian", "میلادی")]
    [InlineData("ExpiryDate", "2030-03-15", "15/03/2030")]
    [InlineData("ExpiryDate", "03/15/2030", "15/03/2030")]
    [InlineData("UnitCost", "1250000", "1,250,000")]
    [InlineData("Code", "TEST0000000001", "TEST0000000001")]
    public void Value_FormatsForDisplay(string field, string? raw, string expected) =>
        Assert.Equal(expected, AuditLabels.Value(field, raw));

    [Fact]
    public void Value_DropsGuidPrefix()
    {
        var id = Guid.NewGuid();

        Assert.Equal("قفسه‌ی آزمایشی", AuditLabels.Value("LocationId", $"{id}|قفسه‌ی آزمایشی"));
    }

    [Fact]
    public void Value_KeepsPipeInFreeText() => Assert.Equal("a|b", AuditLabels.Value("Name", "a|b"));

    [Fact]
    public void Value_BareGuidWithoutName_IsKept()
    {
        var id = Guid.NewGuid().ToString();

        Assert.Equal(id, AuditLabels.Value("LocationId", id));
    }

    [Theory]
    [InlineData("2030-03-15", "Day", "Gregorian", "15/03/2030")]
    [InlineData("2030-03-31", "Month", "Gregorian", "03/2030")]
    [InlineData("2029-07-21", "Month", "Jalali", "۰۴/۱۴۰۸")]
    [InlineData("2029-04-20", "Day", "Jalali", "۰۱/۰۲/۱۴۰۸")]
    [InlineData(null, "Day", "Gregorian", "—")]
    public void Expiry_UsesInputCalendarAndPrecision(string? date, string precision, string calendar, string expected) =>
        Assert.Equal(expected, AuditLabels.Expiry(date, precision, calendar));
}
