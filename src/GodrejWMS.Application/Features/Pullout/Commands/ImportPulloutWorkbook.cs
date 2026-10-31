using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Bulk pullout upload for the client's real daily workflow: one growing Excel workbook,
/// one sheet added per order through the day, the whole file re-uploaded each time. Creates one
/// pullout per order sheet (skipping non-order/blank sheets), with duplicate-safety rules so
/// re-uploading the same growing file never creates a second pullout for an order already pulled.
/// See <see cref="IExcelService.ReadPulloutWorkbook"/> for the exact per-sheet layout this expects.
/// One bad sheet (e.g. a material code not in Material Master) is reported as an error for that
/// sheet only - every other sheet in the same upload is still processed.</summary>
public sealed record ImportPulloutWorkbookCommand(Stream FileStream, int WarehouseId) : IRequest<IReadOnlyList<PulloutSheetImportOutcome>>;

public sealed class ImportPulloutWorkbookValidator : AbstractValidator<ImportPulloutWorkbookCommand>
{
    public ImportPulloutWorkbookValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportPulloutWorkbookHandler(IExcelService excel, IMediator mediator, IApplicationDbContext db, IDateTimeProvider clock)
    : IRequestHandler<ImportPulloutWorkbookCommand, IReadOnlyList<PulloutSheetImportOutcome>>
{
    public async Task<IReadOnlyList<PulloutSheetImportOutcome>> Handle(ImportPulloutWorkbookCommand request, CancellationToken cancellationToken)
    {
        var sheets = excel.ReadPulloutWorkbook(request.FileStream);
        var outcomes = new List<PulloutSheetImportOutcome>();

        var todayStart = clock.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        foreach (var sheet in sheets)
        {
            if (!sheet.IsOrderSheet)
            {
                outcomes.Add(new PulloutSheetImportOutcome(sheet.SheetName, PulloutSheetImportStatus.SkippedNotOrderSheet, null,
                    "Not a Loading Sheet (no \"Code\" table header at row 12) - skipped."));
                continue;
            }

            if (sheet.Rows.Count == 0)
            {
                outcomes.Add(new PulloutSheetImportOutcome(sheet.SheetName, PulloutSheetImportStatus.SkippedNoData, null,
                    "No material rows found before \"Driver Sign\" - skipped."));
                continue;
            }

            // Rule 1: trust the client's own marking - if a Pick Reference Number is already
            // sitting at H10, this sheet was already pulled, no DB lookup needed.
            if (sheet.ExistingPickReference is not null)
            {
                outcomes.Add(new PulloutSheetImportOutcome(sheet.SheetName, PulloutSheetImportStatus.SkippedDuplicateReference, sheet.ExistingPickReference,
                    $"Already pulled as {sheet.ExistingPickReference} (Pick Reference Number already present at H10) - skipped."));
                continue;
            }

            // Rule 2: only checked when rule 1 didn't already catch it - same Destination (B10)
            // and Vehicle No (B9) already pulled today means this is the same order re-seen.
            if (sheet.VehicleNumber is not null && sheet.Destination is not null)
            {
                var existingReference = await db.PulloutTransactions
                    .Where(t => !t.IsRejected
                        && t.CreatedAt >= todayStart && t.CreatedAt < todayEnd
                        && t.VehicleNumber == sheet.VehicleNumber
                        && t.Lines.Any(l => l.PlantName == sheet.Destination))
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => t.ReferenceNumber)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingReference is not null)
                {
                    outcomes.Add(new PulloutSheetImportOutcome(sheet.SheetName, PulloutSheetImportStatus.SkippedDuplicateDestinationVehicle, existingReference,
                        $"Already pulled today as {existingReference} for the same Destination ({sheet.Destination}) and Vehicle No ({sheet.VehicleNumber}) - skipped."));
                    continue;
                }
            }

            try
            {
                var lines = sheet.Rows
                    .Select(r => new SubmitPulloutLine(r.MaterialCode, r.QuantityBoxes, r.PlantName, r.Priority, r.MaterialDescription))
                    .ToList();

                var result = await mediator.Send(
                    new SubmitPulloutCommand(lines, HeaderFields: sheet.HeaderFields, WarehouseId: request.WarehouseId),
                    cancellationToken);

                outcomes.Add(new PulloutSheetImportOutcome(sheet.SheetName, PulloutSheetImportStatus.Created, result.ReferenceNumber,
                    $"Pullout {result.ReferenceNumber} created with {sheet.Rows.Count} line(s)."));
            }
            catch (Exception ex)
            {
                outcomes.Add(new PulloutSheetImportOutcome(sheet.SheetName, PulloutSheetImportStatus.Error, null, ex.Message));
            }
        }

        return outcomes;
    }
}
