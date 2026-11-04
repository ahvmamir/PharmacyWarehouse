using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Auth;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost(ApiRoutes.ChangePassword, ChangePasswordAsync).RequireAuthorization();
        app.MapPost(ApiRoutes.SetInitialPassword, SetInitialPasswordAsync).RequireAuthorization();
    }

    // تعیین رمز بدون رمز فعلی؛ فقط وقتی MustChangePassword = true (بین ورود اول و اولین تغییر رمز).
    // 204 انجام شد | 400 ورودی خالی | 401 نشست نامعتبر | 403 پرچم اجبار فعال نیست
    // 422 رمز جدید پذیرفته نشد (سیاست رمز یا برابر رمز اولیه) | 500 ذخیره نشد
    private static async Task<IResult> SetInitialPasswordAsync(
        SetInitialPasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PharmacyWarehouse.Account");

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            return Results.Problem(
                title: "رمز جدید را وارد کنید.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await users.GetUserAsync(principal);
        if (user is null || !user.IsActive)
        {
            return Results.Unauthorized();
        }

        if (!user.MustChangePassword)
        {
            return Results.Problem(
                title: "این عمل فقط برای رمز اولیه مجاز است.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        // بدون رمز فعلی، «تفاوت با رمز اولیه» را با مقایسه‌ی hash ذخیره‌شده می‌سنجیم.
        if (user.PasswordHash is not null
            && users.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash, request.NewPassword)
               != PasswordVerificationResult.Failed)
        {
            return Results.Problem(
                title: "رمز جدید باید با رمز فعلی فرق داشته باشد.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // رمز را مستقیم hash می‌کنیم، پس validatorها را خودمان اجرا می‌کنیم.
        var errors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(users, user, request.NewPassword);
            if (!validation.Succeeded) errors.AddRange(validation.Errors);
        }

        if (errors.Count > 0)
        {
            logger.LogWarning("Setting initial password rejected: {Errors}",
                string.Join(", ", errors.Select(e => e.Code)));
            return Results.Problem(
                title: $"رمز جدید پذیرفته نشد. حداقل {PasswordRules.MinLength} نویسه و دست‌کم {PasswordRules.MinUniqueChars} نویسه متفاوت لازم است.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // رمز، مهر و پرچم در یک UpdateAsync: یا همه یا هیچ.
        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.MustChangePassword = false;

        var update = await users.UpdateAsync(user);
        if (!update.Succeeded)
        {
            logger.LogError("Setting initial password failed: {Errors}",
                string.Join(", ", update.Errors.Select(e => e.Code)));
            return Results.Problem(
                title: "تغییر رمز انجام نشد.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // مهر عوض شد؛ این خط کوکی همین دستگاه را زنده نگه می‌دارد و دستگاه‌های دیگر فوراً باطل می‌شوند.
        await signIn.RefreshSignInAsync(user);

        logger.LogInformation("User {UserId} set the initial password.", user.Id);
        return Results.NoContent();
    }

    // کدهای وضعیت (کلاینت بر اساس آن‌ها پیام می‌دهد):
    // 204 انجام شد | 400 ورودی خالی یا رمز فعلی غلط | 422 رمز جدید پذیرفته نشد
    // 401 نشست نامعتبر | 429 قفل موقت
    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("PharmacyWarehouse.Account");

        if (string.IsNullOrEmpty(request.CurrentPassword) || string.IsNullOrEmpty(request.NewPassword))
        {
            return Results.Problem(
                title: "رمز فعلی و رمز جدید را وارد کنید.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await users.GetUserAsync(principal);
        if (user is null || !user.IsActive)
        {
            return Results.Unauthorized();
        }

        // ChangePasswordAsync خودش شمارنده تلاش ناموفق را زیاد نمی‌کند؛ کسی که کوکی دزدیده
        // می‌توانست رمز فعلی را بی‌محدودیت حدس بزند. پس اول با قفل‌شدن بررسی می‌کنیم.
        var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            return Results.Problem(
                title: "تلاش‌های ناموفق زیاد بود. چند دقیقه بعد دوباره امتحان کنید.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!check.Succeeded)
        {
            return Results.Problem(
                title: "رمز فعلی درست نیست.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return Results.Problem(
                title: "رمز جدید باید با رمز فعلی فرق داشته باشد.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // پرچم اجبار را روی همان شیء پاک می‌کنیم تا در همان ذخیره‌ی ChangePasswordAsync بنشیند
        // (یک UpdateAsync جدا غیراتمی بود: رمز عوض می‌شد و پرچم می‌ماند). اگر تغییر رد شد، برمی‌گردانیم.
        var hadMustChange = user.MustChangePassword;
        user.MustChangePassword = false;

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            user.MustChangePassword = hadMustChange;

            var codes = result.Errors.Select(e => e.Code).ToArray();
            logger.LogWarning("Changing own password failed: {Errors}", string.Join(", ", codes));

            // خطاهای سیاست رمز با Password شروع می‌شوند؛ بقیه (مثل تداخل هم‌زمان) خطای سرورند.
            if (codes.All(c => c.StartsWith("Password", StringComparison.Ordinal)))
            {
                return Results.Problem(
                    title: $"رمز جدید پذیرفته نشد. حداقل {PasswordRules.MinLength} نویسه و دست‌کم {PasswordRules.MinUniqueChars} نویسه متفاوت لازم است.",
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            return Results.Problem(
                title: "تغییر رمز انجام نشد.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // ChangePasswordAsync مهر امنیتی را عوض می‌کند؛ بدون این خط، کوکی همین دستگاه هم
        // حداکثر یک دقیقه بعد باطل می‌شد. RefreshSignInAsync کوکی را با مهر جدید دوباره می‌نویسد.
        await signIn.RefreshSignInAsync(user);

        logger.LogInformation("User {UserId} changed own password.", user.Id);
        return Results.NoContent();
    }
}