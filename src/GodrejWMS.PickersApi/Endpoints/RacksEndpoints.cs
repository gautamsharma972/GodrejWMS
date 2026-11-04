using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Racks.Queries;
using MediatR;

namespace GodrejWMS.PickersApi.Endpoints;

public static class RacksEndpoints
{
    public static RouteGroupBuilder MapRacksEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/racks/pallet-position/{locationCode}", async (
                string locationCode,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetPalletPositionByCodeQuery(locationCode), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Menu.ViewInwardNoPkm)
            .WithName("GetPalletPositionByCode");

        return group;
    }
}
