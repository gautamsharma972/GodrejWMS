using GodrejWMS.Application.Common.Interfaces;
using MediatR;

namespace GodrejWMS.Application.Features.Reports.Core;

public sealed record GetAccessibleWarehousesQuery : IRequest<IReadOnlyList<WarehouseOption>>;

public sealed class GetAccessibleWarehousesHandler(IWarehouseAccessService warehouseAccess)
    : IRequestHandler<GetAccessibleWarehousesQuery, IReadOnlyList<WarehouseOption>>
{
    public Task<IReadOnlyList<WarehouseOption>> Handle(GetAccessibleWarehousesQuery request, CancellationToken ct) =>
        warehouseAccess.GetAccessibleWarehousesAsync(ct);
}
