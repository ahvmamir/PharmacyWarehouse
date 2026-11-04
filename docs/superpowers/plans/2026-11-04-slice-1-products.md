# برش ۱ — قلم، بارکد، بچ، ورود جنس: برنامه‌ی پیاده‌سازی

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** انباردار قلم تعریف کند (بدون موجودی)، بارکد به قلم بدهد، جنس وارد کند (بچ تازه یا موجود) با دو فیلد تاریخ شمسی/میلادی و با بارکدخوان؛ همه موجودی و حرکت‌ها را ببینند.

**Architecture:** قواعد خالص در Domain (`ScanKey`، `ExpiryDate`)؛ جدول‌های `Product`، `ProductBarcode`، `Batch` (IAudited) و `StockMovement` (دفتر فقط‌اضافه‌شونده با trigger)؛ `Batch.OnHand` در همان تراکنش حرکت به‌روز می‌شود؛ endpointها به الگوی `StorageLocationEndpoints`.

**Tech Stack:** همان برش ۰ (.NET 10، EF Core SQLite 10.0.14، Blazor WASM، xunit).

**Spec:** `docs/spec.md` بخش‌های ۴، ۵، ۶، ۷.۳، ۸، ۹.۱، ۹.۳ (فقط ثبت کد داخلی، نه چاپ)، ۱۴، ۱۵، ۱۶.

**تأیید:** امیر اجرای خودکار تا برش ۶ را مجاز کرد (۲۰۲۶-۱۱-۰۴)؛ این برنامه را Claude تأیید کرده است. تصمیم‌ها با برچسب [Claude] در ledger.

## Global Constraints

- همه‌ی قواعد `CLAUDE.md` (بخش «قواعد کد») برقرارند: رهگیر تاریخچه، `Version` + ۴۰۹، `TextNormalizer.Clean` قبل از ذخیره، یکتایی و جست‌وجو فقط روی ستون‌های کلید، هرگز مقایسه‌ی متن فارسی در SQLite، `no-store`، CSRF، `ApiResult`/`ApiSender`.
- مقدارها عدد صحیح جعبه؛ `UnitsPerPack` = ۱ و در رابط کاربری دیده نمی‌شود.
- تاریخ انقضا `DateOnly` میلادی؛ فقط یک ستون تاریخ؛ `ExpiryPrecision` (Day/Month) و `ExpiryCalendar` (Gregorian/Jalali) فقط برای نمایش.
- `StockMovement` هرگز UPDATE یا DELETE نمی‌شود (trigger مثل `AuditEntries`).
- `OnHand ≥ 0` (کد و `CHECK` در SQLite).
- مجوزها: خواندن `stock.view`؛ قلم/بارکد/محل `products.manage`؛ ورود جنس `stock.receive`.
- داده‌ی تست ساختگی (`TEST-DRUG-01`، `TEST-BATCH-01`، بارکد `TEST0000000001`).
- migration: یک migration به نام `Products` در `Data/Migrations` (`--output-dir Data/Migrations`).

## Review Focus

1. **بارکدخوان با صفحه‌کلید فارسی**: اسکن `شذ12` باید همان قلم `AB12` را پیدا کند؛ هم در ثبت بارکد و هم در جست‌وجو. ← Task 1 و 3.
2. **دو ورود هم‌زمان جنس به یک بچ**: هر دو `OnHand` را می‌خوانند؛ دومی نباید یکی را گم کند (۴۰۹ یا جمع درست). ← Task 4 تست `ConcurrentReceipts_SameBatch_NoLostUpdate`.
3. **تاریخ ماه‌دار در تقویم دیگر**: `04/1408` باید آخر تیر ۱۴۰۸ شود (۲۰۲۹-۰۷-۲۱)، نه آخر آوریل. ← Task 1.
4. **حذف محلی که قلم یا بچ دارد**: باید ۴۰۹ بدهد و قلم/بچ را در وابستگی‌ها نشان دهد. ← Task 2.
5. **تغییر تیک «انقضا لازم است» روی قلمی که بچ بی‌تاریخ دارد**: روشن کردنش نباید بچ‌های قبلی را خراب کند؛ فقط ورودهای بعدی تاریخ می‌خواهند. ← Task 3.

---

## File Structure

