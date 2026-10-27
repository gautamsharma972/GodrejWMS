namespace GodrejWMS.Domain.Entities;

/// <summary>Whether a given role holds a given permission (see PermissionKeys in the Application
/// layer for the fixed catalog of keys). Superadmin-editable on the Role &amp; Permission
/// Management screen - this is the data half of the configurable permission matrix; which keys
/// exist is fixed by the app's features (code), but which role has which key is entirely here.</summary>
public class RolePermission
{
    public string RoleName { get; set; } = string.Empty;
    public string PermissionKey { get; set; } = string.Empty;
    public bool IsGranted { get; set; }
}
