using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Assets;
using PharmacyWarehouse.Api.Data;

namespace PharmacyWarehouse.Api.Operations;

// بعد از روشن شدن سرویس، کارهای پرهزینه‌ی اولین درخواست (کامپایل JIT، ساخت مدل EF، hash رمز)
// همین‌جا انجام می‌شود تا اولین ورود کاربر حدود ۲ ثانیه منتظر نماند. خطا فقط ثبت می‌شود.
public sealed class WarmUpService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WarmUpService> _logger;

    public WarmUpService(IServiceProvider services, ILogger<WarmUpService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            await EquipmentEndpoints.WarmUpAsync(db, stoppingToken);
            await db.Users.AsNoTracking().AnyAsync(stoppingToken);

            // همان الگوریتم hash ورود؛ فقط برای JIT، نتیجه دور ریخته می‌شود.
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
            hasher.HashPassword(new AppUser(), "warm-up-only");

            _logger.LogInformation("Warm-up finished.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warm-up failed; the first request will be slower.");
        }
    }
}
