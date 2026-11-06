# راهنمای استقرار PharmacyWarehouse

> **هشدار:** این راهنما هنوز روی سرور داروخانه آزموده نشده است و در برش ۷ (استقرار و پشتیبان) کامل بازنویسی می‌شود. سرور داروخانه یک کامپیوتر معمولی ویندوز ۱۱ است (نه Windows Server و نه VM)؛ بخش ۱۳ `docs\spec.md`. مواردی که در این فایل درباره‌ی Windows Server، VM، IIS یا دامنه آمده، از Emu مانده و معتبر نیست تا برش ۷.

مسیرها نمونه‌اند. این راهنما هنوز روی سرور واقعی آزموده نشده است؛ بخش ۱۶ `docs\spec.md` (سیستم‌عامل، گواهی، مسیر پشتیبان) باز است.

## الف. روش ساده (پیشنهادی): بسته‌ی نصب و سرویس ویندوز

بدون IIS و بدون نصب .NET روی سرور. برنامه خودش HTTPS را سرو می‌کند و به‌صورت سرویس ویندوز با حساب مجازی کم‌دسترسی `NT SERVICE\PharmacyWarehouse` اجرا می‌شود. سرویس با روشن شدن سرور خودکار بالا می‌آید و اگر از کار بیفتد، بعد از یک دقیقه دوباره راه‌اندازی می‌شود.

**سیستم‌عامل:** Windows Server 2016 یا جدیدتر؛ Server Core هم کافی است. اگر IIS روی سرور نصب است و پورت ۴۴۳ را گرفته، با `-Port` پورت دیگری بدهید (مثلاً ۸۴۴۳). اسکریپت هر دو را بررسی می‌کند.

**ساخت بسته (روی سیستم توسعه):**

```
publish-package.bat
```

این دستور اول همه‌ی تست‌ها را اجرا می‌کند و بعد `publish\PharmacyWarehouse-package.zip` را می‌سازد. داخل بسته پوشه‌ی `app` و سه اسکریپت `install.ps1`، `update.ps1` و `uninstall.ps1` است.

**نصب اولیه (روی سرور):**

1. zip را روی سرور کپی و Extract کنید. اگر فایل از راه شبکه یا اینترنت آمده، در PowerShell داخل همان پوشه `Get-ChildItem -Recurse | Unblock-File` را اجرا کنید.
2. PowerShell را با Run as administrator باز کنید، به پوشه‌ی بسته بروید و این را اجرا کنید:

```
powershell -ExecutionPolicy Bypass -File .\install.ps1 -HostName pharmacy-pc,pharmacy-pc.local -DataDir D:\PharmacyWarehouseData -AdminUser <username> -BackupDir \\backup-server\PharmacyWarehouseBackups
```

| پارامتر | پیش‌فرض | توضیح |
|---|---|---|
| `-HostName` | نام کامپیوتر | نام‌هایی که کاربران در مرورگر تایپ می‌کنند. در گواهی و `AllowedHosts` قرار می‌گیرند. |
| `-IpAddress` | خالی | IP ثابت سرور، اگر کاربران با IP وارد می‌شوند (شبکه‌ی بدون دامنه و DNS). IP در گواهی و `AllowedHosts` قرار می‌گیرد. IP‌ای که این‌جا نیامده، خطای 400 می‌گیرد. |
| `-DataDir` | `C:\PharmacyWarehouseData` | دیتابیس، گواهی و کلیدها. بهتر است روی دیسک داده باشد، نه درایو سیستم. |
| `-InstallDir` | `C:\PharmacyWarehouse\app` | پوشه‌ی برنامه. با هر به‌روزرسانی جایگزین می‌شود. |
| `-Port` | `443` | اگر IIS یا برنامه‌ی دیگری روی ۴۴۳ است، پورت دیگری بدهید. |
| `-PfxPath` | خالی | گواهی CA داخلی. اگر خالی باشد، گواهی self-signed پنج‌ساله ساخته می‌شود. |
| `-AllowFrom` | `LocalSubnet` | چه آدرس‌هایی به پورت برنامه دسترسی داشته باشند، مثلاً `10.1.2.0/24` برای شبکه‌ی واحد IT. `LocalSubnet` فقط زیرشبکه‌ی خود سرور است. |
| `-AdminUser` | خالی | نام کاربری اولین مدیر. رمز را تعاملی می‌پرسد (حداقل ۸ نویسه). |
| `-BackupDir` | خالی | یک کار شبانه در Task Scheduler (ساعت ۰۲:۳۰) می‌سازد. با دامنه: حساب SYSTEM، و پوشه‌ی شبکه باید به حساب کامپیوتر سرور (`DOMAIN\سرور$`) اجازه‌ی نوشتن بدهد. بدون دامنه: به بند بعد نگاه کنید. |
| `-OrganizationName` | خالی | نام داروخانه در سربرگ، صفحه‌ی ورود، گزارش چاپی و برچسب. بعداً هم در `appsettings.Production.json` زیر `Branding:OrganizationName` قابل تغییر است (با Notepad، ذخیره با UTF-8، سپس `Restart-Service PharmacyWarehouse`). |
| `-BackupCredential` | پرسیده می‌شود | فقط برای پوشه‌ی شبکه‌ای در شبکه‌ی بدون دامنه. حساب ویندوزی که روی پوشه‌ی مقصد اجازه‌ی نوشتن دارد و روی سرور عضو Administrators است. اگر ندهید، اسکریپت می‌پرسد. اگر رمز این حساب عوض شود، پشتیبان شبانه از کار می‌افتد. |

