using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;

namespace GodrejWMS.PickersApi.Endpoints;

public static class WarehousesEndpoints
{
    public static RouteGroupBuilder MapWarehousesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/warehouses", async (IWarehouseAccessService warehouses, CancellationToken cancellationToken) =>
                Results.Ok(await warehouses.GetAccessibleWarehousesAsync(cancellationToken)))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Create)
            .WithName("GetWarehouses");

        return group;
    }
}
