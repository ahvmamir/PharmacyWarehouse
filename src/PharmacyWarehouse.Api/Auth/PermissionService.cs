using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;

namespace PharmacyWarehouse.Api.Auth;

// مجوز مؤثر کاربر = مجوزهای نقش + اجازه‌ی خودش − منع خودش. نقش Admin همیشه همه را دارد،
// تا هیچ تنظیمی نتواند سیستم را بی‌مدیر کند.
public sealed class PermissionService
{
    private readonly WarehouseDbContext _db;

    public PermissionService(WarehouseDbContext db)
    {
        _db = db;
    }

    public async Task<HashSet<string>> GetEffectiveAsync(Guid userId)
    {
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.RoleId)
            .ToListAsync();

        var isAdmin = await _db.Roles.AsNoTracking()
            .AnyAsync(r => roleIds.Contains(r.Id) && r.Name == Roles.Admin);

        if (isAdmin) return Permissions.All.Select(p => p.Key).ToHashSet();

        var fromRoles = await _db.RoleClaims.AsNoTracking()
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == Permissions.RoleClaimType)
            .Select(c => c.ClaimValue!)
            .ToListAsync();

        var own = await _db.UserClaims.AsNoTracking()
            .Where(c => c.UserId == userId
                        && (c.ClaimType == Permissions.UserGrantClaimType || c.ClaimType == Permissions.UserDenyClaimType))
            .Select(c => new { c.ClaimType, c.ClaimValue })
            .ToListAsync();

        var result = fromRoles.ToHashSet();
        result.UnionWith(own.Where(c => c.ClaimType == Permissions.UserGrantClaimType).Select(c => c.ClaimValue!));
        result.ExceptWith(own.Where(c => c.ClaimType == Permissions.UserDenyClaimType).Select(c => c.ClaimValue!));
        return result;
    }

    public async Task<bool> HasAsync(Guid? userId, string permission) =>
        userId is { } id && (await GetEffectiveAsync(id)).Contains(permission);
}