کارهایی که اسکریپت انجام می‌دهد: کپی برنامه، ساختن یا وارد کردن گواهی، ساختن `appsettings.Production.json`، ثبت سرویس، بستن دسترسی پوشه‌ی داده به Administrators و SYSTEM و خود سرویس، ساختن دیتابیس، قانون فایروال (روی همه‌ی پروفایل‌ها، محدود به `-AllowFrom`)، کار پشتیبان، روشن کردن سرویس و بررسی `/api/health`.

**شبکه‌ی بدون دامنه (workgroup):**
- نام کامپیوتر معمولاً فقط از همان زیرشبکه پیدا می‌شود. اگر DNS ندارید، با `-IpAddress` نصب کنید تا کاربران با `https://<IP>/` وارد شوند. اگر بخواهید نام هم کار کند، آن نام را به `-HostName` بدهید و روی هر کامپیوتر کاربر در فایل `C:\Windows\System32\drivers\etc\hosts` ثبت کنید. ضدویروس ممکن است تغییر این فایل را ببندد.
- ویندوز سرور بیرون از دامنه معمولاً شبکه را Public می‌شناسد. برای همین قانون فایروال روی همه‌ی پروفایل‌ها ساخته می‌شود و فقط `-AllowFrom` دسترسی را محدود می‌کند. پس `-AllowFrom` را دقیق بدهید و `Any` نگذارید.
- Group Policy در کار نیست؛ `pharmacywarehouse.cer` را روی هر کامپیوتر کاربر دستی نصب کنید: `certutil -addstore -f Root pharmacywarehouse.cer` در CMD با دسترسی Administrator.

نمونه:

```
powershell -ExecutionPolicy Bypass -File .\install.ps1 -IpAddress <IP> -DataDir D:\PharmacyWarehouseData -AdminUser <username> -AllowFrom <it-subnet>
```

**گواهی self-signed:** فایل `D:\PharmacyWarehouseData\cert\pharmacywarehouse.cer` را روی کامپیوترها و گوشی‌های کاربران در Trusted Root Certification Authorities نصب کنید (بهتر با Group Policy)، وگرنه مرورگر هشدار می‌دهد. اگر CA داخلی دارید، از `-PfxPath` استفاده کنید.

**CA محلی (پیشنهادی برای گوشی‌ها):** اندروید گواهی self-signed بالا را نمی‌پذیرد («private key required») و عمر پنج‌ساله‌اش برای آیفون زیاد است. به‌جای آن یک CA محلی بسازید؛ هر دستگاه فقط یک بار به آن اعتماد می‌کند و تمدید گواهی سرور کاری روی دستگاه‌ها ندارد.

1. IP سرور را روی مودم رزرو کنید (رزرو DHCP). اگر IP عوض شود، گواهی با آدرس نمی‌خواند.
2. ساخت CA و گواهی سرور (مسیر خروجی بهتر است فلش باشد):
   ```
   powershell -ExecutionPolicy Bypass -File .\setup-local-ca.ps1 -OutDir E:\PharmacyCA -HostName <نام-کامپیوتر> -IpAddress <IP>
   ```
3. اعمال روی سرور: نصب تازه با `install.ps1 -PfxPath E:\PharmacyCA\pharmacywarehouse-server.pfx -IpAddress <IP>`، یا روی نصب موجود در PowerShell با Run as administrator:
   ```
   powershell -ExecutionPolicy Bypass -File .\apply-certificate.ps1 -PfxPath E:\PharmacyCA\pharmacywarehouse-server.pfx -AddHost <IP>,<نام-کامپیوتر>
   ```
