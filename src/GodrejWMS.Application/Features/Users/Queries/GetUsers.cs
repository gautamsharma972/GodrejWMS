using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Users.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Users.Queries;

/// <summary>Lists users with their role and scope. Superadmin sees everyone (organization-wide,
/// section 6). A Head Supervisor sees only users assigned to their own warehouse - the list this
/// backs is also the password-reset eligible-user list from spec 14.2 ("Display only users within
/// the Head Supervisor's authorized warehouse scope"). Anyone else gets an empty list; this query
/// is not how most roles would reach this screen anyway (the Web page itself is Superadmin/Head
/// Supervisor only), but the handler enforces it independently of that page-level gate.</summary>
public sealed record GetUsersQuery : IRequest<IReadOnlyList<UserListItemDto>>;

public sealed class GetUsersHandler(IUserManagementService users, IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetUsersQuery, IReadOnlyList<UserListItemDto>>
{
    public async Task<IReadOnlyList<UserListItemDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var accounts = await users.ListUsersAsync(cancellationToken);

        var warehouseAssignments = await db.UserWarehouses
            .Select(w => new { w.UserId, w.WarehouseId, w.Warehouse.Name })
            .ToDictionaryAsync(w => w.UserId, cancellationToken);

        var regionAssignments = await db.UserRegions
            .Select(r => new { r.UserId, r.Region.Name })
            .ToListAsync(cancellationToken);
        var regionsByUser = regionAssignments.GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name).OrderBy(n => n).ToList());

        var isSuperadmin = currentUser.IsPlatformLevel();
        int? callerWarehouseId = null;
        if (!isSuperadmin && currentUser.IsInRole(KnownRoles.HeadSupervisor) && currentUser.UserId is { } callerId)
        {
            warehouseAssignments.TryGetValue(callerId, out var callerWarehouse);
            callerWarehouseId = callerWarehouse?.WarehouseId;
        }
        else if (!isSuperadmin)
        {
            // Not Superadmin and not Head Supervisor - no legitimate reason to list users here.
            return [];
        }

        var items = accounts.Select(a =>
        {
            warehouseAssignments.TryGetValue(a.UserId, out var warehouse);
            regionsByUser.TryGetValue(a.UserId, out var regions);
            return new UserListItemDto(
                a.UserId, a.Email, a.FullName, a.IsActive,
                a.Roles.FirstOrDefault(),
                warehouse?.WarehouseId, warehouse?.Name,
                regions ?? []);
        });

        if (callerWarehouseId is { } wid)
        {
            items = items.Where(u => u.WarehouseId == wid);
        }

        return items.ToList();
    }
}
