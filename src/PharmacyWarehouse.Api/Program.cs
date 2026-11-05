using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using PharmacyWarehouse.Api.Alerts;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.DataTools;
using PharmacyWarehouse.Api.Stock;
using PharmacyWarehouse.Api.Storage;
using PharmacyWarehouse.Api.Operations;
using PharmacyWarehouse.Api.Users;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

// سرویس ویندوز با پوشه‌ی جاری C:\Windows\System32 شروع می‌شود؛ پس ریشه‌ی محتوا (محل appsettings
// و wwwroot) باید صریحاً پوشه‌ی برنامه باشد. بیرون از سرویس رفتار قبلی می‌ماند.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

// بیرون از سرویس ویندوز (توسعه، IIS، تست) هیچ اثری ندارد.
builder.Host.UseWindowsService(options => options.ServiceName = "PharmacyWarehouse");

// کلیدهای امضای کوکی ورود. بدون این تنظیم، محل کلیدها به پروفایل حساب اجراکننده بسته است و
// ممکن است بعد از ریستارت از دست برود (یعنی خروج همه‌ی کاربران). install.ps1 آن را در پوشه‌ی داده می‌گذارد.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    var dataProtection = builder.Services.AddDataProtection()
        .SetApplicationName("PharmacyWarehouse")
        .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

    if (OperatingSystem.IsWindows())
    {
        dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}

// مسیر دیتابیس: اگر نسبی باشد، نسبت به پوشه پروژه Api ثابت می‌شود
// تا اجرا از Visual Studio و از CMD یک فایل دیتابیس را باز کنند.
var rawConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

var sqlite = new SqliteConnectionStringBuilder(rawConnectionString);
if (!Path.IsPathRooted(sqlite.DataSource))
{
    sqlite.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
}

builder.Services.AddSingleton(new DatabaseLocation(sqlite.DataSource));

// گرم کردن بعد از روشن شدن فقط در نصب واقعی؛ در توسعه و تست لازم نیست و فقط کار اضافه است.
// فرمان‌های خط فرمان (migrate، backup) سرویس پس‌زمینه را اجرا نمی‌کنند، چون app.Run صدا زده نمی‌شود.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<WarmUpService>();
}

// رهگیر تاریخچه scoped است چون کاربر هر درخواست را از AuditContext می‌خواند.
builder.Services.AddScoped<AuditContext>();
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddScoped<PharmacyWarehouse.Api.Stock.StockLedger>();

builder.Services.AddDbContext<WarehouseDbContext>((services, options) =>
    options.UseSqlite(sqlite.ConnectionString)
        .AddInterceptors(services.GetRequiredService<AuditInterceptor>()));

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        // سیاست رمز: طول مهم‌تر از پیچیدگی
        options.Password.RequiredLength = PasswordRules.MinLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = PasswordRules.MinUniqueChars;

        // قفل موقت و خودکار بعد از تلاش ناموفق
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddPasswordValidator<AppPasswordValidator>()
    .AddEntityFrameworkStores<WarehouseDbContext>()
    .AddSignInManager();

// مدت اعتبار کوکی (ساعت). پیش‌فرض ۱۲ (برنامه‌ی واقعی ۷۲۰ از appsettings.json)؛ از تنظیم Auth:CookieLifetimeHours قابل تغییر است.
var cookieHours = builder.Configuration.GetValue<int?>("Auth:CookieLifetimeHours") ?? 12;
if (cookieHours <= 0)
{
    throw new InvalidOperationException("Auth:CookieLifetimeHours must be greater than zero.");
}

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "PharmacyWarehouse.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(cookieHours);
    options.SlidingExpiration = true;

    // Api باید ۴۰۱/۴۰۳ برگرداند، نه ریدایرکت به صفحه ورود.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// مهر امنیتی در هر درخواست با دیتابیس مقایسه می‌شود (تغییر رمز و غیرفعال‌سازی فوراً اثر می‌کند).