4. فقط `pharmacywarehouse-root.cer` (یا `.crt`) را روی دستگاه‌ها نصب کنید. ویندوز: `certutil -addstore -f Root pharmacywarehouse-root.cer` در CMD با دسترسی Administrator. اندروید: تنظیمات، امنیت، نصب گواهی، «گواهی CA». آیفون: نصب پروفایل، سپس روشن کردن اعتماد در Settings › General › About › Certificate Trust Settings.
5. `pharmacywarehouse-server.pfx` را بعد از اعمال پاک کنید. `pharmacywarehouse-root.pfx` کلید CA است: بیرون از سرور و در جای امن نگه دارید و هرگز روی گوشی نصب نکنید. هر کس آن را داشته باشد، می‌تواند برای دستگاه‌هایی که به این CA اعتماد دارند گواهی جعلی بسازد.
6. تمدید پیش از پایان ۸۰۰ روز: همان `setup-local-ca.ps1` با `-RootPfx E:\PharmacyCA\pharmacywarehouse-root.pfx` و بعد `apply-certificate.ps1`. دستگاه‌ها کاری لازم ندارند.

**فایل‌های آماده (.bat):** این فایل‌ها کنار اسکریپت‌های `.ps1` در ریشه‌ی بسته‌ی نصب قرار دارند (در پوشه‌ی `deploy` هم هستند). هر کدام خودش درخواست دسترسی Administrator می‌کند و با دوبار کلیک اجرا می‌شود؛ پنجره تا زدن کلید باز می‌ماند تا نتیجه را ببینید.

- `1-install.bat`: نصب اولیه. IP سرور را پیشنهاد می‌دهد، در صورت نبودن CA و گواهی را می‌سازد، سرویس را نصب می‌کند و فایل گواهی سرور را پاک می‌کند.
- `update.bat`: به‌روزرسانی. باید از پوشه‌ی بسته‌ی **جدید** اجرا شود.
- `create-admin.bat`: ساخت کاربر مدیر جدید.
- `reset-password.bat`: تعیین رمز جدید برای یک کاربر.
- `backup-now.bat`: پشتیبان‌گیری فوری در پوشه‌ی انتخابی (پیش‌فرض `Documents\Darubar-Backups`). این پوشه باید بیرون از این کامپیوتر کپی شود.
- `renew-certificate.bat`: تمدید گواهی سرور با همان CA، بدون تغییر روی دستگاه‌ها.
- `health-check.bat`: وضعیت سرویس، شنونده‌ی پورت ۴۴۳، پاسخ `/api/health` و خطاهای اخیر. چیزی را تغییر نمی‌دهد.
- `verify-stock.bat`: اجرای بررسی موجودی روی برنامه‌ی نصب‌شده.
- `uninstall.bat`: حذف سرویس و برنامه. با تایپ `YES` تأیید می‌شود و پوشه‌ی داده حذف نمی‌شود.

**به‌روزرسانی:** بسته‌ی جدید را Extract کنید و از داخل همان پوشه:

```
powershell -ExecutionPolicy Bypass -File .\update.ps1
```

این اسکریپت پیش از هر کاری در `DataDir\pre-update` پشتیبان می‌گیرد، برنامه‌ی قبلی را در `C:\PharmacyWarehouse\app.previous` نگه می‌دارد، `appsettings.Production.json` را دست نمی‌زند و migration را اجرا می‌کند. اگر وسط کار خطا بدهد، راه برگشت را چاپ می‌کند.

**حذف:** `uninstall.ps1` سرویس، قانون فایروال، کار پشتیبان و پوشه‌ی برنامه را برمی‌دارد. پوشه‌ی داده هرگز حذف نمی‌شود.

**نسخه و اعلان‌ها:** نسخه‌ی نصب‌شده پایین منوی برنامه و در `https://localhost/api/health` دیده می‌شود. صفحه‌ی «اعلان‌ها» برای کاربر دارای مجوز دانلود بکاپ، پشتیبان قدیمی‌تر از ۲ روز و کمبود فضای دیسک را هم نشان می‌دهد؛ زمان آخرین پشتیبان در فایل `pharmacywarehouse.db.last-backup` کنار دیتابیس ثبت می‌شود.

