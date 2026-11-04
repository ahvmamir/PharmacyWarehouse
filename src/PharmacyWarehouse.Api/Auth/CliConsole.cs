using System.Text;
using Microsoft.AspNetCore.Identity;

namespace PharmacyWarehouse.Api.Auth;

// ابزار مشترک فرمان‌های مدیریتی (create-admin و reset-password).
internal static class CliConsole
{
    // رمز را بدون نمایش روی صفحه می‌خواند (Backspace کار می‌کند).
    public static string ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var buffer = new StringBuilder();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
    }

    public static int Fail(string action, IEnumerable<IdentityError> errors)
    {
        Console.Error.WriteLine($"Could not {action}:");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($" - {error.Code}: {error.Description}");
        }
        return 1;
    }
}