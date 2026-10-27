using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.RolePermissions.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.RolePermissions.Queries;

public sealed record GetRolePermissionMatrixQuery : IRequest<RolePermissionMatrixDto>;

public sealed class GetRolePermissionMatrixHandler(IApplicationDbContext db, IRoleManagementService roleManagement)
    : IRequestHandler<GetRolePermissionMatrixQuery, RolePermissionMatrixDto>
{
    public async Task<RolePermissionMatrixDto> Handle(GetRolePermissionMatrixQuery request, CancellationToken cancellationToken)
    {
        var roles = await roleManagement.GetAllRoleNamesAsync(cancellationToken);
        var grants = await db.RolePermissions.AsNoTracking().ToListAsync(cancellationToken);
        var byKey = grants.GroupBy(g => g.PermissionKey)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.RoleName, x => x.IsGranted));

        var rows = PermissionKeys.All.Select(key =>
        {
            var parts = key.Split('.', 2);
            var module = parts[0];
            var action = parts.Length > 1 ? parts[1] : key;
            var grantedByRole = roles.ToDictionary(r => r, r => byKey.TryGetValue(key, out var g) && g.TryGetValue(r, out var granted) && granted);
            return new PermissionRowDto(key, module, action, grantedByRole);
        }).ToList();

        return new RolePermissionMatrixDto(roles, rows);
    }
}
