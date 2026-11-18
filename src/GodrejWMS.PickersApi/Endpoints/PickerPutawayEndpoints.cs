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

public sealed record RecordInwardVarianceRequest(Domain.Entities.InwardVarianceType Type, decimal QuantityBoxes, string Remarks);

public sealed record RecordExcessMaterialRequest(
    int MaterialId, string MfgMonthText, int PalletPositionId, decimal QuantityBoxes, string Remarks, bool AcknowledgeWarning = false);

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
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm + "|" + PermissionKeys.Inward.Confirm)
            .WithName("GetPickerPutawayQueue");

        group.MapGet("/picker-putaway/location-options", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetAopLocationOptionsQuery(), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm + "|" + PermissionKeys.Inward.Confirm)
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
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm + "|" + PermissionKeys.Inward.Confirm)
            .WithName("PlacePickerPutaway");

        group.MapPost("/picker-putaway/{transactionId:int}/confirm", async (
                int transactionId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new ConfirmPickerPutawayCommand(transactionId), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Confirm)
            .WithName("ConfirmPickerPutaway");

        // ---- Supervisor review (Inward.Confirm): records against a put-away, excess material, material search.
        group.MapPost("/supervisor/putaway/putaways/{putawayId:int}/variances", async (
                int putawayId,
                RecordInwardVarianceRequest request,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(
                        new RecordInwardVarianceCommand(putawayId, request.Type, request.QuantityBoxes, request.Remarks), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Confirm)
            .WithName("RecordInwardVariance");

        group.MapPost("/supervisor/putaway/variances/{varianceId:int}/remove", async (
                int varianceId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    await mediator.Send(new RemoveInwardVarianceCommand(varianceId), cancellationToken);
                    return Results.NoContent();
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Confirm)
            .WithName("RemoveInwardVariance");

        group.MapPost("/supervisor/putaway/{transactionId:int}/excess", async (
                int transactionId,
                RecordExcessMaterialRequest request,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new RecordExcessMaterialCommand(
                        transactionId, request.MaterialId, request.MfgMonthText, request.PalletPositionId, request.QuantityBoxes,
                        request.Remarks, request.AcknowledgeWarning), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Confirm)
            .WithName("RecordExcessMaterial");

        group.MapGet("/supervisor/materials", async (
                string? search,
                int pageNumber = 1,
                int pageSize = 10,
                IMediator mediator = null!,
                CancellationToken cancellationToken = default) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new Application.Features.Materials.Queries.GetMaterialsQuery(
                        Search: search, IsActive: true, PageNumber: pageNumber <= 0 ? 1 : pageNumber, PageSize: pageSize <= 0 ? 10 : pageSize), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Confirm)
            .WithName("SupervisorSearchMaterials");

        return group;
    }
}
