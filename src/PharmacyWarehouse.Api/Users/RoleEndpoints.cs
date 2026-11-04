using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Users;

// تعریف نقش و مجوزهایش. Admin ثابت است تا هیچ‌کس نتواند سیستم را بی‌مدیر کند.
public static class RoleEndpoints
{
    private const int MaxRoleName = 50;

    public static void MapRoleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Roles).RequireAuthorization(Permissions.RolesManage);

        // صفحه‌ی کاربران هم نام نقش‌ها را لازم دارد.
        app.MapGet(ApiRoutes.Roles, ListAsync).RequireAuthorization(Permissions.UsersManage);
        group.MapPost("", CreateAsync);
        group.MapPut("{id:guid}", UpdateAsync);
        group.MapGet("{id:guid}/dependencies", DependenciesAsync);
        group.MapDelete("{id:guid}", DeleteAsync);
    }

    private static async Task<IResult> ListAsync(WarehouseDbContext db)
    {
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync();
        var claims = await db.RoleClaims.AsNoTracking()
            .Where(c => c.ClaimType == Permissions.RoleClaimType)
            .Select(c => new { c.RoleId, c.ClaimValue })
            .ToListAsync();
        var members = await db.UserRoles.AsNoTracking()
            .GroupBy(r => r.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToListAsync();

        return Results.Ok(roles.Select(role =>
        {
            var locked = role.Name == Roles.Admin;
            return new RoleItem
            {
                Id = role.Id,
                Name = role.Name ?? "",
                IsLocked = locked,
                Permissions = locked
                    ? Permissions.All.Select(p => p.Key).ToArray()
                    : claims.Where(c => c.RoleId == role.Id).Select(c => c.ClaimValue!).ToArray(),
                UserCount = members.FirstOrDefault(m => m.RoleId == role.Id)?.Count ?? 0
            };
        }));
    }

    private static IResult? Check(string? name, string[] permissions)
    {
        if (name is null) return ApiProblems.Validation("Name", "نام نقش را وارد کنید.");
        if (name.Length > MaxRoleName) return ApiProblems.Validation("Name", $"نام نقش حداکثر {MaxRoleName} نویسه است.");
        return permissions.Any(p => !Permissions.IsKnown(p)) ? ApiProblems.Validation("Permissions", "مجوز نامعتبر است.") : null;
    }

    private static void ReplaceClaims(WarehouseDbContext db, Guid roleId, IEnumerable<IdentityRoleClaim<Guid>> old, string[] permissions)
    {
        db.RoleClaims.RemoveRange(old);
        db.RoleClaims.AddRange(permissions.Distinct().Select(p => new IdentityRoleClaim<Guid>
        {
            RoleId = roleId, ClaimType = Permissions.RoleClaimType, ClaimValue = p
        }));
    }

    private static async Task<IResult> CreateAsync(SaveRoleRequest request, RoleManager<IdentityRole<Guid>> roles, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (Check(name, request.Permissions) is { } invalid) return invalid;

        if (await roles.RoleExistsAsync(name!)) return ApiProblems.Conflict("نقشی با این نام وجود دارد.");

        var role = new IdentityRole<Guid>(name!) { Id = Guid.NewGuid() };
        var created = await roles.CreateAsync(role);
        if (!created.Succeeded) return Results.Problem(title: "نقش ساخته نشد.", statusCode: StatusCodes.Status500InternalServerError);

        ReplaceClaims(db, role.Id, [], request.Permissions);
        await db.SaveChangesAsync();

        return Results.Ok(new RoleItem { Id = role.Id, Name = name!, Permissions = request.Permissions.Distinct().ToArray() });
    }

    private static async Task<IResult> UpdateAsync(Guid id, SaveRoleRequest request, RoleManager<IdentityRole<Guid>> roles, WarehouseDbContext db)
    {
        var role = await roles.FindByIdAsync(id.ToString());
        if (role is null) return ApiProblems.NotFound();
        if (role.Name == Roles.Admin) return ApiProblems.Validation("Name", "نقش مدیر ثابت است و تغییر نمی‌کند.");

        var name = TextNormalizer.Clean(request.Name);
        if (Check(name, request.Permissions) is { } invalid) return invalid;

        var other = await roles.FindByNameAsync(name!);
        if (other is not null && other.Id != id) return ApiProblems.Conflict("نقشی با این نام وجود دارد.");

        if (role.Name != name)
        {
            // نام نقش Technician در کد فقط پیش‌فرض است؛ تغییرش چیزی را نمی‌شکند.
            var renamed = await roles.SetRoleNameAsync(role, name);
            if (!renamed.Succeeded) return Results.Problem(title: "نام نقش عوض نشد.", statusCode: StatusCodes.Status500InternalServerError);
            await roles.UpdateAsync(role);
        }

        var old = await db.RoleClaims.Where(c => c.RoleId == id && c.ClaimType == Permissions.RoleClaimType).ToListAsync();
        ReplaceClaims(db, id, old, request.Permissions);
        await db.SaveChangesAsync();

        return Results.Ok(new RoleItem { Id = id, Name = name!, Permissions = request.Permissions.Distinct().ToArray() });
    }

    private static async Task<DependencyList> FindDependenciesAsync(WarehouseDbContext db, Guid id, string? roleName)
    {
        var items = new List<DependencyItem>();

        if (roleName == Roles.Admin) items.Add(new DependencyItem("محدودیت", "نقش مدیر حذف نمی‌شود."));

        var names = await db.UserRoles.AsNoTracking()
            .Where(r => r.RoleId == id)
            .Join(db.Users.AsNoTracking(), r => r.UserId, u => u.Id, (r, u) => u.UserName)
            .OrderBy(n => n)
            .ToListAsync();

        items.AddRange(names.Select(n => new DependencyItem("کاربر", n ?? "")));

        return new DependencyList { Items = items, Total = items.Count };
    }

    private static async Task<IResult> DependenciesAsync(Guid id, RoleManager<IdentityRole<Guid>> roles, WarehouseDbContext db)
    {
        var role = await roles.FindByIdAsync(id.ToString());
        return role is null ? ApiProblems.NotFound() : Results.Ok(await FindDependenciesAsync(db, id, role.Name));
    }

    private static async Task<IResult> DeleteAsync(Guid id, RoleManager<IdentityRole<Guid>> roles, WarehouseDbContext db)
    {
        var role = await roles.FindByIdAsync(id.ToString());
        if (role is null) return ApiProblems.NotFound();

        if (!(await FindDependenciesAsync(db, id, role.Name)).CanDelete)
        {
            return ApiProblems.Conflict("این نقش وابستگی دارد و حذف نمی‌شود.");
        }

        var deleted = await roles.DeleteAsync(role);
        return deleted.Succeeded
            ? Results.Ok(new DependencyList())
            : Results.Problem(title: "حذف انجام نشد.", statusCode: StatusCodes.Status500InternalServerError);
    }
}