| مسیر | مسئولیت |
|---|---|
| `src/PharmacyWarehouse.Domain/Text/TextNormalizer.cs` | + `ScanKey` |
| `src/PharmacyWarehouse.Domain/Time/ExpiryDate.cs` | خواندن، تبدیل و نمایش تاریخ انقضا |
| `src/PharmacyWarehouse.Domain/Stock/StockEnums.cs` | `StockMovementKind`، `ExpiryPrecision`، `CalendarKind` |
| `src/PharmacyWarehouse.Domain/Stock/ProductRules.cs` | حد طول فیلدها و اعتبارسنجی قلم |
| `src/PharmacyWarehouse.Domain/Contracts/ProductContracts.cs` | قرارداد قلم، بچ، بارکد، حرکت |
| `src/PharmacyWarehouse.Domain/Contracts/ReceiptContracts.cs` | قرارداد ورود جنس |
| `src/PharmacyWarehouse.Api/Data/Entities.cs` | + `Product`، `ProductBarcode`، `Batch`، `StockMovement` |
| `src/PharmacyWarehouse.Api/Stock/ProductEndpoints.cs` | قلم و بارکد |
| `src/PharmacyWarehouse.Api/Stock/ReceiptEndpoints.cs` | ورود جنس |
| `src/PharmacyWarehouse.Api/Stock/StockLedger.cs` | تنها جای نوشتن حرکت و به‌روز کردن `OnHand` |
| `src/PharmacyWarehouse.Client/Components/ScanInput.razor` | فیلد اسکن |
| `src/PharmacyWarehouse.Client/Components/ExpiryFields.razor` | دو فیلد تاریخ هم‌گام |
| `src/PharmacyWarehouse.Client/Pages/Products.razor`، `ProductDetails.razor`، `ProductEdit.razor`، `Receive.razor` | صفحه‌ها |
| `src/PharmacyWarehouse.Client/Services/ProductsClient.cs` | فراخوانی Api |

---

### Task 0: اصلاح‌های بازبینی نهایی برش ۰ (پیش از هر انتخاب‌گر محل برای قلم)

**Files:**
- Modify: `src/PharmacyWarehouse.Domain/Storage/LocationTree.cs`، `src/PharmacyWarehouse.Api/Storage/StorageLocationEndpoints.cs`، `src/PharmacyWarehouse.Api/Data/Entities.cs` (`StorageLocation.KeyOf`)، `src/PharmacyWarehouse.Api/Data/AuditInterceptor.cs`، `src/PharmacyWarehouse.Client/Pages/Locations.razor`، `docs/brief.md` (بخش ۷)، `docs/spec.md` (بخش ۸ و ۱۷)
- Test: `tests/PharmacyWarehouse.Domain.Tests/LocationTreeTests.cs`، `tests/PharmacyWarehouse.Api.Tests/StorageLocationTests.cs`، `LocationAuditTests.cs`

**Interfaces:**
- Produces (Domain، `LocationTree`):
  - `public static IReadOnlyList<(LocationNode Node, int Depth)> Ordered(IEnumerable<LocationNode> nodes)` — پیمایش درخت: ریشه‌ها، سپس فرزندان هر گره، هم‌سطح‌ها با `SortOrder` سپس `Name` (Ordinal)؛ گره با پدر ناموجود در سطح اول؛ گره‌ی داخل حلقه هم در خروجی می‌آید (بعد از بقیه، عمق ۰) تا پنهان نشود.
  - `public static IReadOnlySet<Guid> EffectivelyActive(IEnumerable<(LocationNode Node, bool IsActive)> nodes)` — گره و همه‌ی نیاکانش فعال.
  - `LocationNode` بدون تغییر.
- Produces (Api): `public static string StorageLocation.KeyOf(Guid? parentId)` — تنها جای ساختن `ParentKey` (`""` برای ریشه، `Guid.ToString()` در غیر این صورت). `StorageLocationItem` یک فیلد تازه می‌گیرد: `bool IsUsable` (= در `EffectivelyActive`)، آخرین پارامتر رکورد؛ کلاینت انتخاب‌گرها را با آن فیلتر می‌کند.
- `AuditInterceptor`: هر ستونی که نامش با `Key` تمام می‌شود ignore می‌شود (به‌جای فهرست ثابت `NameKey`/`ParentKey`)؛ بقیه‌ی فهرست ثابت می‌ماند.

- [ ] **Step 1: تست‌ها (ناموفق)**

```csharp
// LocationTreeTests
Ordered_SiblingNameExtendingParentName_StaysOutsideParentsChildren   // «قفسه»، «قفسه یخچال»، «قفسه › طبقه 1»: ترتیب = قفسه، طبقه 1 (عمق ۱)، قفسه یخچال
Ordered_UsesSortOrderBeforeName
Ordered_CycleNodes_AreStillListed                                    // A↔B بدون ریشه: هر دو در خروجی
EffectivelyActive_InactiveAncestor_DeactivatesBranch
// StorageLocationTests
List_IsInTreeOrder_WithSortOrder
List_ChildOfInactive_IsNotUsable                                     // IsUsable == false
Create_UnderInactiveParent_Returns400                                // «محل بالاتر غیرفعال است» (spec §8 تکمیل)
Create_WithDeletedParent_Returns400                                  // ParentId محلی که حذف شده ← 400. مسیر مسابقه (حذف بین اعتبارسنجی و ذخیره) قطعی قابل تست نیست: در کد، DbUpdateException غیر یکتا در Create/Update ← ApiProblems.Validation("ParentId", "محل بالاتر پیدا نشد.")، نه 500
// LocationAuditTests
DerivedKeyColumns_AreNotAudited                                      // تغییر نام فقط یک ردیف Name، بدون NameKey/ParentKey
```

