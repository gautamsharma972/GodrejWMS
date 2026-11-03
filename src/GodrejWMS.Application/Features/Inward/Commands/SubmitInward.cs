using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>
/// Submits one or more inward (goods-receipt) lines and reserves suggested put-away
/// locations. Physical stock is written only after operator confirmation.
/// </summary>
public sealed record SubmitInwardCommand(
    IReadOnlyList<SubmitInwardLine> Lines, int WarehouseId,
    string? VehicleNumber = null, string? LrNumber = null, string? ReceivedFrom = null,
    string? UploadHeaderJson = null,
    IReadOnlyList<InwardUnsavedLineInput>? UnreadableRows = null) : IRequest<InwardResultDto>;

public sealed record SubmitInwardLine(long MaterialCode, decimal QuantityBoxes, string MfgMonthText, string? Description = null);

public sealed class SubmitInwardValidator : AbstractValidator<SubmitInwardCommand>
{
    public SubmitInwardValidator(IWarehouseAccessService warehouseAccess)
    {
        RuleFor(x => x.WarehouseId)
            .MustAsync((id, ct) => warehouseAccess.HasAccessAsync(id, ct))
            .WithMessage("Warehouse is not assigned to you.");
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MaterialCode).GreaterThan(0);
            line.RuleFor(l => l.QuantityBoxes).GreaterThan(0);
            line.RuleFor(l => l.QuantityBoxes).Must(decimal.IsInteger).WithMessage("Quantity must be a whole number (no decimals).");
            line.RuleFor(l => l.MfgMonthText)
                .Must(text => MfgMonthParser.TryParse(text, out _))
                .WithMessage("Mfg Month must look like 'MAR|2010'.");
        });
    }
}

