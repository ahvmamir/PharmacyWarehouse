using Microsoft.AspNetCore.Identity;

namespace PharmacyWarehouse.Api.Data;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
}
