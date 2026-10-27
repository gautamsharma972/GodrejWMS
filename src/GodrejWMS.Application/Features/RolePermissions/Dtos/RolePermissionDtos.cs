namespace GodrejWMS.Application.Features.RolePermissions.Dtos;

public sealed record RolePermissionMatrixDto(
    IReadOnlyList<string> Roles,
    IReadOnlyList<PermissionRowDto> Rows);

/// <summary>One permission key's grant state across every role, for one row of the matrix UI.</summary>
public sealed record PermissionRowDto(
    string PermissionKey,
    string Module,
    string Action,
    IReadOnlyDictionary<string, bool> GrantedByRole);