- [ ] **Step 2: پیاده‌سازی؛ `ListAsync` و `Locations.razor` (`Ordered` و `ParentOptions`) از `LocationTree.Ordered` و `IsUsable` استفاده کنند؛ PASS؛ کل تست‌ها سبز**
- [ ] **Step 3: مستندات** — spec §8: «غیرفعال کردن یک محل همه‌ی زیرشاخه‌هایش را از انتخاب‌گرها خارج می‌کند؛ زیر محل غیرفعال نمی‌شود محل ساخت.» spec §17 و brief §7: الگوهای حذف‌شده برای برش‌های بعد با `git show 241d144:src/PharmacyWarehouse.Api/DataTools/DataEndpoints.cs`، `...:src/PharmacyWarehouse.Api/Reports/ReportEndpoints.cs`، `...:src/PharmacyWarehouse.Client/Components/HistoryTimeline.razor`.
- [ ] **Step 4: Commit** — `"Location tree order, inactive branches, shared parent key; audit ignores key columns"`

---

### Task 1: `ScanKey` و `ExpiryDate` در Domain

**Files:**
- Modify: `src/PharmacyWarehouse.Domain/Text/TextNormalizer.cs`
- Create: `src/PharmacyWarehouse.Domain/Stock/StockEnums.cs`، `src/PharmacyWarehouse.Domain/Time/ExpiryDate.cs`
- Test: `tests/PharmacyWarehouse.Domain.Tests/ScanKeyTests.cs`، `ExpiryDateTests.cs`

**Interfaces:**
- Produces:
  - `public static string? TextNormalizer.ScanKey(string? value)` — `null`/خالی ← `null`.
  - `public enum ExpiryPrecision { Day, Month }`؛ `public enum CalendarKind { Gregorian, Jalali }`؛ `public enum StockMovementKind { Import, Receipt, Withdrawal, Reversal, Adjustment, Disposal }` (namespace `PharmacyWarehouse.Domain.Stock`).
  - `public readonly record struct ExpiryValue(DateOnly Date, ExpiryPrecision Precision, CalendarKind Calendar);`
  - `public enum ExpiryWarning { ConvertedFromJalali, MonthOnly, DayClampedToMonthEnd }`
  - `public sealed record ExpiryParse(ExpiryValue? Value, IReadOnlyList<ExpiryWarning> Warnings, string? Error);` — `Error` متن فارسی برای کاربر.
  - `public static class ExpiryDate { ExpiryParse Parse(string? text); ExpiryValue FromGregorian(int year, int month, int? day); ExpiryValue FromJalali(int year, int month, int? day); string Display(ExpiryValue v); string Gregorian(ExpiryValue v); string Jalali(ExpiryValue v); }`
  - `Display` = نمایش به همان تقویم و دقتی که وارد شده (ارقام فارسی برای شمسی: `۰۴/۱۴۰۸`؛ میلادی لاتین: `07/2028` یا `31/05/2028`). `Gregorian`/`Jalali` = قالب کامل `dd/MM/yyyy` در آن تقویم، برای پر کردن دو فیلد.

- [ ] **Step 1: تست‌های `ScanKey` (ناموفق)**

```csharp
[Theory]
[InlineData("AB12", "AB12")]
[InlineData("شذ12", "AB12")]          // صفحه‌کلید فارسی استاندارد ویندوز: ش=A، ذ=B
[InlineData("*TEST123*", "TEST123")]
[InlineData(" test-123 ", "TEST-123")]
[InlineData("۱۲۳٤٥", "12345")]        // ارقام فارسی و عربی
[InlineData("ضصثق", "QWER")]
public void ScanKey_Normalizes(string input, string expected) => Assert.Equal(expected, TextNormalizer.ScanKey(input));
[Theory] [InlineData(null)] [InlineData("")] [InlineData("  * ")] public void ScanKey_EmptyIsNull(string? input)
```

نگاشت: چیدمان استاندارد فارسی ویندوز (Persian) ردیف به ردیف: `ضصثقفغعهخحجچ`↔`qwertyuiop[]`، `شسیبلاتنمکگ`↔`asdfghjkl;'`، `ظطزرذدپو`↔`zxcvbnm,`؛ `ي`(عربی)=`d`، `ك`(عربی)=`;`. حروف بزرگ؛ همه‌ی فاصله‌ها حذف؛ `*` ابتدا و انتها حذف.

