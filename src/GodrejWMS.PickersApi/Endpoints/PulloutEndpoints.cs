using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using MediatR;

namespace GodrejWMS.PickersApi.Endpoints;

public static class PulloutEndpoints
{
    public static RouteGroupBuilder MapPulloutEndpoints(this RouteGroupBuilder group)
    {
        // Floor Supervisor's outward queue: pending (not yet confirmed, not rejected) pullouts
        // waiting on a pick to be physically worked and confirmed - same GetPulloutHistoryQuery
        // the web app's /pullout/history page uses, just pre-filtered to the pending state.
        group.MapGet("/pullout/queue", async (
                string? search,
                int? warehouseId,
                int pageNumber = 1,
                int pageSize = 20,
                IMediator mediator = null!,
                CancellationToken cancellationToken = default) =>
            {
                var query = new GetPulloutHistoryQuery(
                    Search: search,
                    WarehouseId: warehouseId,
                    PageNumber: pageNumber <= 0 ? 1 : pageNumber,
                    PageSize: pageSize <= 0 ? 20 : pageSize,
                    IsConfirmed: false,
                    IsRejected: false);
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.View)
            .WithName("GetPulloutQueue");

        group.MapGet("/pullout/{id:int}", async (
                int id,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetPulloutDetailQuery(id), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.View)
            .WithName("GetPulloutDetail");

        group.MapGet("/pullout/pick-options", async (
                string referenceNumber,
                long materialNumber,
                string currentLocationCode,
                string mfgMonthLabel,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    var query = new GetPulloutPickChangeOptionsQuery(referenceNumber, materialNumber, currentLocationCode, mfgMonthLabel);
                    return Results.Ok(await mediator.Send(query, cancellationToken));
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Edit)
            .WithName("GetPulloutPickOptions");

        group.MapPost("/pullout/change-pick-location", async (
                ChangePulloutPickLocationCommand command,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(command, cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Edit)
            .WithName("ChangePulloutPickLocation");

        group.MapPost("/pullout/{referenceNumber}/confirm", async (
                string referenceNumber,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new ConfirmPulloutCommand(referenceNumber), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Confirm)
            .WithName("ConfirmPullout");

        // ----- Picker: confirm picks, change the pick location, relocate what is left (Pullout.Pick) -----

        group.MapPost("/pullout/picks/{pickId:int}/confirm", async (
                int pickId,
                ConfirmPickRequest request,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    var move = request.MoveRemaining is { } m ? new PulloutRemainderMove(m.DestinationPalletPositionId, m.QuantityBoxes) : null;
                    return Results.Ok(await mediator.Send(new ConfirmPulloutPickCommand(pickId, request.PickedQuantityBoxes, move), cancellationToken));
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Pick)
            .WithName("ConfirmPulloutPick");

        group.MapGet("/pullout/picker/queue", async (
                string? search,
                int pageNumber = 1,
                int pageSize = 20,
                IMediator mediator = null!,
                CancellationToken cancellationToken = default) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetPickerPendingPulloutsQuery(
                        search, pageNumber <= 0 ? 1 : pageNumber, pageSize <= 0 ? 20 : pageSize), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Pick)
            .WithName("GetPickerPulloutQueue");

        group.MapGet("/pullout/picks/{pickId:int}/remainder", async (
                int pickId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetPickRemainderQuery(pickId), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Pick)
            .WithName("GetPulloutPickRemainder");

        group.MapGet("/pullout/picks/{pickId:int}/relocation-suggestions", async (
                int pickId,
                decimal quantity,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetRelocationSuggestionsQuery(pickId, quantity), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Pick)
            .WithName("GetPulloutRelocationSuggestions");

        group.MapGet("/pullout/picker/pick-options", async (
                string referenceNumber,
                long materialNumber,
                string currentLocationCode,
                string mfgMonthLabel,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    var query = new GetPulloutPickChangeOptionsQuery(referenceNumber, materialNumber, currentLocationCode, mfgMonthLabel);
                    return Results.Ok(await mediator.Send(query, cancellationToken));
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Pick)
            .WithName("GetPulloutPickerPickOptions");

        group.MapPost("/pullout/picker/change-pick-location", async (
                ChangePulloutPickLocationCommand command,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(command, cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Pullout.Pick)
            .WithName("PickerChangePulloutPickLocation");

        return group;
    }
}

public sealed record RemainderMoveRequest(int DestinationPalletPositionId, decimal QuantityBoxes);

public sealed record ConfirmPickRequest(decimal PickedQuantityBoxes, RemainderMoveRequest? MoveRemaining = null);
