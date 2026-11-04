namespace PharmacyWarehouse.Domain.Contracts;

public sealed class ResetPasswordResponse
{
    public Guid Id { get; init; }

    // فقط در همین پاسخ و فقط یک‌بار؛ در دیتابیس فقط hash آن می‌ماند.
    public string NewPassword { get; init; } = "";

    public override string ToString() => $"ResetPasswordResponse {{ Id = {Id}, NewPassword = *** }}";
}