- [ ] **Step 2: پیاده‌سازی؛ PASS**

- [ ] **Step 3: تست‌های `ExpiryDate` (ناموفق)** — جدول بخش ۱۲.۲ spec، با مقدارهای ساختگی:

```csharp
// (ورودی, تاریخ میلادی انتظار, دقت, تقویم, هشدارها)
"31/05/2028"  → 2028-05-31, Day, Gregorian, []
"*31/05/2028" → همان (ستاره حذف)
"07/2028"     → 2028-07-31, Month, Gregorian, [MonthOnly]
"01/02/1408"  → 2029-04-20, Day, Jalali, [ConvertedFromJalali]      // ۱ اردیبهشت ۱۴۰۸
"04/1408"     → 2029-07-21, Month, Jalali, [ConvertedFromJalali, MonthOnly]   // آخر تیر ۱۴۰۸ (Review Focus ۳)
"31/06/2028"  → 2028-06-30, Day, Gregorian, [DayClampedToMonthEnd]
"31/02/2028"  → 2028-02-29, Day, Gregorian, [DayClampedToMonthEnd]
"۳۱/۰۵/۲۰۲۸"  → 2028-05-31 (ارقام فارسی)
خطا (Value null، Error غیرخالی): "13/062028"، "30/04/02030"، "30/09/0209"، "2027/10"، "4ماه پس از مصرف"، "32/01/2028"، "00/2028"، "13/2028"
null یا "" یا "*" → Value null، Error null (خالی ≠ خطا)
FromJalali(1408, 12, null) → 30/12/1408 (۱۴۰۸ کبیسه است؛ اسفند ۳۰ روز)
Display(FromJalali(1408,4,null)) == "۰۴/۱۴۰۸"؛ Display(FromGregorian(2028,7,null)) == "07/2028"
Jalali(FromGregorian(2028,5,31)) == "۱۱/۰۳/۱۴۰۷"؛ Gregorian(FromJalali(1408,2,1)) == "20/04/2029"
// مقدارها با PersianCalendar در .NET 10.0.14 بررسی شده‌اند (کنترل‌کننده، ۲۰۲۶-۱۱-۰۴).
```

قواعد: سال ۴ رقمی الزامی (`0209`، `02030` خطا)؛ سال < ۱۵۰۰ شمسی؛ `dd/mm/yyyy` یا `mm/yyyy` با `/`؛ روز بیش از طول ماه ← آخر ماه + هشدار؛ روز ۰ یا ماه خارج ۱..۱۲ خطا. تبدیل با `System.Globalization.PersianCalendar`.

- [ ] **Step 4: پیاده‌سازی؛ PASS**

- [ ] **Step 5: Commit** — `git commit -m "Add ScanKey and expiry date parsing"`

---

### Task 2: جدول‌ها، دفتر حرکت، migration، وابستگی محل

**Files:**
- Create: `src/PharmacyWarehouse.Domain/Stock/ProductRules.cs`
- Modify: `src/PharmacyWarehouse.Api/Data/Entities.cs`، `WarehouseDbContext.cs`، `AuditSubject.cs`، `AuditInterceptor.cs` (ستون‌های `*Key` از Task 0 خودکار ignore می‌شوند؛ توصیف `LocationId` (محل) و `ProductId` (قلم) به نام در `DescribeAsync`)، `src/PharmacyWarehouse.Api/Storage/StorageLocationEndpoints.cs` (وابستگی‌ها)
- Create: `src/PharmacyWarehouse.Api/Stock/StockLedger.cs`
- Migration: `ef/dotnet-ef migrations add Products --output-dir Data/Migrations` + SQL: triggerهای `StockMovements_NoUpdate`/`StockMovements_NoDelete` و `CHECK (OnHand >= 0)` روی `Batches` (با `ToTable(t => t.HasCheckConstraint("CK_Batches_OnHand", "OnHand >= 0"))`).
- Test: `tests/PharmacyWarehouse.Api.Tests/StockLedgerTests.cs`، افزودن به `StorageLocationTests.cs`

