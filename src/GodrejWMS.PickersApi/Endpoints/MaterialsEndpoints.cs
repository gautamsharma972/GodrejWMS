using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Materials.Queries;
using MediatR;

namespace GodrejWMS.PickersApi.Endpoints;

public static class MaterialsEndpoints
{
    public static RouteGroupBuilder MapMaterialsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/materials", async (
                string? search,
                int pageNumber = 1,
                int pageSize = 10,
                IMediator mediator = null!,
                CancellationToken cancellationToken = default) =>
            {
                // The mobile material dropdown only offers active materials: an inactive one cannot be received or allocated anyway.
                var query = new GetMaterialsQuery(
                    Search: search,
                    IsActive: true,
                    PageNumber: pageNumber <= 0 ? 1 : pageNumber,
                    PageSize: pageSize <= 0 ? 10 : pageSize);
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Create)
            .WithName("SearchMaterials");

        return group;
    }
}
