using Microsoft.AspNetCore.Identity;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;

namespace PharmacyWarehouse.Api.Auth;

// قواعد رمز علاوه بر قواعد پیش‌فرض Identity (طول و تعداد نویسه متمایز در Program.cs).
// کدهای خطا حتماً با «Password» شروع می‌شوند: AccountEndpoints فقط این‌ها را ۴۲۲ می‌کند.
public sealed class AppPasswordValidator : IPasswordValidator<AppUser>
{
    // فقط رمزهای رایجِ دست‌کم ۸ نویسه (کوتاه‌تر را سیاست طول رد می‌کند). مقایسه بدون حساسیت به حروف.
    // با حداقل ۸، این فهرست مهم‌ترین دفاع در برابر حدس زدن است.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "12345678", "123456789", "1234567890", "12341234", "11223344", "87654321", "123123123",
        "password", "password1", "password12", "password123", "p@ssw0rd", "passw0rd", "Aa123456",
        "qwertyui", "qwerty123", "qwerty1234", "1qaz2wsx", "1q2w3e4r", "1q2w3e4r5t", "zaq12wsx",
        "asdfghjk", "asdf1234", "abcd1234", "abc12345", "abcdefgh", "iloveyou", "sunshine",
        "admin123", "admin1234", "admin12345", "administrator1", "welcome1", "welcome123",
        "pharmacy1234", "pharmacy", "pharmacy1", "pharmacy123", "computer", "internet", "letmein1",
        "123456789012", "1234567890123", "12345678910111", "qwertyuiop12", "qwertyuiop123",
        "1q2w3e4r5t6y", "1qaz2wsx3edc", "password1234", "password12345", "administrator",
        "admin1234567", "adminadmin12", "welcome12345", "pharmacy123456", "warehouse1234"
    };

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        // رمز خالی یا null را validator پیش‌فرض رد می‌کند.
        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        var errors = new List<IdentityError>();

        if (password.Length > PasswordRules.MaxLength)
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"Password must be at most {PasswordRules.MaxLength} characters."
            });
        }

        // فقط ASCII چاپی (فاصله تا ~). ارقام و حروف فارسی و کنترل‌نویسه رد می‌شوند.
        if (password.Any(c => c < ' ' || c > '~'))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordNotPrintableAscii",
                Description = "Password may contain only printable ASCII characters."
            });
        }

        if (Common.Contains(password))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooCommon",
                Description = "Password is too common."
            });
        }

        return Task.FromResult(errors.Count == 0
            ? IdentityResult.Success
            : IdentityResult.Failed(errors.ToArray()));
    }
}
