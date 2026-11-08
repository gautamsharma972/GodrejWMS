using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Queries;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using MediatR;

namespace GodrejWMS.PickersApi.Endpoints;

public sealed record PickerPlaceInwardPutawayRequest(
    decimal QuantityBoxes,
    string? MfgMonthText,
    int? NewPalletPositionId,
    string? ExcludedLocation,
    bool AcknowledgeWarning = false);

/// <summary>Mirrors the Picker Put-away screen (InwardPicker/Index.razor, route /inward-picker) - a
/// queue of GRNs that still have a put-away location to be worked, plus the ability to confirm or
/// change each one's quantity, PKM and location (including setting it aside as GIT / NO RACK).
/// Gated on Inward.AssignPkm, the same permission the Pickers role already holds for the one-shot
/// PKM assignment at /api/inward-no-pkm/assign-pkm.</summary>
public static class PickerPutawayEndpoints
{
    public static RouteGroupBuilder MapPickerPutawayEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/picker-putaway/queue", async (
                string? search,
                int pageNumber,
                int pageSize,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                var query = new GetPickerPendingInwardQuery(
                    Search: search,
                    PageNumber: pageNumber <= 0 ? 1 : pageNumber,
                    PageSize: pageSize <= 0 ? 10 : pageSize);
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm)
            .WithName("GetPickerPutawayQueue");

        group.MapGet("/picker-putaway/location-options", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetAopLocationOptionsQuery(), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm)
            .WithName("GetPickerPutawayLocationOptions");

        group.MapPost("/picker-putaway/putaways/{putawayId:int}/place", async (
                int putawayId,
                PickerPlaceInwardPutawayRequest request,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    var command = new PickerPlaceInwardPutawayCommand(
                        putawayId,
                        request.QuantityBoxes,
                        request.MfgMonthText,
                        request.NewPalletPositionId,
                        request.ExcludedLocation,
                        request.AcknowledgeWarning);
                    return Results.Ok(await mediator.Send(command, cancellationToken));
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm)
            .WithName("PlacePickerPutaway");

        group.MapPost("/picker-putaway/{transactionId:int}/confirm", async (
                int transactionId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new ConfirmPickerPutawayCommand(transactionId), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm)
            .WithName("ConfirmPickerPutaway");

        return group;
    }
}
