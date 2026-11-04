# برش ۰ — پایه از Emu: برنامه‌ی پیاده‌سازی

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** مخزن `pharmacywarehouse` با زیرساخت کامل Emu (ورود، مجوز، تاریخچه، پشتیبان، استقرار) با نام تازه، بدون دامنه‌ی تجهیزات، با محل‌های درختی به‌عنوان اولین موجودیت دامنه، و همه‌ی تست‌ها سبز.

**Architecture:** کپی کامل Emu و تغییر نام مکانیکی (تست‌ها سبز می‌مانند) ← افزودن `StorageLocation` در کنار تجهیزات ← انتقال تست‌های زیرساخت به `StorageLocation` ← حذف تجهیزات و migration تازه ← مستندات. هر Task با build و تست سبز تمام می‌شود.

**Tech Stack:** .NET 10 (SDK `10.0.402` طبق `global.json`)، ASP.NET Core Minimal API، EF Core SQLite `10.0.14`، Identity، Blazor WebAssembly، xunit `2.9.3`.

**Spec:** `docs/spec.md` (بخش‌های ۳، ۸، ۱۱، ۱۳، ۱۶، ۱۷، ۱۸).

**منبع:** Emu در `/home/user/equipmentmanagementutility` (commit `22afe9c`). **Emu تغییر نمی‌کند.**

## Global Constraints

- نام: پروژه‌ها `PharmacyWarehouse.Api`، `PharmacyWarehouse.Client`، `PharmacyWarehouse.Domain`، `PharmacyWarehouse.Api.Tests`، `PharmacyWarehouse.Domain.Tests`؛ solution `PharmacyWarehouse.sln`.
- نسخه‌ی بسته‌ها و SDK دقیقاً همان Emu (`.csproj`، `global.json`، `dotnet-tools.json`)؛ هیچ بسته‌ی تازه.
- پورت‌ها [تصمیم]: Api `https://localhost:7500`، Client `https://localhost:7600`.
- کوکی `PharmacyWarehouse.Auth`؛ هدر CSRF مقدار `PharmacyWarehouse`؛ دیتابیس `pharmacywarehouse.db`.
- نشست [تصمیم]: `Auth:CookieLifetimeHours = 720` در `appsettings.json`؛ غیرفعال کردن کاربر نشست را فوراً باطل می‌کند (موجود در Emu).
- اعلان پشتیبان [تصمیم]: `BackupWarnDays = 1`.
- قواعد کد Emu (`CLAUDE.md` Emu، بخش «قواعد کد») همه برقرارند: رهگیر تاریخچه، `Version`، `TextNormalizer`، ۴۰۹ برای وابستگی، `no-store`، CSRF، `ApiResult`/`ApiSender`.
- داده‌ی تست ساختگی (`TEST-LOC-01`)؛ هیچ داده‌ی واقعی. فایل Excel داروخانه هرگز در مخزن نیست.
- متن رابط فارسی؛ کد و مسیرها انگلیسی. فایل `.bat`: بدون `&` و `%` در متن راهنما.
- commit فقط محلی؛ push با درخواست امیر.

## Review Focus