// هزینه: چند کوئری کوچک در هر درخواست.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);

// هر مجوز یک policy هم‌نام دارد. handler به UserManager (scoped) نیاز دارد، پس scoped ثبت می‌شود.
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

var authorization = builder.Services.AddAuthorizationBuilder();
foreach (var permission in Permissions.All)
{
    authorization.AddPolicy(permission.Key, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new PermissionRequirement(permission.Key)));
}

// CORS فقط وقتی فعال می‌شود که origin صریح در تنظیمات باشد.
const string ClientCorsPolicy = "Client";
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var useCors = corsOrigins.Length > 0;

if (corsOrigins.Any(origin => origin.Trim() == "*"))
{
    throw new InvalidOperationException("Cors:AllowedOrigins must not contain '*'.");
}

if (useCors)
{
    builder.Services.AddCors(options =>
        options.AddPolicy(ClientCorsPolicy, policy =>
            policy.WithOrigins(corsOrigins)
                  .WithHeaders("Content-Type", ApiHeaders.RequestedWith)
                  .WithExposedHeaders("Content-Disposition")
                  .AllowAnyMethod()
                  .AllowCredentials()));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.Logger.LogInformation("SQLite database file: {Path}", sqlite.DataSource);
}

// دستورهای مدیریتی (Api باید بسته باشد):
//   dotnet run --project src\PharmacyWarehouse.Api -- create-admin <username>
//   dotnet run --project src\PharmacyWarehouse.Api -- reset-password <username>
if (args.Length > 0 && args[0] == "create-admin")
{
    Environment.ExitCode = await CreateAdminCommand.RunAsync(app, args);
    return;
}

if (args.Length > 0 && args[0] == "reset-password")
{
    Environment.ExitCode = await ResetPasswordCommand.RunAsync(app, args);
    return;
}

if (args.Length > 0 && args[0] == "migrate")
{
    Environment.ExitCode = await OperationCommands.MigrateAsync(app);
    return;
}

if (args.Length > 0 && args[0] == "backup")
{
    Environment.ExitCode = await OperationCommands.BackupAsync(app, args);
    return;
}

// ترتیب مهم است: no-store ← CORS ← فایل‌های Client ← هدر CSRF ← احراز هویت ← مجوز ← endpointها
// no-store اول است تا پاسخ‌های ردشده در مراحل بعد هم آن را داشته باشند.
app.UseMiddleware<NoStoreMiddleware>();

if (useCors)
{
    app.UseCors(ClientCorsPolicy);
}

// در تولید Api خودش Client را سرو می‌کند: یک آدرس، بدون CORS. در توسعه Client روی پورت جدا اجرا می‌شود.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// صریح و بعد از فایل‌ها: وگرنه ASP.NET مسیریابی را اول می‌گذارد و fallback جای فایل‌های استاتیک را می‌گیرد.
app.UseRouting();

app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthentication();
app.UseMiddleware<AuditContextMiddleware>();
app.UseAuthorization();

var serverVersion = AppVersion.Of(typeof(Program).Assembly);
app.MapGet(ApiRoutes.Health, () => Results.Ok(HealthResponse.Ok(DateTimeOffset.UtcNow, serverVersion)));
var organization = builder.Configuration["Branding:OrganizationName"]?.Trim();
app.MapGet(ApiRoutes.AppInfo, () => Results.Ok(new AppInfoResponse
{
    Organization = string.IsNullOrEmpty(organization) ? null : organization,
    Version = serverVersion
}));
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapStorageLocationEndpoints();
app.MapProductEndpoints();
app.MapReceiptEndpoints();
app.MapWithdrawalEndpoints();
app.MapUserEndpoints();
app.MapRoleEndpoints();
app.MapBackupEndpoints();
app.MapAlertEndpoints();

// مسیر ناشناخته‌ی زیر /api باید ۴۰۴ بماند، نه صفحه‌ی Client؛ بقیه‌ی مسیرها را Router کلاینت می‌فهمد.
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
public partial class Program { }