**Interfaces:**
- Produces (`ProductRules`): `MaxName = 200`، `MaxBrand = 100`، `MaxPack = 100`، `MaxBatch = 50`، `MaxCode = 100`، `MaxNote = 500`؛ `IReadOnlyList<ValidationError> Validate(SaveProductRequest r)` (Task 3 قرارداد را می‌سازد؛ در این Task فقط ثابت‌ها).
- Produces (Api entities، همه `IAudited` جز `StockMovement`):
  - `Product { Id, Name, NameKey, Brand?, BrandKey (""=بی‌برند), PackDescription?, PackKey (""), LocationId?, MinStock (int?), UnitsPerPack = 1, RequiresExpiry = true, AllowExpiredWithdrawal = false, IsActive = true, SearchText, CreatedAtUtc, Version }` — unique `(NameKey, BrandKey, PackKey)`؛ کلیدهای خالی رشته‌ی خالی‌اند، نه NULL (همان دلیل `ParentKey`).
  - `ProductBarcode { Id, ProductId, Code, CodeKey, Version }` — unique `CodeKey`.
  - `Batch { Id, ProductId, BatchNumber, BatchKey, ExpiryDate (DateOnly?), ExpiryPrecision, ExpiryCalendar, LocationId?, UnitCost (long?), OnHand, CreatedAtUtc, Version }` — unique `(ProductId, BatchKey)`؛ enumها به رشته.
  - `StockMovement { Id, BatchId, Kind, Quantity, AtUtc, OccurredAtUtc, UserId?, WithdrawalId?, StocktakeCountId?, ReversesId?, Note? }` — index `(BatchId, AtUtc)`.
  - FKها همه `Restrict`.
- Produces (`StockLedger`، scoped):
  `public sealed class StockLedger { public StockLedger(WarehouseDbContext db, AuditContext audit); public void Post(Batch batch, StockMovementKind kind, int quantity, DateTime occurredAtUtc, string? note = null); }` — حرکت را Add و `batch.OnHand += quantity` می‌کند؛ اگر نتیجه منفی ← `InvalidOperationException`؛ `SaveChanges` را صدا **نمی‌زند** (فراخواننده یک تراکنش دارد). `Version` بچ را رهگیر زیاد می‌کند، پس دو ذخیره‌ی هم‌زمان روی یک بچ یکی ۴۰۹ می‌گیرد (فراخواننده `db.ExpectVersion(batch, batch.Version)` را قبل از `Post` صدا می‌زند). تنها کدی که `OnHand` را عوض می‌کند همین کلاس است.
- `AuditSubject.Of`: `Product` ← `Id`؛ `Batch` و `ProductBarcode` ← `ProductId`.
- `TestApp`: `public Task<List<StockMovement>> MovementsAsync(Guid batchId)`؛ `public Task AssertLedgerConsistentAsync()` (برای هر بچ `OnHand == Σ Quantity`؛ Assert.Equal با نام بچ).

- [ ] **Step 1: تست‌ها (ناموفق)**

```csharp
StockMovements_CannotBeUpdatedOrDeleted       // ExecuteRawAsync UPDATE/DELETE ← SqliteException "append-only"
Batch_OnHandBelowZero_IsRejectedByCheck       // ExecuteRawAsync INSERT بچ با OnHand -1 ← SqliteException
Triggers_ExistAfterMigration                  // sqlite_master شامل StockMovements_NoUpdate/NoDelete
// StorageLocationTests:
Delete_LocationWithProduct_Returns409AndListsIt     // قلم با LocationId این محل (درج مستقیم از TestApp یا بعد از Task 3 با API — در این Task با کمک‌کننده‌ی TestApp.AddProductRawAsync)
Delete_LocationWithBatch_Returns409AndListsIt
```

`TestApp.AddProductRawAsync(string name, Guid? locationId)` و `AddBatchRawAsync(Guid productId, string batch, Guid? locationId)` فقط برای تست (از DbContext، با رهگیر).

- [ ] **Step 2: موجودیت‌ها، DbContext، `StockLedger` (ثبت در DI)، `AuditSubject`، وابستگی‌های محل («قلم» با `SubjectId` = قلم، «بچ» با برچسب «قلم — بچ»)، migration؛ PASS**

- [ ] **Step 3: کل تست‌ها سبز؛ Commit** — `"Add product, barcode, batch and stock movement tables"`

---

### Task 3: endpointهای قلم و بارکد

**Files:**
- Create: `src/PharmacyWarehouse.Domain/Contracts/ProductContracts.cs`، `src/PharmacyWarehouse.Api/Stock/ProductEndpoints.cs`
- Modify: `ApiRoutes.cs` (`Products = "/api/products"`، `ProductByCode(string code)`، `ProductBarcodes(Guid id)`، `ProductBarcode(Guid id, Guid barcodeId)`، `ProductHistory(Guid id)`)، `ProductRules.cs` (`Validate`)، `Program.cs`
- Test: `tests/PharmacyWarehouse.Api.Tests/ProductTests.cs`، `BarcodeTests.cs`

