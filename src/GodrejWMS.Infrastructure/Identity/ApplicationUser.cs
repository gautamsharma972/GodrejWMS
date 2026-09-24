using Microsoft.AspNetCore.Identity;

namespace GodrejWMS.Infrastructure.Identity;

/// <summary>Warehouse staff account. Extends the standard Identity user with a display name and active flag.</summary>
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
