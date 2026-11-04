namespace PharmacyWarehouse.Domain.Contracts;

public sealed class SetInitialPasswordRequest
{
    public string NewPassword { get; init; } = "";

    // عمداً override شده: اگر کسی اشتباهاً کل شیء را لاگ کند، رمز در لاگ نیاید.
    public override string ToString() => "SetInitialPasswordRequest { NewPassword = *** }";
}