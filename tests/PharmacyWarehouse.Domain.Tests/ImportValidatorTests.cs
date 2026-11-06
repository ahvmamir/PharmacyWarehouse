using PharmacyWarehouse.Domain.Import;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Tests;

// جدول بخش ۱۲.۲ spec. داده‌ی ساختگی.
public class ImportValidatorTests
{
    private static ImportContext Ctx(params string[] roots)
    {
        var c = new ImportContext();
        foreach (var r in roots) c.RootLocationKeys.Add(r.ToUpperInvariant().Replace(" ", ""));
        return c;
    }

    private static ImportRowInput Row(string name = "داروی آزمایشی", string? qty = "5", string? batch = "TEST-B1", string? expiry = "31/05/2028",
        string sheet = "ضلع آزمایشی", string? brand = null, string? pack = null) =>
        new() { Sheet = sheet, RowNumber = 3, Name = name, Brand = brand, Quantity = qty, BatchNumber = batch, Expiry = expiry, UnitsPerPack = pack };

    private static ImportRowStatus Single(ImportReport r) => Assert.Single(r.Statuses);

    [Theory]
    [InlineData("12,5")]
    [InlineData("1,2,3")]
    [InlineData("1,20")]
    [InlineData("12٬5")]
    public void Quantity_WithMisplacedComma_IsError_NotSilentlyMerged(string qty)
    {
        var report = ImportValidator.Validate([Row(qty: qty)], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Error, Single(report).Severity);
        Assert.Empty(report.Plan);
    }

    [Theory]
    [InlineData("1,200", 1200)]
    [InlineData("1٬200", 1200)]
    [InlineData("1,200,000", 1200000)]
    [InlineData("1200", 1200)]
    [InlineData("30.0", 30)]
    public void Quantity_ValidForms_AreAccepted(string qty, int expected)
    {
        var report = ImportValidator.Validate([Row(qty: qty)], Ctx("ضلع آزمایشی"));

        Assert.Equal(expected, Assert.Single(report.Plan).Quantity);
    }

    [Fact]
    public void Pack_WithComma_IsKeptAsText_NotMergedInto25()
    {
        var report = ImportValidator.Validate([Row(pack: "2,5")], Ctx("ضلع آزمایشی"));

        Assert.Equal("2,5", Assert.Single(report.Plan).Pack);
    }

    [Fact]
    public void Pack_HugeNumber_DoesNotThrow()
    {
        var report = ImportValidator.Validate([Row(pack: "99999999999999999999")], Ctx("ضلع آزمایشی"));

        Assert.Equal("99999999999999999999", Assert.Single(report.Plan).Pack);
    }

    [Theory]
    [InlineData("12,5", null)]
    [InlineData("1,200", 1200L)]
    public void UnitCost_Comma_OnlyAsThousandsSeparator(string cost, long? expected)
    {
        var row = Row();
        row.UnitCost = cost;

        var report = ImportValidator.Validate([row], Ctx("ضلع آزمایشی"));

        if (expected is null) Assert.Equal(ImportSeverity.Error, Single(report).Severity);
        else Assert.Equal(expected, Assert.Single(report.Plan).UnitCost);
    }

    [Theory]
    [InlineData("#REF!")]
    [InlineData("#N/A")]
    [InlineData("#VALUE!")]
    [InlineData("#DIV/0!")]
    [InlineData("#NAME?")]
    public void ExcelErrorCell_IsError(string cell)
    {
        var report = ImportValidator.Validate([Row(batch: cell)], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Error, Single(report).Severity);
    }

