using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace GodrejWMS.PickersApi.Endpoints;

public sealed record ChangeInwardPutawayLocationRequest(int NewPalletPositionId, string? Reason = null, decimal? QuantityBoxes = null);

public static class InwardNoPkmEndpoints
{
    private const long MaxImportBytes = 10 * 1024 * 1024;

    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static RouteGroupBuilder MapInwardNoPkmEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/inward-no-pkm/submit", async (
                SubmitInwardWithoutPkmCommand command,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(command, cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Create)
            .WithName("SubmitInwardWithoutPkm");

        group.MapPost("/inward-no-pkm/import", async (
                IFormFile file,
                [FromForm] int warehouseId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    if (file.Length is 0 or > MaxImportBytes || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        return Results.BadRequest(new { message = "Upload an .xlsx file of up to 10 MB." });
                    }

                    await using var stream = new MemoryStream();
                    await file.CopyToAsync(stream, cancellationToken);
                    stream.Position = 0;
                    var outcomes = await mediator.Send(new ImportInwardWorkbookWithoutPkmCommand(stream, warehouseId), cancellationToken);
                    return Results.Ok(outcomes);
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Create)
            .DisableAntiforgery()
            .WithName("ImportInwardWorkbookWithoutPkm");

        group.MapGet("/inward-no-pkm/sample-template", (IExcelService excel) =>
                Results.File(
                    excel.WriteInwardWorkbookSampleTemplate(),
                    XlsxContentType,
                    "inward-no-pkm-sample.xlsx"))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Create)
            .WithName("GetInwardNoPkmSampleTemplate");

        group.MapGet("/inward-no-pkm/history", async (
                string? search,
                int? warehouseId,
                string? sortBy,
                bool sortDescending = true,
                int pageNumber = 1,
                int pageSize = 20,
                IMediator mediator = null!,
                CancellationToken cancellationToken = default) =>
            {
                var query = new GetInwardHistoryWithoutPkmQuery(
                    Search: search,
                    WarehouseId: warehouseId,
                    SortBy: string.IsNullOrWhiteSpace(sortBy) ? "created" : sortBy,
                    SortDescending: sortDescending,
                    PageNumber: pageNumber <= 0 ? 1 : pageNumber,
                    PageSize: pageSize <= 0 ? 20 : pageSize);
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .RequireAuthorization("Permission:" + PermissionKeys.Menu.ViewInwardNoPkm)
            .WithName("GetInwardNoPkmHistory");

        group.MapGet("/inward-no-pkm/{id:int}", async (
                int id,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetInwardDetailQuery(id), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Menu.ViewInwardNoPkm)
            .WithName("GetInwardNoPkmDetail");

        group.MapGet("/inward-no-pkm/{id:int}/download", async (
                int id,
                IMediator mediator,
                IExcelService excel,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    var detail = await mediator.Send(new GetInwardDetailQuery(id), cancellationToken);
                    var bytes = excel.WriteInwardDownload(InwardDownloadMapper.PackageFromDetail(detail));
                    return Results.File(bytes, XlsxContentType, $"{detail.ReferenceNumber}.xlsx");
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Menu.ViewInwardNoPkm)
            .WithName("DownloadInwardNoPkmDetail");

        group.MapPost("/inward-no-pkm/assign-pkm", async (
                AssignInwardPutawayPkmCommand command,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(command, cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.AssignPkm)
            .WithName("AssignInwardPutawayPkm");

        // Floor Supervisor: edit a pending putaway's location, then confirm the whole GRN once
        // every location has been physically worked - see GetInwardPalletChangeOptionsQuery /
        // ChangeInwardPutawayLocationCommand / ConfirmInwardPutawayCommand (the same commands
        // Inward/Detail.razor's non-Picker "edit putaway" UI already calls).
        group.MapGet("/inward-no-pkm/putaways/{putawayId:int}/location-options", async (
                int putawayId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new GetInwardPalletChangeOptionsQuery(putawayId), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Edit)
            .WithName("GetInwardPutawayLocationOptions");

        group.MapPost("/inward-no-pkm/putaways/{putawayId:int}/change-location", async (
                int putawayId,
                ChangeInwardPutawayLocationRequest request,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                {
                    var command = new ChangeInwardPutawayLocationCommand(putawayId, request.NewPalletPositionId, request.Reason, request.QuantityBoxes);
                    return Results.Ok(await mediator.Send(command, cancellationToken));
                }))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Edit)
            .WithName("ChangeInwardPutawayLocation");

        group.MapPost("/inward-no-pkm/{referenceNumber}/confirm", async (
                string referenceNumber,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                await ApiExceptionMapping.RunAsync(async () =>
                    Results.Ok(await mediator.Send(new ConfirmInwardPutawayCommand(referenceNumber), cancellationToken))))
            .RequireAuthorization("Permission:" + PermissionKeys.Inward.Confirm)
            .WithName("ConfirmInwardPutaway");

        return group;
    }
}
