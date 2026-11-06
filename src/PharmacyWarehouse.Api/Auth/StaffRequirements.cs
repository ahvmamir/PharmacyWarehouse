using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using PharmacyWarehouse.Api.Data;

namespace PharmacyWarehouse.Api.Auth;

// هر مسیر دامنه یک مجوز می‌خواهد، به‌علاوه‌ی کاربر فعال با رمز عوض‌شده.
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    // یکی از مجوزها کافی است (برای جست‌وجوی قلم)؛ با یک مجوز، همان لازم است.
    public PermissionRequirement(params string[] anyOf)
    {
        AnyOf = anyOf;
    }

    public IReadOnlyList<string> AnyOf { get; }
}

// شرطی که برقرار نشود Succeed نمی‌شود و policy رد می‌شود (۴۰۳).
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly UserManager<AppUser> _users;
    private readonly PermissionService _permissions;

    public PermissionHandler(UserManager<AppUser> users, PermissionService permissions)
    {
        _users = users;
        _permissions = permissions;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;

        var user = await _users.GetUserAsync(context.User);

        // مهر امنیتی خط دفاع اول است؛ IsActive بیمه‌ی مسیری است که مهر را نچرخانده.
        if (user is null || !user.IsActive || user.MustChangePassword) return;

        var effective = await _permissions.GetEffectiveAsync(user.Id);
        if (requirement.AnyOf.Any(effective.Contains))
        {
            context.Succeed(requirement);
        }
    }
}
