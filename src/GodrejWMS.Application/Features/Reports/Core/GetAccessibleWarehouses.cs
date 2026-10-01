using GodrejWMS.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Core;

public sealed record WarehouseOption(int Id, string Code, string Name);
public sealed record GetAccessibleWarehousesQuery : IRequest<IReadOnlyList<WarehouseOption>>;

public sealed class GetAccessibleWarehousesHandler(IApplicationDbContext db, ICurrentUserService user)
    : IRequestHandler<GetAccessibleWarehousesQuery, IReadOnlyList<WarehouseOption>>
{
    public async Task<IReadOnlyList<WarehouseOption>> Handle(GetAccessibleWarehousesQuery request, CancellationToken ct) =>
        await db.Warehouses.AsNoTracking().Where(w => w.IsActive &&
                ((user.IsInRole("Admin") || user.IsInRole("Supervisor") || user.IsInRole("Operator")) ||
                 db.UserWarehouses.Any(a => a.UserId == user.UserId && a.WarehouseId == w.Id)))
            .OrderBy(w => w.Code)
            .Select(w => new WarehouseOption(w.Id, w.Code, w.Name)).ToListAsync(ct);
}
