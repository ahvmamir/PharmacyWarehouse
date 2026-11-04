using System.Security.Cryptography;

namespace PharmacyWarehouse.Api.Users;

public static class CredentialsGenerator
{
    // بدون نویسه‌های مبهم: 0 و o، و 1 و l و i. فقط حروف کوچک، تا خواندن از صفحه و تایپ ساده‌تر باشد.
    private const string PasswordAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    // باید از PasswordRules.MinLength بزرگ‌تر یا مساوی باشد؛ مقدار ۱۰ در Rehab اینجا رد می‌شد.
    private const int PasswordLength = 14;

    public static string NewPassword()
    {
        while (true)
        {
            var chars = new char[PasswordLength];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
            }

            // سیاست رمز ما حداقل ۵ نویسه متمایز می‌خواهد؛ رمز کم‌تنوع را دور می‌اندازیم.
            if (chars.Distinct().Count() >= 6)
            {
                return new string(chars);
            }
        }
    }
}