1. **نام قدیمی جامانده در پیکربندی و اسکریپت** (نه فقط C#): `Emu` در `deploy/*.ps1`، `publish-package.bat`، `run-dev.bat`، `launchSettings.json`، `index.html` (عنوان صفحه)، نام سرویس و پوشه‌ی داده. اگر بماند، نصب روی سروری که Emu هم دارد سرویس Emu را بازنویسی می‌کند. ← Task 5، گام grep.
2. **مسیر محل با عمق زیاد یا تغییر پدر:** جابه‌جا کردن یک گره زیر نواده‌ی خودش باید رد شود، و `Path` همه‌ی نواده‌ها بعد از تغییر نام پدر درست نمایش داده شود. ← Task 2.
3. **یکتایی نام بین هم‌شاخه‌ها در ریشه:** `ParentId = null` در SQLite در unique index برابر حساب نمی‌شود؛ دو محل سطح اول هم‌نام باید ۴۰۹ بگیرند. ← Task 2.
4. **کاربر غیرفعال روی کامپیوتر مشترک:** درخواست بعدی همان کوکی ۴۰۱. ← Task 3 (تست Emu نگه داشته می‌شود).
5. **trigger فقط‌اضافه‌شونده بعد از migration تازه:** بازسازی migration نباید trigger را جا بیندازد. ← Task 4.

---

## File Structure

| مسیر | مسئولیت |
|---|---|
| `src/PharmacyWarehouse.Domain/Auth/Permissions.cs`، `Roles.cs` | فهرست مجوزها و نقش‌های بخش ۱۱ |
| `src/PharmacyWarehouse.Domain/Contracts/StorageLocationContracts.cs` | قرارداد محل |
| `src/PharmacyWarehouse.Domain/Storage/LocationTree.cs` | محاسبه‌ی مسیر و تشخیص حلقه (بدون EF) |
| `src/PharmacyWarehouse.Api/Data/Entities.cs` | `StorageLocation`؛ `AuditEntry.SubjectId` |
| `src/PharmacyWarehouse.Api/Data/AuditSubject.cs` | نگاشت موجودیت ← `SubjectId` تاریخچه |
| `src/PharmacyWarehouse.Api/Storage/StorageLocationEndpoints.cs` | endpointهای محل |
| `src/PharmacyWarehouse.Client/Pages/Locations.razor`، `Services/LocationsClient.cs` | صفحه‌ی درخت محل |
| `tests/PharmacyWarehouse.Api.Tests/StorageLocationTests.cs` | تست محل |
| `tests/PharmacyWarehouse.Domain.Tests/LocationTreeTests.cs`، `PermissionsTests.cs` | تست Domain |
| `CLAUDE.md`، `docs/brief.md`، `docs/deploy.md` | مستندات پروژه |

---

### Task 1: کپی Emu و تغییر نام مکانیکی

**Files:**
- Create: همه‌ی فایل‌های Emu به‌جز `.git/`، `docs/`، `CLAUDE.md`، `bin/`، `obj/`، `*.db*`
- Rename: پوشه‌ها و فایل‌های `Emu.*` ← `PharmacyWarehouse.*`؛ `Emu.sln` ← `PharmacyWarehouse.sln`

**Interfaces:**
- Produces: `PharmacyWarehouse.Api.Data.WarehouseDbContext` (به جای `EmuDbContext`)، `AppPasswordValidator` (به جای `EmuPasswordValidator`)، خاصیت MSBuild `WarehousePackage` (به جای `EmuPackage`). بقیه‌ی نام‌ها فقط پیشوند namespace عوض می‌کنند.

- [ ] **Step 1: نصب SDK در محیط (فقط اگر `dotnet` نیست)**

`packages.microsoft.com` در دسترس است؛ `builds.dotnet.microsoft.com` نیست. از مخزن apt مایکروسافت `dotnet-sdk-10.0` نصب شود. اگر نسخه‌ی نصب‌شده در باند `10.0.4xx` نبود، `global.json` تغییر **نمی‌کند**؛ برای اجرای محلی `DOTNET_ROLL_FORWARD`/`--sdk` بررسی و در گزارش گفته شود. اگر هیچ راهی نشد، کار متوقف و به امیر گزارش شود (تست‌ها روی ویندوز امیر).

Run: `dotnet --version`
Expected: `10.0.4xx`

- [ ] **Step 2: کپی و تغییر نام**

کپی با `rsync -a --exclude .git --exclude docs --exclude CLAUDE.md --exclude bin --exclude obj --exclude '*.db*'`. سپس:
- نام پوشه‌ها و فایل‌ها: `Emu.` ← `PharmacyWarehouse.`؛ `EmuDbContext*` ← `WarehouseDbContext*`؛ `EmuPasswordValidator.cs` ← `AppPasswordValidator.cs`.
- محتوا (فقط شناسه‌ها، با مرز کلمه): `Emu.` ← `PharmacyWarehouse.`، `EmuDbContext` ← `WarehouseDbContext`، `EmuPasswordValidator` ← `AppPasswordValidator`، `EmuPackage` ← `WarehousePackage`، `"Emu.Auth"` ← `"PharmacyWarehouse.Auth"`، مقدار هدر CSRF ← `PharmacyWarehouse`، `emu.db` ← `pharmacywarehouse.db`.
- پورت‌ها: `7300` ← `7500`، `7400` ← `7600` در `launchSettings.json` (هر دو)، `src/PharmacyWarehouse.Api/appsettings.Development.json` (`AllowedOrigins`)، `src/PharmacyWarehouse.Client/wwwroot/appsettings.Development.json` (`ApiBaseUrl`).
- `Directory.Build.props`: `<Version>0.1.0</Version>`.
- **دست نزن** به: پوشه‌ی `Data/Migrations` (Task 4 آن را کامل بازسازی می‌کند)، `deploy/`، `publish-package.bat`، `run-dev.bat` (Task 5).

- [ ] **Step 3: build و همه‌ی تست‌ها**

Run: `dotnet build PharmacyWarehouse.sln && dotnet test PharmacyWarehouse.sln`
Expected: build بدون خطا؛ همه‌ی تست‌ها PASS (همان تعداد تست Emu). شکست = تغییر نام ناقص، نه باگ Emu.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "Copy Emu infrastructure and rename to PharmacyWarehouse"
```

---

### Task 2: محل‌های درختی (`StorageLocation`)

**Files:**
- Create: `src/PharmacyWarehouse.Domain/Storage/LocationTree.cs`
- Create: `src/PharmacyWarehouse.Domain/Contracts/StorageLocationContracts.cs`
- Create: `src/PharmacyWarehouse.Api/Storage/StorageLocationEndpoints.cs`
- Modify: `src/PharmacyWarehouse.Api/Data/Entities.cs`، `Data/WarehouseDbContext.cs`، `Program.cs` (`app.MapStorageLocationEndpoints()`)
- Modify: `src/PharmacyWarehouse.Domain/Contracts/ApiRoutes.cs` (`Locations = "/api/locations"`)
- Modify: `src/PharmacyWarehouse.Domain/Auth/Permissions.cs` (افزودن `StockView = "stock.view"`، `ProductsManage = "products.manage"` کنار مجوزهای قدیمی؛ Task 4 قدیمی‌ها را حذف می‌کند)
- Create: `src/PharmacyWarehouse.Client/Services/LocationsClient.cs`، `Pages/Locations.razor` (مسیر `/locations`)
- Test: `tests/PharmacyWarehouse.Domain.Tests/LocationTreeTests.cs`، `tests/PharmacyWarehouse.Api.Tests/StorageLocationTests.cs`
- Migration موقت: `dotnet ef migrations add StorageLocations` (Task 4 همه را در یک Initial ادغام می‌کند)

**Interfaces:**
- Produces (Domain):
  - `public sealed record LocationNode(Guid Id, Guid? ParentId, string Name, int SortOrder);`
  - `public static class LocationTree { public const int MaxName = 100; public const string Separator = " › "; public static IReadOnlyDictionary<Guid, string> Paths(IEnumerable<LocationNode> nodes); public static bool WouldCreateCycle(IEnumerable<LocationNode> nodes, Guid id, Guid? newParentId); }`
  - `public sealed record StorageLocationItem(Guid Id, Guid? ParentId, string Name, string Path, int SortOrder, bool IsActive, int Version);`
  - `public sealed class SaveStorageLocationRequest { public string Name { get; set; } = ""; public Guid? ParentId { get; set; } public int SortOrder { get; set; } public int Version { get; set; } }`
- Produces (Api): `public class StorageLocation : IAudited { Guid Id; Guid? ParentId; string Name; string NameKey; string ParentKey; int SortOrder; bool IsActive = true; int Version = 1; }` — `ParentKey` = `ParentId?.ToString() ?? ""`، برای یکتایی (unique index روی `ParentKey` + `NameKey`، چون NULL در unique index SQLite برابر نیست).
- Endpoints (الگوی ساختمان/واحد در `Locations/LocationEndpoints.cs` Emu، شامل `Deactivate`/`Activate`/`Dependencies`):
  - `GET /api/locations` ← `List<StorageLocationItem>` مرتب بر اساس مسیر و `SortOrder` (`stock.view`)
  - `POST /api/locations`، `PUT /api/locations/{id}`، `POST .../deactivate`، `POST .../activate`، `GET .../dependencies`، `DELETE /api/locations/{id}` (`products.manage`)

- [ ] **Step 1: تست‌های Domain (ناموفق)**

```csharp
[Fact] public void Paths_JoinAncestors()
// ضلع جنوبی › قفسه ۲ › طبقه ۳ › قسمت B
{ var a=N("ضلع جنوبی"); var b=N("قفسه ۲",a); var c=N("طبقه ۳",b); var d=N("قسمت B",c);
  Assert.Equal("ضلع جنوبی › قفسه ۲ › طبقه ۳ › قسمت B", LocationTree.Paths([a,b,c,d])[d.Id]); }
[Fact] public void Cycle_SelfParent_IsDetected()            // WouldCreateCycle(nodes, a.Id, a.Id) == true
[Fact] public void Cycle_MoveUnderDescendant_IsDetected()   // a ← b ← c ؛ WouldCreateCycle(nodes, a.Id, c.Id) == true
[Fact] public void MoveToRootOrSibling_IsNotCycle()         // WouldCreateCycle(nodes, c.Id, null) == false ؛ (c.Id, a.Id) == false
[Fact] public void Paths_BrokenParent_DoesNotLoop()         // گره با ParentId ناموجود: مسیرش فقط نام خودش؛ بدون حلقه‌ی بی‌پایان
```

Run: `dotnet test tests/PharmacyWarehouse.Domain.Tests --filter LocationTreeTests`
Expected: FAIL (`LocationTree` تعریف نشده)

- [ ] **Step 2: پیاده‌سازی `LocationTree`؛ تست‌ها PASS**

- [ ] **Step 3: تست‌های Api (ناموفق)** در `StorageLocationTests : ApiTestBase`

```csharp
List_WithoutLogin_Returns401
UserWithoutRole_GetsForbidden                // AddUserWithoutRoleAsync ← GET و POST هر دو 403 (دسترسی نقش‌ها در Task 4 با StaffPolicyTests)
Create_Child_ReturnsFullPath                 // POST ریشه "TEST-LOC-01"، POST فرزند ← Path == "TEST-LOC-01 › TEST-LOC-02"
DuplicateNameAmongSiblings_AfterNormalizing_Returns409   // "قفسه ۲" و "قفسه 2" زیر یک پدر ← ۴۰۹
DuplicateRootName_Returns409                 // دو ریشه‌ی هم‌نام ← ۴۰۹ (Review Focus ۳)
SameName_UnderDifferentParents_IsAllowed
MoveUnderOwnDescendant_Returns409            // Review Focus ۲
RenameParent_ChildPathFollows                // PUT پدر با نام تازه ← GET فرزند Path تازه
TwoEditsWithSameVersion_SecondReturns409
Delete_WithChildren_Returns409WithDependencies
Delete_Leaf_Succeeds_AndIsAudited            // CountAuditAsync(id, "StorageLocation") >= 2
Deactivate_ThenActivate_FlipsFlag
```

Run: `dotnet test tests/PharmacyWarehouse.Api.Tests --filter StorageLocationTests`
Expected: FAIL

- [ ] **Step 4: موجودیت، DbContext (unique index `ParentKey`+`NameKey`)، endpointها، migration موقت**

اعتبارسنجی: نام بعد از `TextNormalizer.Clean` خالی ← ۴۰۰؛ طول > `LocationTree.MaxName` ← ۴۰۰؛ `ParentId` ناموجود ← ۴۰۰؛ حلقه (`WouldCreateCycle`) ← ۴۰۹؛ وابستگی‌های حذف در این برش فقط «زیرشاخه‌ها» (برش ۱ «اقلام» و «بچ‌ها» را اضافه می‌کند).

Run: `dotnet ef migrations add StorageLocations --project src/PharmacyWarehouse.Api` سپس تست‌های Step 3
Expected: PASS

- [ ] **Step 5: صفحه‌ی `Locations.razor`**

درخت تورفته با مسیر کامل؛ افزودن ریشه و زیرشاخه؛ ویرایش نام و پدر (انتخاب‌گر پدر بدون خود گره و نواده‌هایش)؛ غیرفعال/فعال؛ حذف با `DeleteDialog`. همه‌ی فراخوانی‌ها با `ApiSender`/`ApiResult<T>`. پیوند در `NavMenu` با `RequirePermission` روی `stock.view`.

Run: `dotnet build PharmacyWarehouse.sln`
Expected: بدون خطا. (آزمون مرورگر با امیر در پایان برش.)

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Add hierarchical storage locations"
```

---

### Task 3: انتقال تست‌های زیرساخت از تجهیزات به محل؛ `SubjectId` تاریخچه

**Files:**
- Modify: `src/PharmacyWarehouse.Api/Data/Entities.cs` (`AuditEntry.EquipmentId` ← `SubjectId`)
- Create: `src/PharmacyWarehouse.Api/Data/AuditSubject.cs`
- Modify: `src/PharmacyWarehouse.Api/Data/AuditInterceptor.cs` (استفاده از `AuditSubject.Of`)
- Modify (تست): `AuditTests.cs`، `DeleteTests.cs`، `UserTests.cs`، `SessionTests.cs` — هر جا تجهیزات فقط «یک موجودیت نمونه» است، با `StorageLocation` جایگزین شود.

**Interfaces:**
- Produces: `internal static class AuditSubject { public static Guid? Of(object entity); }` — برای `StorageLocation` ← `Id`. برش ۱ `Product` ← `Id`، `Batch`/`ProductBarcode` ← `ProductId` را اضافه می‌کند. تا Task 4 شاخه‌های `Equipment`/`Component`/`RepairRecord` همان منطق Emu را نگه می‌دارند.
- `AuditEntry.SubjectId` جایگزین `EquipmentId` در قرارداد تاریخچه‌ی کلاینت (`HistoryTimeline`, `DeleteDialog`) هم می‌شود.

- [ ] **Step 1: تست‌های تاریخچه‌ی محل (ناموفق)** در `AuditTests`

```csharp
LocationRename_WritesOldAndNewValue          // Field == "Name"، OldValue/NewValue درست، SubjectId == location.Id
AuditEntries_CannotBeUpdatedOrDeleted        // ExecuteRawAsync("UPDATE AuditEntries ...") ← SqliteException 'audit is append-only' (از Emu)
SameChangeSet_ForOneRequest                  // یک PUT ← همه‌ی ردیف‌ها یک ChangeSetId
```

Run: `dotnet test tests/PharmacyWarehouse.Api.Tests --filter AuditTests`
Expected: FAIL (`SubjectId` وجود ندارد)

- [ ] **Step 2: `SubjectId` و `AuditSubject.Of`؛ migration موقت `AuditSubject`؛ تست‌ها PASS**

- [ ] **Step 3: بازنویسی تست‌های Delete/User/Session که به تجهیزات تکیه دارند**

`UserTests` خط‌های `ApiRoutes.Equipment` ← `ApiRoutes.Locations`. `Deactivate_EndsSessionNow_AndBlocksLogin_ActivateRestores` و تست `SetIsActiveRawAsync` در `SessionTests` **بدون تغییر منطق** باقی می‌مانند (Review Focus ۴). تست‌های `DeleteTests` برای وابستگی، روی محل با زیرشاخه.

Run: `dotnet test PharmacyWarehouse.sln`
Expected: همه PASS

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "Move audit and infrastructure tests onto storage locations"
```

---

### Task 4: حذف دامنه‌ی تجهیزات، مجوزها و نقش‌های تازه، migration اولیه

**Files (حذف):**
- Domain: `Assets/*`، `Contracts/EquipmentContracts.cs`، `Contracts/LocationContracts.cs`، مسیرهای تجهیزات/ساختمان/طبقه/واحد/تعمیر/گزارش و `DataTemplate`/`DataExport`/`DataImport*` در `ApiRoutes.cs`، `Contracts/ReportContracts.cs`
- Api: `Assets/`، `Locations/`، `Reports/`، `DataTools/EquipmentSheet.cs`، از `DataTools/DataEndpoints.cs` همه‌چیز جز `BackupAsync` (فایل به `DataTools/BackupEndpoints.cs` تغییر نام)، از `Alerts/AlertEndpoints.cs` همه‌ی اعلان‌ها جز پشتیبان، `seed-demo` از `Operations/OperationCommands.cs`، فراخوانی `EquipmentEndpoints.WarmUpAsync` در `WarmUpService.cs` (WarmUp یک `SELECT` ساده روی `StorageLocations`)، موجودیت‌های `Building`/`Floor`/`Unit`/`Equipment`/`Component`/`RepairRecord`، همه‌ی `Data/Migrations/*`
- Client: `Pages/Equipment*.razor`، `Stocktake.razor`، `Reports.razor`، `AdminLocations.razor`، `Components/EquipmentForm.razor`، `BulkActionDialog.razor`، `BulkCreateDialog.razor`، `LocationFields.razor`، `StatusBadge.razor`، `Services/EquipmentClient.cs`، `EquipmentListState.cs`، `StocktakeState.cs`، `CatalogClient.cs`، `ReportsClient.cs`؛ در `AdminData.razor` فقط بخش دانلود پشتیبان می‌ماند
- Tests: `EquipmentTests`، `EquipmentTestBase`، `BulkAndHistoryTests`، `ReportAndDataTests`، `StocktakeAndRepairTests`، `LocationTests`، `Domain.Tests/EquipmentRulesTests`، `EquipmentInputCopyTests`

**نگه‌داشته (برای برش‌های بعد، بی‌تغییر):** `DataTools/Xlsx.cs`، `Domain/Labels/Code128.cs` و `Components/Barcode.razor`، `Components/HistoryTimeline.razor` (شاخه‌های قطعه/تعمیر حذف)، `DeleteDialog.razor`، `FoldSection.razor`.

**Interfaces:**
- Produces — `Permissions` دقیقاً کلیدهای بخش ۱۱ spec:
  `StockView="stock.view"`، `WithdrawalCreate="withdrawal.create"`، `WithdrawalReview="withdrawal.review"`، `StockReceive="stock.receive"`، `StockAdjust="stock.adjust"`، `ProductsManage="products.manage"`، `DashboardView="dashboard.view"`، `ReportsView="reports.view"`، `DataImport="data.import"`، `DataExport="data.export"`، `BackupDownload="backup.download"`، `SettingsManage="settings.manage"`، `UsersManage="users.manage"`، `RolesManage="roles.manage"`؛ برچسب‌ها فارسی، گروه‌ها: «انبار»، «بررسی و گزارش»، «داده»، «مدیریت».
- `Roles`: `Admin`، `Staff` («کارکنان»)، `Keeper` («انباردار»)، `Manager` («مدیر»).
- `TestApp`: `AddTechnicianAsync` ← `AddStaffAsync`؛ افزودن `AddKeeperAsync`، `AddManagerAsync`؛ ثابت‌های رمز به همان شکل.

- [ ] **Step 1: تست‌های مجوز (ناموفق)**

`tests/PharmacyWarehouse.Domain.Tests/PermissionsTests.cs`:
```csharp
AllKeys_AreUnique
AllKeys_MatchSpecSection11   // مجموعه دقیقاً ۱۴ کلید بالا
```
`tests/PharmacyWarehouse.Api.Tests/StaffPolicyTests.cs` (بازنویسی، Theory روی جدول بخش ۱۱):
```csharp
[Theory] DefaultRole_HasExactlySpecPermissions(string role, string permission, bool expected)
// Staff: stock.view ✓، withdrawal.create ✓، withdrawal.review ✗، stock.adjust ✗، backup.download ✗
// Keeper: همه‌ی ستون انباردار؛ settings.manage ✗، users.manage ✗
// Manager: dashboard.view ✓، reports.view ✓، settings.manage ✓، users.manage ✓، roles.manage ✓، stock.adjust ✗، withdrawal.create ✗
Admin_HasEverything
UserGrant_AddsPermission_UserDeny_RemovesIt      // Manager + grant stock.adjust ← true؛ Keeper + deny withdrawal.review ← false
```

Run: `dotnet test PharmacyWarehouse.sln --filter "PermissionsTests|StaffPolicyTests"`
Expected: FAIL

- [ ] **Step 2: حذف فایل‌ها، `Permissions`/`Roles` تازه، اصلاح ارجاع‌ها**

`NavMenu.razor`: فقط «محل‌ها»، «بکاپ»، «کاربران»، «نقش‌ها»، «حساب». `AdminRoles.razor` خط پیش‌فرض انتخاب ← `Permissions.StockView`. `Alerts/AlertEndpoints.cs`: `BackupWarnDays = 1`.

Run: `dotnet build PharmacyWarehouse.sln`
Expected: بدون خطا و بدون هشدار تازه

- [ ] **Step 3: یک migration اولیه‌ی تازه**

حذف `Data/Migrations/*`، سپس `dotnet ef migrations add Initial --project src/PharmacyWarehouse.Api`. به `Up` با `migrationBuilder.Sql` اضافه شود:
- دو trigger `AuditEntries_NoUpdate`/`AuditEntries_NoDelete` دقیقاً مثل `20261103142355_AuditAppendOnly.cs` Emu (Review Focus ۵)؛ و `DROP TRIGGER` در `Down`.
- چهار نقش با شناسه‌ی ثابت `A0000000-0000-0000-0000-00000000000{1..4}` (`ADMIN`، `STAFF`، `KEEPER`، `MANAGER`) و `AspNetRoleClaims` با `ClaimType = 'perm'` طبق جدول بخش ۱۱، به الگوی `20261103182648_AccessAndReports.cs` Emu.

- [ ] **Step 4: تست‌ها**

افزودن به `AuditTests`: `Triggers_ExistAfterFreshMigration` (پرس‌وجوی `sqlite_master` برای هر دو نام trigger).

Run: `dotnet test PharmacyWarehouse.sln`
Expected: همه PASS

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "Remove equipment domain; add pharmacy roles, permissions and fresh initial migration"
```

---

### Task 5: نام‌های استقرار، نشست، مستندات، بررسی نهایی

**Files:**
- Modify: `deploy/install.ps1`، `uninstall.ps1`، `update.ps1`، `setup-backup.ps1`، `setup-backup-target.ps1` (نام سرویس `PharmacyWarehouse`، حساب `NT SERVICE\PharmacyWarehouse`، پوشه‌ی پیش‌فرض `C:\PharmacyWarehouseData`، نام گواهی و فایل‌ها)، `publish-package.bat`، `run-dev.bat`، `src/PharmacyWarehouse.Client/wwwroot/index.html` (عنوان «انبار داروخانه»)، `Components/BrandMark.razor`
- Modify: `src/PharmacyWarehouse.Api/appsettings.json` (`"Auth": { "CookieLifetimeHours": 720 }`)
- Create: `CLAUDE.md` (از `CLAUDE.md` Emu: توضیح پروژه، کاربران (انباردار، کارکنان، مدیر)، خطوط قرمز با «فایل Excel و هر داده‌ی واقعی داروخانه وارد Git نشود»، قواعد کد با حذف قاعده‌ی شماره‌ی اموال و افزودن «موجودی فقط با `StockMovement`؛ `OnHand` در همان تراکنش؛ `StockMovement` فقط اضافه‌شونده»، پورت‌های 7500/7600، دستورها با نام‌های تازه بدون `seed-demo`)
- Create: `docs/brief.md` (بخش ۷ «وضعیت جاری»: برش ۰ تمام، برش ۱ بعدی، پیش‌نیاز برش ۱ = خروجی بارکدخوان)
- Create: `docs/deploy.md` (از Emu با نام‌های تازه؛ بازنویسی کامل در برش ۷؛ بالای فایل: «هنوز روی سرور داروخانه آزموده نشده»)

- [ ] **Step 1: تست نشست (ناموفق)** در `SessionTests`

```csharp
CookieLifetime_Is30Days   // پاسخ ورود: هدر Set-Cookie با expires حدود ۷۲۰ ساعت بعد (±۱ ساعت)
```

Run: `dotnet test tests/PharmacyWarehouse.Api.Tests --filter CookieLifetime_Is30Days`
Expected: FAIL (۱۲ ساعت)

- [ ] **Step 2: تنظیم `CookieLifetimeHours`؛ تست PASS**

- [ ] **Step 3: نام‌ها در اسکریپت‌ها و مستندات**

- [ ] **Step 4: بررسی جامانده‌ها (Review Focus ۱)**

Run: `grep -rnwi "emu" --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj . | grep -v "docs/spec.md\|docs/superpowers"`
Expected: فقط ذکر منشأ در `CLAUDE.md` و توضیح‌ها («برگرفته از Emu»)؛ هیچ شناسه، نام سرویس، مسیر یا کوکی.

Run: `grep -rniE "equipment|AssetNumber|Building|Floor\b|Repair" src tests deploy`
Expected: بدون نتیجه.

- [ ] **Step 5: کل تست‌ها و build انتشار**

Run: `dotnet test PharmacyWarehouse.sln && dotnet publish src/PharmacyWarehouse.Api -c Release -o /tmp/pw-publish`
Expected: همه PASS؛ publish بدون خطا (`-p:WarehousePackage=true` فقط روی ویندوز امیر).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "Rename deployment, 30-day session, project docs for PharmacyWarehouse"
```

---

## پایان برش: آزمون مرورگر با امیر (ویندوز)

```
git pull
dotnet tool restore
dotnet ef database update --project src\PharmacyWarehouse.Api
dotnet run --project src\PharmacyWarehouse.Api -- create-admin <username>
run-dev.bat
```

1. `https://localhost:7600` ← ورود، تعویض رمز اولیه.
2. «محل‌ها»: ساختن «ضلع جنوبی › قفسه ۲ › طبقه ۳ › قسمت B»؛ تغییر نام «قفسه ۲» و دیدن مسیر تازه‌ی «قسمت B».
3. انتقال «ضلع جنوبی» زیر «قسمت B» ← پیام خطا.
4. حذف «طبقه ۳» ← پیام وابستگی؛ حذف «قسمت B» ← موفق.
5. ساختن کاربر «کارکنان»، ورود در مرورگر دیگر، غیرفعال کردن از حساب Admin ← درخواست بعدی مرورگر دوم به صفحه‌ی ورود می‌رود.
6. «بکاپ» ← دانلود فایل پشتیبان.

یادآوری: `database update` روی سیستم دیگر (خانه/کار) هم اجرا شود.
