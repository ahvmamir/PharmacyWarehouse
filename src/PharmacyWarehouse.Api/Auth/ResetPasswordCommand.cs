using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Auth;

// بازیابی رمز مدیر (رمز مدیر از رابط وب بازیابی نمی‌شود). مثل create-admin: تعاملی و Api بسته.
public static class ResetPasswordCommand
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: reset-password <username>");
            return 1;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("This command needs an interactive console (the password is typed hidden).");
            return 1;
        }

        var username = TextNormalizer.ToAsciiDigits(args[1].Trim());

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        try
        {
            var user = await users.FindByNameAsync(username);
            if (user is null || !await users.IsInRoleAsync(user, Roles.Admin))
            {
                Console.Error.WriteLine($"Admin '{username}' not found. Nothing was changed.");
                return 1;
            }

            var password = CliConsole.ReadPassword("New password: ");
            var confirmation = CliConsole.ReadPassword("Repeat new password: ");

            if (password != confirmation)
            {
                Console.Error.WriteLine("Passwords do not match. Nothing was changed.");
                return 1;
            }

            // همه‌ی validatorها (پیش‌فرض و ما) را خودمان اجرا می‌کنیم، چون رمز را مستقیم hash می‌کنیم.
            // RemovePassword + AddPassword اتمی نیست: اگر دومی رد می‌شد، مدیر بی‌رمز می‌ماند.
            var errors = new List<IdentityError>();
            foreach (var validator in users.PasswordValidators)
            {
                var result = await validator.ValidateAsync(users, user, password);
                if (!result.Succeeded) errors.AddRange(result.Errors);
            }

            if (errors.Count > 0) return CliConsole.Fail("set the new password", errors);

            // همه در یک UpdateAsync: یا همه یا هیچ.
            user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
            user.SecurityStamp = Guid.NewGuid().ToString();
            user.AccessFailedCount = 0;
            user.LockoutEnd = null;

            var update = await users.UpdateAsync(user);
            if (!update.Succeeded) return CliConsole.Fail("save the new password", update.Errors);
        }
        catch (SqliteException ex)
        {
            Console.Error.WriteLine("Database error (were the migrations applied?): " + ex.Message);
            return 1;
        }

        Console.WriteLine($"Password of admin '{username}' was reset. All open sessions of this admin are now invalid.");
        return 0;
    }
}