    [Fact]
    public void InactiveProduct_CannotReceiveBatch_ButZeroQuantityRowIsHarmless()
    {
        var context = Ctx("ضلع آزمایشی");
        var key = ImportValidator.ProductKeyOf("داروی آزمایشی", null, null);
        context.ExistingProducts[key] = true;
        context.InactiveProducts.Add(key);

        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([Row()], context)).Severity);
        Assert.NotEqual(ImportSeverity.Error, Single(ImportValidator.Validate([Row(qty: "0")], context)).Severity);
    }

    [Fact]
    public void InactiveLocation_FromColumn_FromSheet_OrAsAncestor_IsError()
    {
        var context = Ctx("قفسه آزمایشی");
        context.InactiveLocationPathKeys.Add(ImportValidator.LocationKey("قفسه آزمایشی")!);

        var column = Row(sheet: "دیگر");
        column.Location = "قفسه آزمایشی";
        var child = Row(sheet: "دیگر");
        child.Location = "قفسه آزمایشی › طبقه تازه";

        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([column], context)).Severity);
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([child], context)).Severity);
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([Row(sheet: "قفسه آزمایشی")], context)).Severity);
    }

    [Fact]
    public void SheetName_TooLongForLocation_IsError()
    {
        var report = ImportValidator.Validate([Row(sheet: new string('ا', Storage.LocationTree.MaxName + 1))], Ctx());

        Assert.Equal(ImportSeverity.Error, Single(report).Severity);
    }

    [Fact]
    public void ExistingProduct_NewBarcodeOrMinStock_WarnsThatItIsNotApplied_OwnBarcodeDoesNot()
    {
        var context = Ctx("ضلع آزمایشی");
        var key = ImportValidator.ProductKeyOf("داروی آزمایشی", null, null);
        context.ExistingProducts[key] = true;
        context.ExistingBarcodes["TEST0001"] = key;

        var newCode = Row();
        newCode.Barcode = "TEST-0009";
        var min = Row(batch: "TEST-B2");
        min.MinStock = "4";
        var own = Row(batch: "TEST-B3");
        own.Barcode = "TEST0001";

        Assert.Equal(ImportSeverity.Warning, Single(ImportValidator.Validate([newCode], context)).Severity);
        Assert.Equal(ImportSeverity.Warning, Single(ImportValidator.Validate([min], context)).Severity);
        Assert.Equal(ImportSeverity.Ok, Single(ImportValidator.Validate([own], context)).Severity);
    }

    [Fact]
    public void ExistingProduct_ZeroQuantityRow_DoesNotListNewLocation()
    {
        var context = Ctx();
        context.ExistingProducts[ImportValidator.ProductKeyOf("داروی آزمایشی", null, null)] = true;

        var report = ImportValidator.Validate([Row(qty: "0", sheet: "شیت تازه")], context);

        Assert.Empty(report.Summary.NewLocations);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("  ")]
    public void RowWithoutName_IsIgnored(string? name)
    {
        var report = ImportValidator.Validate([Row(name: name!, qty: "*", batch: "*", expiry: "*")], Ctx());

        Assert.Equal(ImportSeverity.Ignored, Single(report).Severity);
        Assert.Empty(report.Plan);
        Assert.Equal(1, report.Summary.IgnoredRows);
    }

    [Fact]
    public void StarPrefixOnBatchAndExpiry_IsStripped()
    {
        var report = ImportValidator.Validate([Row(batch: "*TEST123", expiry: "*31/05/2028")], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Ok, Single(report).Severity);
        var plan = Assert.Single(report.Plan);
        Assert.Equal("TEST123", plan.BatchNumber);
        Assert.Equal(new DateOnly(2028, 5, 31), plan.Expiry!.Value.Date);
    }

    [Fact]
    public void GregorianFullDate_IsOkWithoutWarning()
    {
        var s = Single(ImportValidator.Validate([Row(expiry: "31/05/2028")], Ctx("ضلع آزمایشی")));
        Assert.Equal(ImportSeverity.Ok, s.Severity);
        Assert.Empty(s.Messages);
    }

    [Fact]
    public void JalaliFullDate_ConvertsWithWarning()
    {
        var report = ImportValidator.Validate([Row(expiry: "01/02/1408")], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Warning, Single(report).Severity);
        var plan = Assert.Single(report.Plan);
        Assert.Equal(CalendarKind.Jalali, plan.Expiry!.Value.Calendar);
        // همان تبدیل ماژول تاریخ برنامه؛ دقت خود PersianCalendar (حسابی) در برش ۴ ادعا نمی‌شود، ریسکش در brief ثبت است.
        Assert.Equal(ExpiryDate.FromJalali(1408, 2, 1).Date, plan.Expiry.Value.Date);
    }

    [Theory]
    [InlineData("07/2028", 2028, 7, 31)]
    [InlineData("04/1408", 2029, 7, 21)]
    public void MonthOnly_IsEndOfMonthWithWarning(string text, int y, int m, int d)
    {
        var report = ImportValidator.Validate([Row(expiry: text)], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Warning, Single(report).Severity);
        var plan = Assert.Single(report.Plan);
        Assert.Equal(new DateOnly(y, m, d), plan.Expiry!.Value.Date);
        Assert.Equal(ExpiryPrecision.Month, plan.Expiry.Value.Precision);
    }

    [Fact]
    public void NonexistentDay_IsClampedToMonthEndAndFlaggedCorrected()
    {
        var report = ImportValidator.Validate([Row(expiry: "31/06/2028")], Ctx("ضلع آزمایشی"));

        var status = Single(report);
        Assert.Equal(ImportSeverity.Warning, status.Severity);
        Assert.Contains(status.Messages, m => m.Contains("اصلاح‌شده"));
        Assert.Equal(new DateOnly(2028, 6, 30), report.Plan[0].Expiry!.Value.Date);
    }

    [Theory]
    [InlineData("13/062028")]
    [InlineData("30/04/02030")]
    [InlineData("30/09/0209")]
    [InlineData("2027/10")]
    public void BrokenDates_AreErrors(string text)
    {
        var report = ImportValidator.Validate([Row(expiry: text)], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Error, Single(report).Severity);
        Assert.Empty(report.Plan);
        Assert.False(report.CanCommit);
    }

    [Fact]
    public void MissingExpiry_ForRequiresExpiryProduct_IsError_UnlessMarkedNotRequired()
    {
        var text = Row(expiry: "۴ ماه پس از مصرف");
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([text], Ctx("ضلع آزمایشی"))).Severity);

        var empty = Row(expiry: null);
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([empty], Ctx("ضلع آزمایشی"))).Severity);

        var notRequired = Row(expiry: null);
        notRequired.ExpiryNotRequired = true;
        var report = ImportValidator.Validate([notRequired], Ctx("ضلع آزمایشی"));
        Assert.Equal(ImportSeverity.Ok, Single(report).Severity);
        Assert.False(report.Plan[0].RequiresExpiry);
        Assert.Null(report.Plan[0].Expiry);
    }

    [Theory]
    [InlineData("186+C97")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.5")]
    [InlineData("-3")]
    [InlineData("abc")]
    public void BadQuantity_IsError(string? qty) =>
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([Row(qty: qty)], Ctx("ضلع آزمایشی"))).Severity);

    [Theory]
    [InlineData("12", 12)]
    [InlineData("۱۲", 12)]
    [InlineData("12.0", 12)]
    [InlineData("1,200", 1200)]
    public void Quantity_ParsesPersianDigitsAndExcelDecimals(string qty, int expected)
    {
        var report = ImportValidator.Validate([Row(qty: qty)], Ctx("ضلع آزمایشی"));
        Assert.Equal(expected, Assert.Single(report.Plan).Quantity);
    }

    [Fact]
    public void ZeroQuantity_DefinesProductButNoBatch_AndDiscardsBatchAndExpiry()
    {
        var report = ImportValidator.Validate([Row(qty: "0", batch: "TEST-B9", expiry: "13/45/2028")], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Ok, Single(report).Severity); // تاریخ خراب هم دور ریخته می‌شود
        var plan = Assert.Single(report.Plan);
        Assert.Equal(0, plan.Quantity);
        Assert.Null(plan.BatchNumber);
        Assert.Null(plan.Expiry);
        Assert.Equal(1, report.Summary.ZeroQuantityProducts);
        Assert.Equal(0, report.Summary.NewBatches);
        Assert.Equal(1, report.Summary.NewProducts);
    }

    [Fact]
    public void MissingBatchNumber_WithPositiveQuantity_IsError() =>
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([Row(batch: "*")], Ctx("ضلع آزمایشی"))).Severity);

    [Fact]
    public void BatchAlreadyInSystem_IsError()
    {
        var ctx = Ctx("ضلع آزمایشی");
        var key = ImportValidator.ProductKeyOf("داروی آزمایشی", null, null);
        ctx.ExistingProducts[key] = true;
        ctx.ExistingBatches.Add(ImportValidator.BatchKeyOf(key, "TEST-B1"));

        var status = Single(ImportValidator.Validate([Row()], ctx));

        Assert.Equal(ImportSeverity.Error, status.Severity);
        Assert.Contains(status.Messages, m => m.Contains("قبلاً در سیستم"));
    }

    [Fact]
    public void BatchKeyIsScanKey_PersianKeyboardDuplicateIsCaught()
    {
        var ctx = Ctx("ضلع آزمایشی");
        var key = ImportValidator.ProductKeyOf("داروی آزمایشی", null, null);
        ctx.ExistingBatches.Add(ImportValidator.BatchKeyOf(key, "TEST-B1"));

        // «ف» = T روی صفحه‌کلید فارسی
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([Row(batch: "فثسف-B1")], ctx)).Severity);
    }

    [Fact]
    public void DuplicateBatchInFile_SecondRowIsError()
    {
        var report = ImportValidator.Validate([Row(), Row()], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Ok, report.Statuses[0].Severity);
        Assert.Equal(ImportSeverity.Error, report.Statuses[1].Severity);
        Assert.Single(report.Plan);
    }

    [Fact]
    public void SameProduct_MultipleBatches_CountsOneProductTwoBatches()
    {
        var report = ImportValidator.Validate([Row(batch: "TEST-B1"), Row(batch: "TEST-B2", expiry: "01/01/2030")], Ctx("ضلع آزمایشی"));

        Assert.Equal(1, report.Summary.NewProducts);
        Assert.Equal(2, report.Summary.NewBatches);
        Assert.True(report.CanCommit);
    }

    [Fact]
    public void SameNameDifferentPack_AreDifferentProducts()
    {
        var report = ImportValidator.Validate([Row(pack: "30"), Row(pack: "60", batch: "TEST-B2")], Ctx("ضلع آزمایشی"));
        Assert.Equal(2, report.Summary.NewProducts);
    }

    [Fact]
    public void PackFromExcelDecimal_IsNormalized() =>
        Assert.Equal("30", ImportValidator.Validate([Row(pack: "30.0")], Ctx("ضلع آزمایشی")).Plan[0].Pack);

    [Fact]
    public void UnknownSheetName_ListsNewLocationOnce_AndKnownRootDoesNot()
    {
        var report = ImportValidator.Validate([Row(sheet: "شیت تازه"), Row(sheet: "شیت تازه", batch: "TEST-B2"), Row(sheet: "ضلع آزمایشی", name: "داروی دیگر")], Ctx("ضلع آزمایشی"));

        Assert.Equal(["شیت تازه"], report.Summary.NewLocations);
    }

    [Fact]
    public void LocationColumn_OverridesSheet_ExistingPathIsNotNew_UnknownPathIsListedAsNew()
    {
        var ctx = Ctx();
        ctx.LocationPathKeys.Add(ImportValidator.LocationKey("ضلع غربی › قفسه ۱")!);

        var known = ImportValidator.Validate([new ImportRowInput { Sheet = "هرچه", Name = "داروی آزمایشی", Quantity = "0", Location = "ضلع غربی / قفسه ۱" }], ctx);
        Assert.Equal(ImportSeverity.Ok, Single(known).Severity);
        Assert.Equal("ضلع غربی / قفسه 1", known.Plan[0].LocationPath); // Clean ارقام را لاتین می‌کند
        Assert.Empty(known.Summary.NewLocations);

        var unknown = ImportValidator.Validate([new ImportRowInput { Sheet = "هرچه", Name = "داروی آزمایشی", Quantity = "0", Location = "جای تازه › قفسه" }], ctx);
        Assert.Equal(ImportSeverity.Ok, Single(unknown).Severity);
        Assert.Equal(["جای تازه › قفسه"], unknown.Summary.NewLocations);
    }

    [Fact]
    public void LocationSegmentTooLong_IsError()
    {
        var row = new ImportRowInput { Sheet = "S", Name = "داروی آزمایشی", Quantity = "0", Location = new string('ا', 101) };
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([row], Ctx())).Severity);
    }

    [Fact]
    public void BadQuantityAndBadExpiry_BothErrorsAreReportedTogether()
    {
        var status = Single(ImportValidator.Validate([Row(qty: "186+C97", expiry: "13/062028")], Ctx("ضلع آزمایشی")));
        Assert.Equal(2, status.Messages.Count);
    }

    [Fact]
    public void ProductInTwoSheets_LaterRowWarns_FirstLocationWins()
    {
        var report = ImportValidator.Validate([Row(sheet: "الف"), Row(sheet: "ب", batch: "TEST-B2")], Ctx("الف", "ب"));

        Assert.Equal(ImportSeverity.Ok, report.Statuses[0].Severity);
        Assert.Equal(ImportSeverity.Warning, report.Statuses[1].Severity);
        Assert.True(report.CanCommit);
    }

    [Fact]
    public void ExistingProduct_IsCounted_AndKeepsItsOwnRequiresExpiry()
    {
        var ctx = Ctx("ضلع آزمایشی");
        ctx.ExistingProducts[ImportValidator.ProductKeyOf("داروی آزمایشی", null, null)] = false;

        var report = ImportValidator.Validate([Row(expiry: null)], ctx);

        Assert.Equal(ImportSeverity.Ok, Single(report).Severity);
        Assert.Equal(1, report.Summary.ExistingProducts);
        Assert.Equal(0, report.Summary.NewProducts);
    }

    [Fact]
    public void Barcode_UsedByAnotherProduct_IsError_SameProductIsFine_DuplicateInFileAcrossProductsIsError()
    {
        var ctx = Ctx("ضلع آزمایشی");
        var other = ImportValidator.ProductKeyOf("داروی دیگر", null, null);
        ctx.ExistingProducts[other] = true;
        ctx.ExistingBarcodes["TEST-0001"] = other;

        var taken = Row(); taken.Barcode = "TEST-0001";
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([taken], ctx)).Severity);

        var same = Row(name: "داروی دیگر"); same.Barcode = "TEST-0001";
        Assert.Equal(ImportSeverity.Ok, Single(ImportValidator.Validate([same], ctx)).Severity);

        var a = Row(); a.Barcode = "TEST-0002";
        var b = Row(name: "داروی سوم", batch: "TEST-B3"); b.Barcode = "TEST-0002";
        var report = ImportValidator.Validate([a, b], Ctx("ضلع آزمایشی"));
        Assert.Equal(ImportSeverity.Error, report.Statuses[1].Severity);
    }

    [Theory]
    [InlineData("x", null)]
    [InlineData(null, "-5")]
    [InlineData(null, "ab")]
    public void OptionalNumbers_MustBeNumeric(string? minStock, string? cost)
    {
        var row = Row();
        row.MinStock = minStock;
        row.UnitCost = cost;
        Assert.Equal(ImportSeverity.Error, Single(ImportValidator.Validate([row], Ctx("ضلع آزمایشی"))).Severity);
    }

    [Fact]
    public void OptionalNumbers_Parse()
    {
        var row = Row();
        row.MinStock = "۱۰";
        row.UnitCost = "1,250,000";
        var plan = Assert.Single(ImportValidator.Validate([row], Ctx("ضلع آزمایشی")).Plan);
        Assert.Equal(10, plan.MinStock);
        Assert.Equal(1250000, plan.UnitCost);
    }

    [Fact]
    public void ErrorRows_DoNotBlockSummaryCounts_ButBlockCommit()
    {
        var report = ImportValidator.Validate([Row(), Row(qty: "x", name: "داروی خراب")], Ctx("ضلع آزمایشی"));

        Assert.Equal(1, report.Summary.ErrorRows);
        Assert.False(report.CanCommit);
        Assert.Single(report.Plan);
    }

    [Fact]
    public void EmptyInput_CannotCommit() =>
        Assert.False(ImportValidator.Validate([], Ctx()).CanCommit);

    [Fact]
    public void NameKey_IsCaseAndSpaceInsensitive_ForProductIdentity()
    {
        var report = ImportValidator.Validate([Row(name: "Test Drug", batch: "TEST-B1"), Row(name: "TESTDRUG", batch: "TEST-B2")], Ctx("ضلع آزمایشی"));
        Assert.Equal(1, report.Summary.NewProducts);
    }

    [Fact]
    public void Expiry_ExcelZeroDate_IsError_NotAnExpiredBatch()
    {
        var report = ImportValidator.Validate([Row(expiry: "31/12/1899")], Ctx("ضلع آزمایشی"));

        Assert.Equal(ImportSeverity.Error, Single(report).Severity);
        Assert.Contains(Single(report).Messages, m => m.Contains("۱۹۰۰"));
        Assert.Empty(report.Plan);
    }
}
