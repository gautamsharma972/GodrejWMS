using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.CycleCount.Commands;
using GodrejWMS.Application.Features.CycleCount.Queries;
using GodrejWMS.Application.Features.Materials.Queries;
using MediatR;

namespace GodrejWMS.PickersApi.Endpoints;

public sealed record CorrectCycleCountCardRequest(long? MaterialNumber, string? MfgMonthText, decimal PhysicalQuantityBoxes, int? StockSubtypeId = null);

public sealed record AddCycleCountFoundStockRequest(int PalletPositionId, long MaterialNumber, string MfgMonthText, decimal PhysicalQuantityBoxes, int StockSubtypeId = 1);

/// <summary>The counting side of Cycle Count for the Pickers app: the same handlers the web Count screen uses, so card order, validation,
/// the submit and the Inward / Pullout freeze behave identically. Scheduling, review and reports are web-only.</summary>
public static class CycleCountEndpoints
{
    private const string PerformPolicy = "Permission:" + PermissionKeys.CycleCount.Perform + "|" + PermissionKeys.CycleCount.Submit;

    public static RouteGroupBuilder MapCycleCountEndpoints(this RouteGroupBuilder group)
    {
        var g = group.MapGroup("/cycle-count");

        g.MapGet("/my-batches", async (IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new GetMyCycleCountBatchesQuery(), ct))))
            .RequireAuthorization(PerformPolicy).WithName("GetMyCycleCountBatches");

        g.MapPost("/batches/{batchId:int}/start", async (int batchId, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new StartCycleCountCommand(batchId), ct))))
            .RequireAuthorization(PerformPolicy).WithName("StartCycleCount");

        g.MapGet("/batches/{batchId:int}/racks/{rackCode}/locations", async (int batchId, string rackCode, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new GetCycleCountLocationsQuery(batchId, rackCode), ct))))
            .RequireAuthorization(PerformPolicy).WithName("GetCycleCountLocations");

        g.MapGet("/batches/{batchId:int}/racks/{rackCode}/cards", async (int batchId, string rackCode, string? from, string? to, int? stockSubtypeId, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new GetCycleCountCardsQuery(batchId, rackCode, from, to, stockSubtypeId), ct))))
            .RequireAuthorization(PerformPolicy).WithName("GetCycleCountCards");

        g.MapGet("/batches/{batchId:int}/racks/{rackCode}/summary", async (int batchId, string rackCode, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new GetCycleCountSubmitSummaryQuery(batchId, rackCode), ct))))
            .RequireAuthorization(PerformPolicy).WithName("GetCycleCountSubmitSummary");

        g.MapPost("/batches/{batchId:int}/racks/{rackCode}/submit", async (int batchId, string rackCode, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new SubmitCycleCountRackCommand(batchId, rackCode), ct))))
            .RequireAuthorization("Permission:" + PermissionKeys.CycleCount.Submit).WithName("SubmitCycleCountRack");

        g.MapPost("/cards/{cardId:int}/verify", async (int cardId, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new VerifyCycleCountCardCommand(cardId), ct))))
            .RequireAuthorization("Permission:" + PermissionKeys.CycleCount.Perform).WithName("VerifyCycleCountCard");

        g.MapPost("/cards/{cardId:int}/correct", async (int cardId, CorrectCycleCountCardRequest request, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new CorrectCycleCountCardCommand(cardId, request.MaterialNumber, request.MfgMonthText, request.PhysicalQuantityBoxes, request.StockSubtypeId), ct))))
            .RequireAuthorization("Permission:" + PermissionKeys.CycleCount.Perform).WithName("CorrectCycleCountCard");

        g.MapPost("/batches/{batchId:int}/found-stock", async (int batchId, AddCycleCountFoundStockRequest request, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new AddCycleCountFoundStockCommand(batchId, request.PalletPositionId, request.MaterialNumber, request.MfgMonthText, request.PhysicalQuantityBoxes, request.StockSubtypeId), ct))))
            .RequireAuthorization("Permission:" + PermissionKeys.CycleCount.Perform).WithName("AddCycleCountFoundStock");

        g.MapPost("/batches/{batchId:int}/racks/{rackCode}/damage", async (int batchId, string rackCode, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new EnableCycleCountDamageCountingCommand(batchId, rackCode), ct))))
            .RequireAuthorization("Permission:" + PermissionKeys.CycleCount.Perform).WithName("EnableCycleCountDamageCounting");

        // Active materials for the correction screen (a counter may not hold the Inward / Pullout permissions the other material search needs).
        g.MapGet("/materials", async (string? search, int pageNumber, int pageSize, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(
                    new GetMaterialsQuery(Search: search, IsActive: true, PageNumber: pageNumber <= 0 ? 1 : pageNumber, PageSize: pageSize <= 0 ? 15 : pageSize), ct))))
            .RequireAuthorization("Permission:" + PermissionKeys.CycleCount.Perform).WithName("SearchCycleCountMaterials");

        // Any signed-in user may ask: used for the "cycle count in progress" banner on the Inward entry screen.
        g.MapGet("/freeze", async (int warehouseId, IMediator mediator, CancellationToken ct) =>
                await ApiExceptionMapping.RunAsync(async () => Results.Ok(await mediator.Send(new GetCycleCountFreezeQuery(warehouseId), ct))))
            .RequireAuthorization().WithName("GetCycleCountFreeze");

        return group;
    }
}
