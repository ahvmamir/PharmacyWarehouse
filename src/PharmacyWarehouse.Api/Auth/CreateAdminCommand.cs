using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Auth;

public static class CreateAdminCommand
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: create-admin <username>");
            return 1;
        }

        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("This command needs an interactive console (the password is typed hidden).");
            return 1;
        }

        // مثل صفحه‌ی ورود؛ وگرنه نام کاربری با رقم فارسی ساخته می‌شد و هرگز قابل ورود نبود.
        var username = TextNormalizer.ToAsciiDigits(args[1].Trim());
        var password = CliConsole.ReadPassword("Password: ");
        var confirmation = CliConsole.ReadPassword("Repeat password: ");

        if (password != confirmation)
        {
            Console.Error.WriteLine("Passwords do not match. Nothing was created.");
            return 1;
        }

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();


        try
        {
            if (await users.FindByNameAsync(username) is not null)
            {
                Console.Error.WriteLine($"User '{username}' already exists.");
                return 1;
            }


            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = username,
                DisplayName = username,
                MustChangePassword = false,
                IsActive = true
            };

            var createResult = await users.CreateAsync(user, password);
            if (!createResult.Succeeded) return CliConsole.Fail("create user", createResult.Errors);

            var roleAssign = await users.AddToRoleAsync(user, Roles.Admin);
            if (!roleAssign.Succeeded)
            {
                await users.DeleteAsync(user);
                return CliConsole.Fail("assign admin role", roleAssign.Errors);
            }
        }
        catch (SqliteException ex)
        {
            Console.Error.WriteLine("Database error (were the migrations applied?): " + ex.Message);
            return 1;
        }

        Console.WriteLine($"Admin '{username}' created.");
        return 0;
    }
}
