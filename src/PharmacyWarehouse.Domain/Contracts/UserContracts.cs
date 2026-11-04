namespace PharmacyWarehouse.Domain.Contracts;

public sealed class UserItem
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Role { get; init; } = "";
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }
    public string[] Grants { get; init; } = [];
    public string[] Denies { get; init; } = [];
}

public sealed class CreateUserRequest
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = "";
}

public sealed class CreateUserResponse
{
    public UserItem User { get; init; } = new();

    // فقط در همین پاسخ و فقط یک‌بار؛ در دیتابیس فقط hash آن می‌ماند.
    public string InitialPassword { get; init; } = "";

    public override string ToString() => $"CreateUserResponse {{ Id = {User.Id}, InitialPassword = *** }}";
}