**Interfaces:**
- Produces (contracts):
  - `SaveProductRequest { string Name; string? Brand; string? PackDescription; Guid? LocationId; int? MinStock; bool RequiresExpiry = true; bool AllowExpiredWithdrawal; int Version }`
  - `ProductListItem(Guid Id, string Name, string? Brand, string? PackDescription, string? LocationPath, int OnHand, int UsableOnHand, int? MinStock, DateOnly? NearestExpiry, bool IsActive, int Version)` — `UsableOnHand` = موجودی بچ‌های منقضی‌نشده و بی‌تاریخ (امروز به وقت `LocalDay`).
  - `BatchItem(Guid Id, string BatchNumber, DateOnly? ExpiryDate, ExpiryPrecision Precision, CalendarKind Calendar, string? ExpiryText, string? LocationPath, int OnHand, bool IsExpired, int Version)` — `ExpiryText` = `ExpiryDate.Display`.
  - `BarcodeItem(Guid Id, string Code)`
  - `MovementItem(Guid Id, Guid BatchId, string BatchNumber, StockMovementKind Kind, int Quantity, DateTime AtUtc, DateTime OccurredAtUtc, string UserName, string? Note)`
  - `ProductDetail { ProductListItem Summary; bool RequiresExpiry; bool AllowExpiredWithdrawal; Guid? LocationId; List<BatchItem> Batches; List<BarcodeItem> Barcodes; }`
- Endpoints:
  - `GET /api/products?q=&locationId=&filter=` (`stock.view`) — `q` با `SearchKey` روی `SearchText` (نام + برند + بسته + بارکدها)؛ `locationId` شامل زیرشاخه‌ها (با `LocationTree`)؛ `filter`: `zero` (OnHand=0)، `belowMin` (`UsableOnHand < MinStock`)، `expiring` (بچ با موجودی و انقضا تا ۱۸۰ روز؛ برش ۵ با تنظیمات جایگزین می‌شود)؛ مرتب با نام در حافظه؛ غیرفعال‌ها فقط با `includeInactive=true`.
  - `GET /api/products/by-code/{code}` (`stock.view`) — `ScanKey`؛ پیدا نشد ← ۴۰۴.
  - `GET /api/products/{id}` ← `ProductDetail`؛ `GET /api/products/{id}/history` ← `{ List<MovementItem> Movements; List<AuditEntryItem> Changes }` (`stock.view`).
  - `POST`، `PUT /{id}`، `POST /{id}/deactivate|activate`، `GET /{id}/dependencies`، `DELETE /{id}` (`products.manage`) — حذف فقط بدون بچ (۴۰۹ با فهرست بچ‌ها)؛ حذف قلم بارکدهایش را هم حذف می‌کند.
  - `POST /api/products/{id}/barcodes` بدنه `{ string Code }`، `DELETE /api/products/{id}/barcodes/{barcodeId}` (`products.manage`) — تکراری بین همه‌ی اقلام ← ۴۰۹ با پیام «این بارکد مال «{نام قلم}» است».
  - اعتبارسنجی (`ProductRules.Validate`): نام اجباری؛ طول‌ها؛ `MinStock ≥ 0`؛ `LocationId` موجود و فعال (۴۰۰).
  - `RequiresExpiry` روشن روی قلمی که بچ بی‌تاریخ دارد مجاز است؛ بچ‌های موجود دست نمی‌خورند (Review Focus ۵).

- [ ] **Step 1: تست‌ها (ناموفق)**

```csharp
// ProductTests
Create_WithoutStock_ShowsZero                        // ProductListItem.OnHand == 0, UsableOnHand == 0
DuplicateNameBrandPack_AfterNormalizing_Returns409   // "کو آموکسی کلاو 625" و "کوآموکسی کلاو 625"، برند و بسته یکسان
SameName_DifferentPack_IsAllowed
Staff_CanList_ButNotCreate                           // AddStaffAsync: GET 200، POST 403
Search_FindsByBrandAndBarcode
FilterByLocation_IncludesDescendants
FilterBelowMin_UsesUnexpiredStockOnly                // بچ منقضی با موجودی، MinStock=5، UsableOnHand=0 ← در belowMin
Update_StaleVersion_Returns409
Delete_WithBatch_Returns409                          // AddBatchRawAsync
Delete_WithoutBatch_RemovesBarcodes_AndIsAudited
TurnOnRequiresExpiry_WithUndatedBatch_KeepsBatch     // Review Focus ۵
History_ListsChangesOfProductAndItsBarcodes          // SubjectId
// BarcodeTests
AddBarcode_ThenFindByCode
FindByCode_PersianKeyboardInput_FindsSameProduct     // ثبت "AB12"، جست‌وجو "شذ12" (Review Focus ۱)
DuplicateBarcode_OnOtherProduct_Returns409WithItsName
UnknownCode_Returns404
Staff_CannotAddBarcode
```

- [ ] **Step 2: پیاده‌سازی؛ PASS؛ کل تست‌ها سبز**

- [ ] **Step 3: Commit** — `"Add product and barcode endpoints"`

---

### Task 4: ورود جنس