**مدیریت سرویس:**
- دیدن و ری‌استارت با رابط گرافیکی: `services.msc`، سرویس «PharmacyWarehouse - Equipment Management»، راست‌کلیک و Restart.
- با PowerShell (Run as administrator): `Get-Service PharmacyWarehouse`، `Restart-Service PharmacyWarehouse`، `Stop-Service PharmacyWarehouse`، `Start-Service PharmacyWarehouse`.
- `Running` به‌تنهایی یعنی سالم نیست: اگر برنامه هنگام شروع خطا بدهد، ویندوز آن را دوباره روشن می‌کند و وضعیت `Running` می‌ماند. معیار سلامت این آدرس روی سرور است: `https://localhost/api/health` باید `"status":"ok"` بدهد.
- خطاها: Event Viewer، Windows Logs، Application، با منبع `.NET Runtime` یا `Application Error`؛ یا این دستور:
  `Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-2)} | Where-Object { $_.ProviderName -match 'PharmacyWarehouse|\.NET Runtime|Application Error' } | Select-Object -First 5 TimeCreated, ProviderName, Message | Format-List`

**ریسک‌ها:**
- رمز فایل PFX به‌صورت متن ساده در `appsettings.Production.json` است. فقط Administrators، SYSTEM و خود سرویس می‌توانند آن را بخوانند.
- اسکریپت‌ها هنوز روی ویندوز واقعی اجرا نشده‌اند. اول روی یک سرور آزمایشی امتحان کنید.

بخش‌های بعدی روش دستی با IIS است. اگر از روش ساده استفاده می‌کنید، از بین آن‌ها فقط پشتیبان‌گیری، بازگردانی و فراموشی رمز به کارتان می‌آید. در این روش جای «Application Pool» بخوانید «سرویس `PharmacyWarehouse`».

## ب. روش دستی با IIS

## ۱. پیش‌نیاز روی سرور

- ASP.NET Core Hosting Bundle نسخه‌ی ۱۰ (فقط برای IIS). بسته‌ی روش «الف» self-contained است و به نصب .NET نیاز ندارد.
- گواهی HTTPS. کوکی ورود `Secure` است و **بدون HTTPS ورود کار نمی‌کند**. CA داخلی بهتر از self-signed است، چون self-signed روی هر کامپیوتر هشدار می‌دهد.

## ۲. ساخت خروجی (روی سیستم توسعه)

```
dotnet publish src\PharmacyWarehouse.Api -c Release -o publish\PharmacyWarehouse
```

خروجی هم Api است و هم Client؛ هر دو از یک آدرس سرو می‌شوند و CORS لازم نیست.

## ۳. تنظیمات

در پوشه‌ی نصب، `appsettings.Production.json` بسازید. این فایل را وارد Git نکنید.

```json
{
  "ConnectionStrings": { "Default": "Data Source=D:\\PharmacyWarehouseData\\pharmacywarehouse.db" },
  "AllowedHosts": "pharmacy-pc.local"
}
```

پوشه‌ی داده باید بیرون از پوشه‌ی برنامه باشد تا نصب نسخه‌ی جدید به آن دست نزند. حساب اجراکننده‌ی برنامه باید روی آن اجازه‌ی نوشتن داشته باشد.

## ۴. نصب اولیه

در پوشه‌ی نصب، از CMD با دسترسی Administrator. برنامه بدون تنظیم محیط، خودش در حالت Production اجرا می‌شود:

```
PharmacyWarehouse.Api.exe migrate
PharmacyWarehouse.Api.exe create-admin <username>
```

`create-admin` رمز را مخفی و تعاملی می‌پرسد. حداقل ۸ نویسه.

## ۵. اجرا

- **IIS:** یک Site با binding روی HTTPS بسازید و Physical Path را پوشه‌ی نصب بگذارید. Application Pool روی No Managed Code، با `Load User Profile = True` تا کلیدهای ورود بعد از ریستارت بمانند. حساب `IIS AppPool\PharmacyWarehouse` روی پوشه‌ی داده اجازه‌ی Modify بگیرد. نقش WebDAV Publishing را نصب نکنید، چون PUT و DELETE برنامه را می‌بندد.
- **Windows Service:** روش ساده‌ی بخش «الف» همین است.

## ۶. به‌روزرسانی نسخه

