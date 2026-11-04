namespace PharmacyWarehouse.Domain.Contracts;

public sealed class CurrentUserResponse
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string[] Roles { get; init; } = [];
    public bool MustChangePassword { get; init; }
    public string[] Permissions { get; init; } = [];
}