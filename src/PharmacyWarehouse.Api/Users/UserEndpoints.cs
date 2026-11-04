using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Users;

// مدیریت کاربران. AppUser در رهگیر نیست، پس ردیف تاریخچه دستی است.
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Users).RequireAuthorization(Permissions.UsersManage);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("{id:guid}", UpdateAsync);
        group.MapGet("{id:guid}/dependencies", DependenciesAsync);
        group.MapDelete("{id:guid}", DeleteAsync);
        group.MapPost("{id:guid}/reset-password", ResetPasswordAsync);
        group.MapPost("{id:guid}/deactivate",
            (Guid id, ClaimsPrincipal me, UserManager<AppUser> users, WarehouseDbContext db, AuditContext audit) =>
                SetActiveAsync(id, false, users, db, audit));
        group.MapPost("{id:guid}/activate",
            (Guid id, ClaimsPrincipal me, UserManager<AppUser> users, WarehouseDbContext db, AuditContext audit) =>
                SetActiveAsync(id, true, users, db, audit));
    }

    private static async Task<UserItem> ToItemAsync(AppUser user, UserManager<AppUser> users, WarehouseDbContext db)
    {
        var roles = await users.GetRolesAsync(user);
        var claims = await db.UserClaims.AsNoTracking()
            .Where(c => c.UserId == user.Id)
            .Select(c => new { c.ClaimType, c.ClaimValue })
            .ToListAsync();

        return new UserItem
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            DisplayName = user.DisplayName,
            Role = roles.FirstOrDefault() ?? "",
            IsActive = user.IsActive,
            MustChangePassword = user.MustChangePassword,
            Grants = claims.Where(c => c.ClaimType == Permissions.UserGrantClaimType).Select(c => c.ClaimValue!).ToArray(),
            Denies = claims.Where(c => c.ClaimType == Permissions.UserDenyClaimType).Select(c => c.ClaimValue!).ToArray()
        };
    }

    private static async Task<IResult> ListAsync(UserManager<AppUser> users, WarehouseDbContext db)
    {
        var all = await users.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();

        var items = new List<UserItem>(all.Count);
        foreach (var user in all) items.Add(await ToItemAsync(user, users, db));

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(
        CreateUserRequest request,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        WarehouseDbContext db,
        AuditContext audit)
    {
        var username = TextNormalizer.ToAsciiDigits(request.UserName?.Trim() ?? "");
        var displayName = TextNormalizer.Clean(request.DisplayName);

        if (username.Length == 0) return ApiProblems.Validation("UserName", "نام کاربری را وارد کنید.");
        if (displayName is null) return ApiProblems.Validation("DisplayName", "نام را وارد کنید.");
        if (displayName.Length > 100) return ApiProblems.Validation("DisplayName", "نام حداکثر ۱۰۰ نویسه است.");
        if (string.IsNullOrWhiteSpace(request.Role) || !await roles.RoleExistsAsync(request.Role))
        {
            return ApiProblems.Validation("Role", "نقش را انتخاب کنید.");
        }

        if (await users.FindByNameAsync(username) is not null)
        {
            return ApiProblems.Conflict("این نام کاربری وجود دارد.");
        }

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = username,
            DisplayName = displayName,
            MustChangePassword = true,
            IsActive = true
        };

        var password = CredentialsGenerator.NewPassword();
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return created.Errors.Any(e => e.Code == "InvalidUserName")
                ? ApiProblems.Validation("UserName", "نام کاربری فقط حروف انگلیسی، رقم و . - _ می‌پذیرد.")
                : Results.Problem(title: "کاربر ساخته نشد.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var role = await users.AddToRoleAsync(user, request.Role);
        if (!role.Succeeded)
        {
            await users.DeleteAsync(user);
            return Results.Problem(title: "کاربر ساخته نشد.", statusCode: StatusCodes.Status500InternalServerError);
        }

        await WriteAuditAsync(db, audit, user, AuditActions.Created, null, null, request.Role);

        return Results.Ok(new CreateUserResponse { User = await ToItemAsync(user, users, db), InitialPassword = password });
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles,
        WarehouseDbContext db,
        AuditContext audit)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return ApiProblems.NotFound();

        var username = TextNormalizer.ToAsciiDigits(request.UserName?.Trim() ?? "");
        var displayName = TextNormalizer.Clean(request.DisplayName);

        if (username.Length == 0) return ApiProblems.Validation("UserName", "نام کاربری را وارد کنید.");
        if (displayName is null) return ApiProblems.Validation("DisplayName", "نام را وارد کنید.");
        if (displayName.Length > 100) return ApiProblems.Validation("DisplayName", "نام حداکثر ۱۰۰ نویسه است.");
        if (string.IsNullOrWhiteSpace(request.Role) || !await roles.RoleExistsAsync(request.Role))
        {
            return ApiProblems.Validation("Role", "نقش را انتخاب کنید.");
        }

        var unknown = request.Grants.Concat(request.Denies).FirstOrDefault(p => !Permissions.IsKnown(p));
        if (unknown is not null) return ApiProblems.Validation("Permissions", "مجوز نامعتبر است.");
        if (request.Grants.Intersect(request.Denies).Any())
        {
            return ApiProblems.Validation("Permissions", "یک مجوز نمی‌تواند هم اجازه داشته باشد و هم منع.");
        }

        var currentRoles = await users.GetRolesAsync(user);
        var currentRole = currentRoles.FirstOrDefault() ?? "";
        var isSelf = id == audit.UserId;

        // مدیر نمی‌تواند نقش خودش را عوض کند؛ پس همیشه دست‌کم یک مدیر می‌ماند.
        if (isSelf && currentRole != request.Role)
        {
            return ApiProblems.Validation("Role", "نقش خودتان را نمی‌توانید عوض کنید.");
        }

        var existingOwner = await users.FindByNameAsync(username);
        if (existingOwner is not null && existingOwner.Id != id)
        {
            return ApiProblems.Conflict("این نام کاربری وجود دارد.");
        }

        var oldUserName = user.UserName;
        var oldDisplayName = user.DisplayName;

        if (user.UserName != username)
        {
            var renamed = await users.SetUserNameAsync(user, username);
            if (!renamed.Succeeded)
            {
                return renamed.Errors.Any(e => e.Code == "InvalidUserName")
                    ? ApiProblems.Validation("UserName", "نام کاربری فقط حروف انگلیسی، رقم و . - _ می‌پذیرد.")
                    : Results.Problem(title: "نام کاربری عوض نشد.", statusCode: StatusCodes.Status500InternalServerError);
            }
        }

        user.DisplayName = displayName;

        if (currentRole != request.Role)
        {
            if (currentRoles.Count > 0) await users.RemoveFromRolesAsync(user, currentRoles);
            await users.AddToRoleAsync(user, request.Role);
        }

        // فهرست کامل جایگزین می‌شود.
        var oldClaims = await db.UserClaims
            .Where(c => c.UserId == id
                        && (c.ClaimType == Permissions.UserGrantClaimType || c.ClaimType == Permissions.UserDenyClaimType))
            .ToListAsync();
        var oldGrants = oldClaims.Where(c => c.ClaimType == Permissions.UserGrantClaimType).Select(c => c.ClaimValue).Order();
        var oldDenies = oldClaims.Where(c => c.ClaimType == Permissions.UserDenyClaimType).Select(c => c.ClaimValue).Order();
        var permissionsChanged = !oldGrants.SequenceEqual(request.Grants.Order())
                                 || !oldDenies.SequenceEqual(request.Denies.Order());

        db.UserClaims.RemoveRange(oldClaims);
        db.UserClaims.AddRange(request.Grants.Select(p => new IdentityUserClaim<Guid>
        {
            UserId = id, ClaimType = Permissions.UserGrantClaimType, ClaimValue = p
        }));
        db.UserClaims.AddRange(request.Denies.Select(p => new IdentityUserClaim<Guid>
        {
            UserId = id, ClaimType = Permissions.UserDenyClaimType, ClaimValue = p
        }));
        await db.SaveChangesAsync();

        // برای خود کاربر نشست را نمی‌بندیم؛ تغییر نقش دیگران با مهر فوراً اثر می‌کند.
        if (!isSelf) user.SecurityStamp = Guid.NewGuid().ToString();

        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return Results.Problem(title: "تغییر انجام نشد.", statusCode: StatusCodes.Status500InternalServerError);
        }

        if (oldUserName != username) await WriteAuditAsync(db, audit, user, AuditActions.Updated, "UserName", oldUserName, username);
        if (oldDisplayName != displayName) await WriteAuditAsync(db, audit, user, AuditActions.Updated, "DisplayName", oldDisplayName, displayName);
        if (currentRole != request.Role) await WriteAuditAsync(db, audit, user, AuditActions.Updated, "Role", currentRole, request.Role);
        if (permissionsChanged)
        {
            await WriteAuditAsync(db, audit, user, AuditActions.Updated, "Permissions", null,
                $"+{string.Join(',', request.Grants)} -{string.Join(',', request.Denies)}");
        }

        return Results.Ok(await ToItemAsync(user, users, db));
    }

    // کاربری که کاری کرده خام نیست: اسمش در تاریخچه هست و باید غیرفعال شود، نه حذف.
    private static async Task<DependencyList> FindDependenciesAsync(WarehouseDbContext db, Guid id, Guid? me)
    {
        var items = new List<DependencyItem>();

        if (id == me) items.Add(new DependencyItem("محدودیت", "نمی‌توانید خودتان را حذف کنید."));

        var actions = await db.AuditEntries.AsNoTracking().CountAsync(a => a.UserId == id);
        if (actions > 0) items.Add(new DependencyItem("تاریخچه", $"{actions} تغییر ثبت‌شده؛ به‌جای حذف، غیرفعالش کنید."));

        return new DependencyList { Items = items, Total = items.Count };
    }

    private static async Task<IResult> DependenciesAsync(Guid id, UserManager<AppUser> users, WarehouseDbContext db, AuditContext audit) =>
        await users.FindByIdAsync(id.ToString()) is null
            ? ApiProblems.NotFound()
            : Results.Ok(await FindDependenciesAsync(db, id, audit.UserId));

    private static async Task<IResult> DeleteAsync(Guid id, UserManager<AppUser> users, WarehouseDbContext db, AuditContext audit)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return ApiProblems.NotFound();

        if (!(await FindDependenciesAsync(db, id, audit.UserId)).CanDelete)
        {
            return ApiProblems.Conflict("این کاربر وابستگی دارد و حذف نمی‌شود.");
        }

        var name = user.UserName;
        var result = await users.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return Results.Problem(title: "حذف انجام نشد.", statusCode: StatusCodes.Status500InternalServerError);
        }

        await WriteAuditAsync(db, audit, user, AuditActions.Deleted, null, name, null);
        return Results.Ok(new DependencyList());
    }

    private static async Task<IResult> ResetPasswordAsync(
        Guid id,
        UserManager<AppUser> users,
        WarehouseDbContext db,
        AuditContext audit)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return ApiProblems.NotFound();

        if (id == audit.UserId)
        {
            return ApiProblems.Validation("Id", "رمز خودتان را از صفحه‌ی حساب من تغییر دهید.");
        }

        var password = CredentialsGenerator.NewPassword();

        // همه در یک UpdateAsync: یا همه یا هیچ. مهر عوض می‌شود و نشست‌های باز فوراً باطل می‌شوند.
        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.MustChangePassword = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return Results.Problem(title: "رمز عوض نشد.", statusCode: StatusCodes.Status500InternalServerError);
        }

        await WriteAuditAsync(db, audit, user, AuditActions.Updated, "PasswordReset", null, null);

        return Results.Ok(new ResetPasswordResponse { Id = user.Id, NewPassword = password });
    }

    private static async Task<IResult> SetActiveAsync(
        Guid id,
        bool active,
        UserManager<AppUser> users,
        WarehouseDbContext db,
        AuditContext audit)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return ApiProblems.NotFound();

        // چون هیچ مدیری خودش را غیرفعال نمی‌کند، آخرین مدیر فعال هم همیشه می‌ماند.
        if (!active && id == audit.UserId)
        {
            return ApiProblems.Validation("Id", "نمی‌توانید خودتان را غیرفعال کنید.");
        }

        user.IsActive = active;
        user.SecurityStamp = Guid.NewGuid().ToString();

        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return Results.Problem(title: "تغییر انجام نشد.", statusCode: StatusCodes.Status500InternalServerError);
        }

        await WriteAuditAsync(db, audit, user, AuditActions.Updated, "IsActive", null, active.ToString());

        return Results.Ok(await ToItemAsync(user, users, db));
    }

    private static async Task WriteAuditAsync(
        WarehouseDbContext db, AuditContext audit, AppUser user, string action, string? field, string? oldValue, string? newValue)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            ChangeSetId = Guid.NewGuid(),
            AtUtc = DateTime.UtcNow,
            UserId = audit.UserId,
            UserName = audit.UserName,
            EntityType = "User",
            EntityId = user.Id,
            EntityLabel = user.UserName,
            Action = action,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue
        });

        await db.SaveChangesAsync();
    }
}