1. روی سیستم توسعه: `git pull`، `dotnet test PharmacyWarehouse.sln`، و `dotnet publish` در پوشه‌ی خالی.
2. پشتیبان بگیرید (بخش ۷) و پوشه‌ی برنامه را هم برای برگشت کپی کنید.
3. Application Pool را متوقف کنید.
4. فایل‌ها را جایگزین کنید: `robocopy C:\PharmacyWarehouse\new C:\PharmacyWarehouse\app /MIR /XF appsettings.Production.json`. کد خروج تا ۷ موفق است.
5. `PharmacyWarehouse.Api.exe migrate`
6. Application Pool را شروع کنید و در مرورگر با Ctrl+F5 بررسی کنید.

برگشت: migration معکوس نمی‌شود، پس هم پوشه‌ی قبلی برنامه و هم فایل پشتیبان باید برگردند. تغییرات بعد از پشتیبان از بین می‌رود.

## ۷. پشتیبان‌گیری

### راه پیشنهادی: دو اسکریپت (شبکه‌ی بدون دامنه)

هر دو در بسته‌ی نصب هستند. در PowerShell با Run as administrator، داخل پوشه‌ی بسته اجرا کنید.

1. **روی کامپیوتر مقصد** (هر سیستمی غیر از سرور که شب‌ها روشن است):
   ```
   powershell -ExecutionPolicy Bypass -File .\setup-backup-target.ps1 -Folder D:\PharmacyWarehouseBackups
   ```
   کاربر `pwbackup`، پوشه و اشتراک `PharmacyWarehouseBackups` را می‌سازد و دستور مرحله‌ی بعد را چاپ می‌کند. رمز را یادداشت کنید.
2. **روی سرور**، با **همان رمز**:
   ```
   powershell -ExecutionPolicy Bypass -File .\setup-backup.ps1 -BackupDir \\<کامپیوتر-مقصد>\PharmacyWarehouseBackups
   ```
   همان کاربر را روی سرور می‌سازد (عضو Administrators، چون پوشه‌ی دیتابیس فقط برای مدیرها باز است)، کار «PharmacyWarehouse nightly backup» را ساعت ۰۲:۳۰ هر شب ثبت می‌کند، یک بار همان لحظه اجرا و نتیجه را بررسی می‌کند. اگر سرور ساعت اجرا خاموش بود، اولین فرصت بعد از روشن شدن اجرا می‌شود.

اگر شکست خورد، آخرین خطوط `DataDir\backup-last.log` را نشان می‌دهد. رایج‌ترین علت‌ها: رمز یا نام کاربر روی دو کامپیوتر یکی نیست؛ نام اشتراک غلط است؛ فایروال کامپیوتر مقصد File and Printer Sharing را بسته است. بعد از رفع، همان اسکریپت را دوباره اجرا کنید؛ کار قبلی جایگزین می‌شود.

**ریسک:** اگر رمز `pwbackup` را روی یکی از دو کامپیوتر عوض کنید، پشتیبان شبانه بی‌صدا شکست می‌خورد؛ اعلان «پشتیبان قدیمی است» در برنامه بعد از ۲ روز هشدار می‌دهد. برای عوض کردن رمز، هر دو اسکریپت را دوباره اجرا کنید.

### روش دستی

```
PharmacyWarehouse.Api.exe backup \\backup-server\PharmacyWarehouseBackups
```

فایلی به نام `pharmacywarehouse-YYYYMMDD-HHmmss.db` می‌سازد (زمان UTC) و فقط ۳۰ نسخه‌ی آخر را نگه می‌دارد. از `VACUUM INTO` استفاده می‌کند، پس برنامه می‌تواند روشن باشد.

در Task Scheduler ویندوز یک کار شبانه با همین دستور بسازید. مقصد باید **بیرون از سرور** باشد.

## ۸. بازگردانی

1. سرویس را متوقف کنید.
2. فایل `pharmacywarehouse.db` فعلی را برای احتیاط جای دیگری کپی کنید، همراه `pharmacywarehouse.db-wal` و `pharmacywarehouse.db-shm` اگر هستند؛ سپس این دو فایل را از پوشه‌ی داده بردارید.
3. فایل پشتیبان را با نام `pharmacywarehouse.db` جایگزین کنید.
4. سرویس را شروع کنید و وارد شوید.

**پیش از استفاده‌ی واقعی، یک بار این مسیر را روی سرور آزمایشی امتحان کنید.** پشتیبانی که بازگردانی‌اش آزموده نشده، پشتیبان مطمئنی نیست.

## ۹. فراموشی رمز مدیر

```
PharmacyWarehouse.Api.exe reset-password <username>
```

سرویس بهتر است متوقف باشد. رمز تکنسین‌ها را مدیر از صفحه‌ی کاربران ریست می‌کند.
