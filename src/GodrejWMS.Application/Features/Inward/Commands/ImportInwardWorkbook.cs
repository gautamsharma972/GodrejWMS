using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>Bulk inward (GRN/putaway) upload for the client's real daily workflow: one growing
/// Unloading Sheet workbook, one sheet added per receipt through the day, the whole file
/// re-uploaded each time. Creates one GRN per order sheet (skipping non-order/blank sheets and any
/// sheet with a row missing its required PKM), with duplicate-safety rules so re-uploading the same
/// growing file never creates a second GRN for a receipt already processed. Put-away for every line
/// runs through the existing allocation engine (AOP-preference, location-range mapping, FIFO)
/// exactly as manual entry and the single-sheet upload already do - see
/// <see cref="IExcelService.ReadInwardWorkbook"/> for the exact per-sheet layout this expects. One
/// bad sheet is reported as an error for that sheet only - every other sheet in the same upload is
/// still processed.</summary>
public sealed record ImportInwardWorkbookCommand(Stream FileStream, int WarehouseId) : IRequest<IReadOnlyList<InwardSheetImportOutcome>>;

public sealed class ImportInwardWorkbookValidator : AbstractValidator<ImportInwardWorkbookCommand>
{
    public ImportInwardWorkbookValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportInwardWorkbookHandler(IExcelService excel, IMediator mediator, IApplicationDbContext db, IDateTimeProvider clock)
    : IRequestHandler<ImportInwardWorkbookCommand, IReadOnlyList<InwardSheetImportOutcome>>
{
    public async Task<IReadOnlyList<InwardSheetImportOutcome>> Handle(ImportInwardWorkbookCommand request, CancellationToken cancellationToken)
    {
        var sheets = excel.ReadInwardWorkbook(request.FileStream);
        var outcomes = new List<InwardSheetImportOutcome>();

        var todayStart = clock.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        foreach (var sheet in sheets)
        {
            if (!sheet.IsOrderSheet)
            {
                outcomes.Add(new InwardSheetImportOutcome(sheet.SheetName, InwardSheetImportStatus.SkippedNotOrderSheet, null,
                    "Not an Unloading Sheet (no \"Code\" table header at row 8) - skipped."));
                continue;
            }

            if (sheet.Rows.Count == 0)
            {
                outcomes.Add(new InwardSheetImportOutcome(sheet.SheetName, InwardSheetImportStatus.SkippedNoData, null,
                    "No material rows found before \"Remark\" - skipped."));
                continue;
            }

            if (sheet.RowsMissingPkm.Count > 0)
            {
                outcomes.Add(new InwardSheetImportOutcome(sheet.SheetName, InwardSheetImportStatus.SkippedMissingPkm, null,
                    $"Pkd/Batch is required on every row - missing on row(s) {string.Join(", ", sheet.RowsMissingPkm)} - skipped."));
                continue;
            }

            // Rule 1: trust the client's own marking - if a GRN Reference Number is already
            // sitting at E2, this sheet was already received, no DB lookup needed.
            if (sheet.ExistingGrnReference is not null)
            {
                outcomes.Add(new InwardSheetImportOutcome(sheet.SheetName, InwardSheetImportStatus.SkippedDuplicateReference, sheet.ExistingGrnReference,
                    $"Already received as {sheet.ExistingGrnReference} (GRN Reference Number already present at E2) - skipped."));
                continue;
            }

            // Rule 2: only checked when rule 1 didn't already catch it - same LR No (D2), Vehicle
            // No (D3), and Received From (F5) already received today means this is the same
            // receipt re-seen.
            if (sheet.LrNumber is not null && sheet.VehicleNumber is not null && sheet.ReceivedFrom is not null)
            {
                var existingReference = await db.InwardTransactions
                    .Where(t => !t.IsRejected
                        && t.CreatedAt >= todayStart && t.CreatedAt < todayEnd
                        && t.LrNumber == sheet.LrNumber
                        && t.VehicleNumber == sheet.VehicleNumber
                        && t.ReceivedFrom == sheet.ReceivedFrom)
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => t.ReferenceNumber)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingReference is not null)
                {
                    outcomes.Add(new InwardSheetImportOutcome(sheet.SheetName, InwardSheetImportStatus.SkippedDuplicateLrVehicleSource, existingReference,
                        $"Already received today as {existingReference} for the same LR No ({sheet.LrNumber}), Vehicle No ({sheet.VehicleNumber}) " +
                        $"and Received From ({sheet.ReceivedFrom}) - skipped."));
                    continue;
                }
            }

            try
            {
                var lines = sheet.Rows
                    .Select(r => new SubmitInwardLine(r.MaterialCode, r.QuantityBoxes, r.MfgMonthText))
                    .ToList();

                var result = await mediator.Send(
                    new SubmitInwardCommand(lines, request.WarehouseId,
                        VehicleNumber: sheet.VehicleNumber, LrNumber: sheet.LrNumber, ReceivedFrom: sheet.ReceivedFrom),
                    cancellationToken);

                outcomes.Add(new InwardSheetImportOutcome(sheet.SheetName, InwardSheetImportStatus.Created, result.ReferenceNumber,
                    $"GRN {result.ReferenceNumber} created with {sheet.Rows.Count} line(s)."));
            }
            catch (Exception ex)
            {
                outcomes.Add(new InwardSheetImportOutcome(sheet.SheetName, InwardSheetImportStatus.Error, null, ex.Message));
            }
        }

        return outcomes;
    }
}
