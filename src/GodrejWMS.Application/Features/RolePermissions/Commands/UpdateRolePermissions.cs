using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.RolePermissions.Commands;

public sealed record RolePermissionAssignment(string RoleName, string PermissionKey, bool IsGranted);

/// <summary>Bulk-updates the permission matrix. Superadmin-only - and Superadmin's own row is
/// never actually applied even if submitted differently, since Superadmin always has every
/// permission by definition (organization-wide access per spec section 5.1), so there is nothing
/// to "lock yourself out of" here the way there would be for the other four roles.</summary>
public sealed record UpdateRolePermissionsCommand(IReadOnlyList<RolePermissionAssignment> Assignments) : IRequest;

public sealed class UpdateRolePermissionsValidator : AbstractValidator<UpdateRolePermissionsCommand>
{
    public UpdateRolePermissionsValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsPlatformLevel())
            .WithMessage("Only Superadmin can configure role permissions.");
        RuleForEach(x => x.Assignments).ChildRules(a =>
        {
            a.RuleFor(m => m.RoleName).NotEmpty();
            a.RuleFor(m => m.PermissionKey).Must(k => PermissionKeys.All.Contains(k))
                .WithMessage("Unknown permission key.");
        });
    }
}

public sealed class UpdateRolePermissionsHandler(IApplicationDbContext db, PermissionRevision permissionRevision)
    : IRequestHandler<UpdateRolePermissionsCommand>
{
    public async Task Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var byKey = await db.RolePermissions
            .ToDictionaryAsync(p => (p.RoleName, p.PermissionKey), cancellationToken);

        foreach (var assignment in request.Assignments)
        {
            // Superadmin and Platform Admin are always fully granted - see class remarks.
            if (assignment.RoleName is "Superadmin" or KnownRoles.PlatformAdmin)
            {
                continue;
            }

            if (byKey.TryGetValue((assignment.RoleName, assignment.PermissionKey), out var row))
            {
                row.IsGranted = assignment.IsGranted;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        permissionRevision.Increment();
    }
}