**Files:**
- Create: `src/PharmacyWarehouse.Domain/Contracts/ReceiptContracts.cs`، `src/PharmacyWarehouse.Api/Stock/ReceiptEndpoints.cs`
- Modify: `ApiRoutes.cs` (`Receipts = "/api/receipts"`)، `Program.cs`
- Test: `tests/PharmacyWarehouse.Api.Tests/ReceiptTests.cs`

**Interfaces:**
- Consumes: `StockLedger.Post` (Task 2)، `ExpiryDate.Parse` (Task 1).
- Produces:
  - `ReceiptRequest { Guid ProductId; string BatchNumber; string? Expiry; Guid? LocationId; long? UnitCost; int Quantity; string? Note; int? BatchVersion }` — `Expiry` متن ورودی (هر قالب بخش ۱۲.۲ که `Parse` قبول می‌کند)؛ کلاینت متن میلادی کامل می‌فرستد. `BatchVersion` برای بچ موجود (از `BatchItem.Version` که کلاینت دیده)؛ برای بچ تازه null.
  - پاسخ: `ReceiptResult(BatchItem Batch, bool CreatedBatch, IReadOnlyList<ExpiryWarning> Warnings)`.
- قواعد (بخش ۷.۳):
  - `Quantity > 0` (۴۰۰)؛ قلم فعال (۴۰۰)؛ `BatchNumber` بعد از `Clean` اجباری، کلید = `ScanKey`.
  - بچ موجود (`ProductId` + `BatchKey`): تاریخ متفاوت از ثبت‌شده ← ۴۰۹ «یک بچ دو تاریخ ندارد؛ بچ نامبر را بررسی کنید»؛ تاریخ خالی در درخواست = همان تاریخ ثبت‌شده؛ `BatchVersion` ناسازگار ← ۴۰۹.
  - بچ تازه: `RequiresExpiry` و تاریخ خالی ← ۴۰۰؛ `Parse.Error` ← ۴۰۰ با همان متن.
  - حرکت `Receipt` با `OccurredAtUtc = AtUtc = اکنون`؛ یک `SaveChanges`.
- Test (`ReceiptTests`):

```csharp
NewBatch_CreatesBatchAndMovement_AndLedgerIsConsistent   // AssertLedgerConsistentAsync
ExistingBatch_AddsQuantity
ExistingBatch_DifferentExpiry_Returns409
ExistingBatch_StaleVersion_Returns409
JalaliMonthExpiry_StoredAsEndOfJalaliMonth               // "04/1408" ← 2029-07-21، Precision Month، Calendar Jalali، هشدارها در پاسخ
RequiresExpiry_MissingExpiry_Returns400
NoExpiryNeeded_UndatedBatchAllowed
ZeroOrNegativeQuantity_Returns400
InvalidExpiry_Returns400WithMessage                      // "13/062028"
Staff_CannotReceive                                      // 403
ConcurrentReceipts_SameBatch_NoLostUpdate                // دو درخواست با همان BatchVersion: یکی 200، یکی 409؛ OnHand = مقدار موفق؛ ledger سازگار (Review Focus ۲)
BatchNumberFromPersianKeyboard_MatchesExisting           // ثبت "AB12"، ورود دوم با "شذ12" ← همان بچ
```

- [ ] **Step 1: تست‌ها (ناموفق)؛ Step 2: پیاده‌سازی؛ PASS؛ کل تست‌ها سبز؛ Step 3: Commit** — `"Add stock receipt"`

---

### Task 5: کلاینت — اقلام، جزئیات، تعریف قلم، بارکد

**Files:**
- Create: `Services/ProductsClient.cs`، `Components/ScanInput.razor`، `Pages/Products.razor` (`@page "/"` و `/products`؛ `/` را از `Locations.razor` بردار)، `Pages/ProductDetails.razor` (`/products/{id:guid}`)، `Pages/ProductEdit.razor` (`/products/new`، `/products/{id:guid}/edit`)
- Modify: `Layout/NavMenu.razor` («اقلام» اول، «ورود جنس» با `stock.receive`، «محل‌ها»)، `Components/DeleteDialog.razor` (پیوند `SubjectId` ← `products/{id}`)، `Program.cs`، `wwwroot/css/app.css` (حذف CSS مرده‌ی تجهیزات، ledger Task 4 برش ۰)

