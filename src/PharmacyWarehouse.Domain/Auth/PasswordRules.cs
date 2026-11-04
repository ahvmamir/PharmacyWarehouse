namespace PharmacyWarehouse.Domain.Auth;

// سیاست رمز در یک جا، تا Api و Client از هم جدا نشوند. سرور مرجع است.
public static class PasswordRules
{
    // برای همه‌ی نقش‌ها یکسان. ۸ به خواست امیر (پیش‌تر ۱۲)؛ جبرانش: فهرست رمزهای رایج کوتاه در AppPasswordValidator
    // و قفل ۵ دقیقه‌ای بعد از ۵ تلاش ناموفق. مطابق کف NIST SP 800-63B برای رمزی که کاربر انتخاب می‌کند.
    public const int MinLength = 8;
    public const int MaxLength = 128;
    public const int MinUniqueChars = 5;
}
