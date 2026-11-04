using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost(ApiRoutes.Login, LoginAsync);
        app.MapPost(ApiRoutes.Logout, LogoutAsync);
        app.MapGet(ApiRoutes.Me, MeAsync).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        PermissionService permissions)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.Problem(
                title: "نام کاربری و رمز عبور را وارد کنید.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // کیبورد فارسی ارقام فارسی می‌نویسد؛ نام کاربری رقمی در دیتابیس لاتین است. رمز عمداً دست‌نخورده می‌ماند.
        var username = TextNormalizer.ToAsciiDigits(request.Username.Trim());

        var user = await users.FindByNameAsync(username);

        if (user is null || !user.IsActive)
        {
            return InvalidCredentials();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return Results.Problem(
                title: "تلاش‌های ناموفق زیاد بود. چند دقیقه بعد دوباره امتحان کنید.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!result.Succeeded)
        {
            return InvalidCredentials();
        }

        await signIn.SignInAsync(user, isPersistent: true);

        return Results.Ok(await BuildCurrentUserAsync(user, users, permissions));
    }

    private static async Task<IResult> LogoutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        PermissionService permissions)
    {
        var user = await users.GetUserAsync(principal);

        if (user is null || !user.IsActive)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await BuildCurrentUserAsync(user, users, permissions));
    }

    private static IResult InvalidCredentials() =>
        Results.Problem(
            title: "نام کاربری یا رمز عبور درست نیست.",
            statusCode: StatusCodes.Status401Unauthorized);

    private static async Task<CurrentUserResponse> BuildCurrentUserAsync(
        AppUser user,
        UserManager<AppUser> users,
        PermissionService permissions)
    {
        var roles = await users.GetRolesAsync(user);

        return new CurrentUserResponse
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            DisplayName = user.DisplayName,
            Roles = roles.ToArray(),
            MustChangePassword = user.MustChangePassword,
            Permissions = (await permissions.GetEffectiveAsync(user.Id)).ToArray()
        };
    }
}