**Interfaces:**
- Produces: `ScanInput` — پارامترها `EventCallback<string> OnScan`، `string Placeholder`، `bool AutoFocus = true`؛ با Enter مقدار خام را می‌فرستد و فیلد را خالی می‌کند و فوکوس را نگه می‌دارد؛ متد عمومی `FocusAsync()`.
- رفتار صفحه‌ها (بخش ۱۵ spec):
  - اقلام: فیلد اسکن + جست‌وجو؛ فیلتر محل (درخت)، «موجودی صفر»، «کمتر از حداقل»، «نزدیک انقضا»؛ ستون‌ها: نام، برند، بسته، محل، موجودی، نزدیک‌ترین انقضا (`ExpiryDate.Display`)؛ اسکن بارکد شناخته‌شده ← جزئیات؛ ناشناخته ← برای `products.manage`: پنجره‌ی «این بارکد مال کدام قلم است؟» با جست‌وجو و دکمه‌ی ثبت؛ برای بقیه: پیام «بارکد ناشناخته؛ با نام جست‌وجو کنید و به انباردار بگویید».
  - جزئیات: خلاصه، بچ‌ها (محل، انقضا، موجودی، منقضی با نشان قرمز)، بارکدها (افزودن/حذف با مجوز)، حرکت‌ها، تغییرات.
  - تعریف/ویرایش: نام و دوز، برند، محتوای بسته، محل (انتخاب‌گر مسیر)، حداقل موجودی، تیک «انقضا لازم است» و «برداشت از بچ منقضی مجاز است» (کنارش هشدار: «برای کالای غیردارویی؛ خروج داروی منقضی خطر دارد»).

- [ ] **Step 1: پیاده‌سازی؛ build بدون خطا و هشدار**
- [ ] **Step 2: Commit** — `"Add product pages and scan input"`

(تأیید مرورگر: کنترل‌کننده بعد از Task 6 با Playwright.)

---

### Task 6: کلاینت — ورود جنس با دو فیلد تاریخ؛ مستندات

**Files:**
- Create: `Components/ExpiryFields.razor`، `Pages/Receive.razor` (`/receive`، و `/receive?productId=`)
- Modify: `docs/brief.md` (بخش ۷)، `docs/spec.md` (بخش ۱۸ اگر انحرافی بود)

**Interfaces:**
- `ExpiryFields`: پارامترهای `string? Value` / `EventCallback<string?> ValueChanged` (متن میلادی کامل `dd/MM/yyyy` یا `MM/yyyy`، همان که به Api می‌رود)، `bool Required`. دو `input`: «میلادی» (`dir="ltr"`) و «شمسی»؛ با `@onchange` هر کدام، `ExpiryDate.Parse` روی کلاینت؛ موفق ← فیلد دیگر با `ExpiryDate.Jalali`/`Gregorian` پر و هشدارها زیر فیلد؛ خطا ← متن خطا. ورودی ماه‌دار (`04/1408`) در فیلد دیگر به روز مشخص تبدیل می‌شود (`21/07/2029`) با زیرنویس «دقت: ماه — آخر تیر ۱۴۰۸»؛ مقدار ارسالی همان متن **ورودی کاربر** است تا سرور دقت و تقویم را درست ثبت کند.
- `Receive.razor`: فیلد اسکن (بارکد قلم)؛ یا جست‌وجوی نام؛ بعد از انتخاب قلم: فهرست بچ‌های موجود (انتخاب یکی ← تاریخ قفل و نمایشی) یا «بچ تازه» (بچ نامبر، `ExpiryFields`، محل اختیاری، قیمت اختیاری)؛ تعداد با − و +؛ ثبت؛ پاسخ موفق ← پیام و آماده‌ی قلم بعدی (فوکوس روی اسکن)؛ ۴۰۹ ← پیام سرور و بارگذاری دوباره‌ی بچ‌ها. بچ نامبر هم از بارکدخوان پر می‌شود (همان `ScanKey` روی سرور).
- [نامطمئن spec §6] `PersianCalendar` در WASM: اگر در مرورگر کار نکرد، کنترل‌کننده با Playwright می‌بیند و یک Task اصلاحی (تبدیل روی سرور با `GET /api/expiry/parse?text=`) می‌سازد.

- [ ] **Step 1: پیاده‌سازی؛ build بدون خطا؛ کل تست‌ها سبز**
- [ ] **Step 2: `docs/brief.md` بخش ۷: برش ۱ تمام، دستور migration، برش ۲ بعدی**
- [ ] **Step 3: Commit** — `"Add stock receipt page with dual-calendar expiry"`

---

## پایان برش: تأیید مرورگر (کنترل‌کننده با Playwright، سپس امیر)

کنترل‌کننده: Api را با دیتابیس موقت بالا می‌آورد، `create-admin`، و با Playwright: ورود ← تعریف قلم `TEST-DRUG-01` ← افزودن بارکد ← ورود جنس با تاریخ شمسی `04/1408` و دیدن پر شدن خودکار فیلد میلادی ← جزئیات قلم و حرکت. اسکرین‌شات‌ها در scratchpad.

امیر (ویندوز): `git pull`، حذف دیتابیس قدیمی، `dotnet ef database update --project src\PharmacyWarehouse.Api` روی هر دو سیستم، `run-dev.bat`، همان گام‌ها با بارکدخوان واقعی وقتی در دسترس بود.