public sealed class SubmitInwardHandler(
    IApplicationDbContext db,
    IPalletAllocationService allocator,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<SubmitInwardCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(SubmitInwardCommand request, CancellationToken cancellationToken)
    {
        // One GRN line per material + Mfg Month: sheet rows repeating the same material and PKM are added together
        // before allocating. Left separate, each row would be allocated on its own and the same pallet would be split
        // across rows (e.g. 38 + 16 on one 54-box location) instead of reading as a single 54.
        var requestLines = request.Lines
            .GroupBy(l => (l.MaterialCode, Pkm: MfgMonthParser.TryParse(l.MfgMonthText, out var key) ? key.ToString() : l.MfgMonthText.Trim().ToUpperInvariant()))
            .Select(g => new SubmitInwardLine(
                g.Key.MaterialCode,
                g.Sum(l => l.QuantityBoxes),
                g.First().MfgMonthText,
                g.Select(l => l.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))))
            .ToList();

        var materialCodes = requestLines.Select(l => l.MaterialCode).ToList();
        var materialsByCode = await db.Materials
            .Where(m => materialCodes.Contains(m.MaterialNumber))
            .ToDictionaryAsync(m => m.MaterialNumber, cancellationToken);

        var createdAt = clock.UtcNow;
        var transaction = new InwardTransaction
        {
            WarehouseId = request.WarehouseId,
            ReferenceNumber = await InwardSubmissionLogic.NextReferenceAsync(db, createdAt, cancellationToken),
            CreatedAt = createdAt,
            CreatedByUserId = currentUser.UserId,
            VehicleNumber = request.VehicleNumber,
            LrNumber = request.LrNumber,
            ReceivedFrom = request.ReceivedFrom,
            UploadHeaderJson = request.UploadHeaderJson
        };

        db.InwardTransactions.Add(transaction);
        var resultLines = new List<InwardLineResultDto>();

        // Allocate in a deterministic order instead of the upload/entry order: by material code,
        // then design type, then PKM (oldest first). Later lines see earlier lines' reservations,
        // so this keeps a material's lines (and materials of one design) together in the rack
        // no matter how the sheet happened to be sorted. OrderBy is stable, so identical lines
        // keep their entered order.
        var orderedLines = requestLines
            .OrderBy(l => l.MaterialCode)
            .ThenBy(l => materialsByCode.TryGetValue(l.MaterialCode, out var m) ? m.DesignType : string.Empty, StringComparer.Ordinal)
            .ThenBy(l => MfgMonthParser.TryParse(l.MfgMonthText, out var pkm) ? pkm : int.MaxValue)
            .ToList();

        foreach (var line in orderedLines)
        {
            if (!materialsByCode.TryGetValue(line.MaterialCode, out var material))
            {
                var mfgMonthLabel = MfgMonthParser.TryParse(line.MfgMonthText, out var unknownMaterialMfgMonth)
                    ? MfgMonthParser.Format(unknownMaterialMfgMonth)
                    : line.MfgMonthText;
                resultLines.Add(new InwardLineResultDto(
                    line.MaterialCode, InwardDownloadMapper.NameOrPlaceholder(line.Description), mfgMonthLabel, line.QuantityBoxes, 0,
                    Domain.Enums.AllocationStatus.Failed, "Material code not found in Material Master.", [], IsConfirmed: true));
                transaction.UnsavedLines.Add(new Domain.Entities.InwardUnsavedLine
                {
                    CodeText = line.MaterialCode.ToString(),
                    Description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim(),
                    QuantityBoxes = line.QuantityBoxes,
                    Kind = Domain.Entities.InwardUnsavedLineKind.UnknownMaterial,
                    Reason = "Material code not found in Material Master.",
                    MfgMonth = MfgMonthParser.TryParse(line.MfgMonthText, out var unsavedMfgMonth) ? unsavedMfgMonth : null
                });
                continue;
            }

            MfgMonthParser.TryParse(line.MfgMonthText, out var mfgMonth);

            var (transactionLine, reservedRows, allocation) =
                await InwardSubmissionLogic.AllocateAndReserveLineAsync(db, allocator, transaction, material, line.QuantityBoxes, mfgMonth, request.WarehouseId, cancellationToken);

            resultLines.Add(new InwardLineResultDto(
                material.MaterialNumber,
                material.Description,
                MfgMonthParser.Format(mfgMonth),
                line.QuantityBoxes,
                allocation.AllocatedQuantityBoxes,
                allocation.Status,
                allocation.Remarks,
                reservedRows
                    .OrderBy(r => r.LocationCode)
                    .Select(r => new InwardAllocationLineDto(
                        r.LocationCode,
                        r.Putaway.QuantityBoxes,
                        r.Reason,
                        IsConfirmed: r.Putaway.IsConfirmed,
                        PutawayId: r.Putaway.Id))
                    .ToList(),
                IsConfirmed: transactionLine.Putaways.Count == 0));
        }

        // Sheet rows that could not even be read as a material line still carry boxes: record them against the
        // GRN so the inbound volume is the whole upload and nothing disappears silently.
        foreach (var unreadable in request.UnreadableRows ?? [])
        {
            transaction.UnsavedLines.Add(new Domain.Entities.InwardUnsavedLine
            {
                CodeText = unreadable.CodeText,
                Description = string.IsNullOrWhiteSpace(unreadable.Description) ? null : unreadable.Description.Trim(),
                QuantityBoxes = unreadable.QuantityBoxes,
                Kind = Domain.Entities.InwardUnsavedLineKind.UnreadableRow,
                Reason = unreadable.Reason,
                SourceRow = unreadable.SourceRow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new InwardResultDto(
            transaction.Id, transaction.ReferenceNumber, transaction.CreatedAt, resultLines,
            IsConfirmed: resultLines.All(l => l.IsConfirmed),
            VehicleNumber: transaction.VehicleNumber,
            LrNumber: transaction.LrNumber,
            UploadHeaderJson: transaction.UploadHeaderJson,
            ReceivedFrom: transaction.ReceivedFrom,
            UnsavedLines: InwardUnsavedLineMapper.From(transaction));
    }
}
