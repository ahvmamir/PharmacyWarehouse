namespace PharmacyWarehouse.Domain.Contracts;

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; init; } = "";
    public string NewPassword { get; init; } = "";

    // عمداً override شده: اگر کسی اشتباهاً کل شیء را لاگ کند، رمزها در لاگ نیایند.
    public override string ToString() => "ChangePasswordRequest { CurrentPassword = ***, NewPassword = *** }";
}