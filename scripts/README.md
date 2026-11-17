# اسکریپت‌های توسعه (`scripts\`)

برای سیستم توسعه (خانه و کار)؛ اسکریپت‌های سرور داروخانه در `deploy\` هستند. همه از ریشه‌ی مخزن اجرا می‌شوند، از هر جایی که دوبار کلیک شوند.

| فایل | کار |
|---|---|
| `start-work.bat` | شروع کار روی این سیستم: `git pull`، پشتیبان دیتابیس توسعه، `database update`، build |
| `finish-work.bat` | پایان کار: نمایش تغییرات، پرسیدن تأیید و پیام، commit و push |
| `db-backup.bat` | کپی دیتابیس توسعه در `backups\` (در Git نمی‌رود) |
| `db-update.bat` | پشتیبان و بعد اعمال migrationهای تازه |
| `run-dev.bat` | اجرای Api و Client در دو پنجره |
| `run-tests.bat` | همه‌ی تست‌ها؛ با یک نام کلاس فقط همان، مثلاً `run-tests StocktakePeriodTests` |
| `create-admin.bat` | ساخت کاربر مدیر در دیتابیس توسعه (Api بسته) |
| `reset-password.bat` | رمز تازه برای یک کاربر موجود در دیتابیس توسعه (Api بسته) |
| `seed-test-data.bat` | ۱۰۰ قلم آزمایشی ساختگی |
| `verify-stock.bat` | بررسی برابری موجودی بچ با جمع حرکت‌ها (فقط گزارش) |
| `publish-package.bat` | همه‌ی تست‌ها و بعد ساخت بسته‌ی نصب در `publish\PharmacyWarehouse-package.zip` (راهنما در `docs\deploy.md`) |

پیش از `db-update` و `start-work` پنجره‌ی Api را ببندید؛ اسکریپت پشتیبان اگر Api باز باشد متوقف می‌شود.
