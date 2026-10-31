using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>Bulk inward upload for the Pickers' "Inward without PKM" flow - the exact same
/// multi-sheet Unloading Sheet workbook format as <see cref="ImportInwardWorkbookCommand"/>
/// (see <see cref="IExcelService.ReadInwardWorkbook"/>), except a missing Pkd/Batch never skips a
/// sheet here - every row's PKM is ignored entirely and routed through
/// <see cref="SubmitInwardWithoutPkmCommand"/> instead, landing on empty pallet positions only.
/// Everything else (one GRN per order sheet, both duplicate-safety rules) is identical.</summary>
public sealed record ImportInwardWorkbookWithoutPkmCommand(Stream FileStream, int WarehouseId) : IRequest<IReadOnlyList<InwardSheetImportOutcome>>;

public sealed class ImportInwardWorkbookWithoutPkmValidator : AbstractValidator<ImportInwardWorkbookWithoutPkmCommand>
{
    public ImportInwardWorkbookWithoutPkmValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportInwardWorkbookWithoutPkmHandler(IExcelService excel, IMediator mediator, IApplicationDbContext db, IDateTimeProvider clock)
    : IRequestHandler<ImportInwardWorkbookWithoutPkmCommand, IReadOnlyList<InwardSheetImportOutcome>>
{
    public async Task<IReadOnlyList<InwardSheetImportOutcome>> Handle(ImportInwardWorkbookWithoutPkmCommand request, CancellationToken cancellationToken)
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
                    .Select(r => new SubmitInwardWithoutPkmLine(r.MaterialCode, r.QuantityBoxes))
                    .ToList();

                var result = await mediator.Send(
                    new SubmitInwardWithoutPkmCommand(lines, request.WarehouseId,
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
