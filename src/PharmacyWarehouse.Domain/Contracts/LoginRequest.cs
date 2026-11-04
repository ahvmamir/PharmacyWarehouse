namespace PharmacyWarehouse.Domain.Contracts;

public sealed class LoginRequest
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";

    // عمداً override شده: رمز و نام کاربری هرگز نباید در لاگ بیایند.
    public override string ToString() => "LoginRequest { Username = ***, Password = *** }";
}