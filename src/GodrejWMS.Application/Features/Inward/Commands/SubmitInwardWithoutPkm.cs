using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>Submits one or more inward (goods-receipt) lines for the Pickers' "Inward without
/// PKM" flow - no manufacturing month is ever required here, unlike <see cref="SubmitInwardCommand"/>.
/// Deliberately a separate command/handler, not a parameter on the existing one, so nothing about
/// today's Inward entry (manual, single-sheet, or multi-sheet upload) changes. Every line is routed
/// straight to an empty pallet position by <see cref="IPalletAllocationService"/> (no same-SKU
/// top-up or same-rack consolidation is possible without a PKM to match against), using the exact
/// same zone/velocity/season/AOP-preference scoring every other inward uses. The resulting GRN is
/// flagged <see cref="InwardTransaction.IsPkmless"/> so it's recognizable wherever a non-Picker
/// reviews pending GRNs, and cannot be confirmed into real stock until a real PKM is assigned (see
/// <see cref="InwardPutawayConfirmationLogic"/>) - that assignment flow is a later piece of work.</summary>
public sealed record SubmitInwardWithoutPkmCommand(
    IReadOnlyList<SubmitInwardWithoutPkmLine> Lines, int WarehouseId,
    string? VehicleNumber = null, string? LrNumber = null, string? ReceivedFrom = null) : IRequest<InwardResultDto>;

public sealed record SubmitInwardWithoutPkmLine(long MaterialCode, decimal QuantityBoxes);

public sealed class SubmitInwardWithoutPkmValidator : AbstractValidator<SubmitInwardWithoutPkmCommand>
{
    public SubmitInwardWithoutPkmValidator(IWarehouseAccessService warehouseAccess)
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
        });
    }
}

public sealed class SubmitInwardWithoutPkmHandler(
    IApplicationDbContext db,
    IPalletAllocationService allocator,
    IDateTimeProvider clock,
    ICurrentUserService currentUser) : IRequestHandler<SubmitInwardWithoutPkmCommand, InwardResultDto>
{
    public async Task<InwardResultDto> Handle(SubmitInwardWithoutPkmCommand request, CancellationToken cancellationToken)
    {
        var materialCodes = request.Lines.Select(l => l.MaterialCode).ToList();
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
            IsPkmless = true
        };

        db.InwardTransactions.Add(transaction);
        var resultLines = new List<InwardLineResultDto>();

        // Same deterministic ordering rationale as SubmitInwardHandler - by material code, then
        // design type, so a material's lines (and materials of one design) land together rather
        // than scattering by entry order. No PKM to order by here.
        var orderedLines = request.Lines
            .OrderBy(l => l.MaterialCode)
            .ThenBy(l => materialsByCode.TryGetValue(l.MaterialCode, out var m) ? m.DesignType : string.Empty, StringComparer.Ordinal)
            .ToList();

        foreach (var line in orderedLines)
        {
            if (!materialsByCode.TryGetValue(line.MaterialCode, out var material))
            {
                resultLines.Add(new InwardLineResultDto(
                    line.MaterialCode, "(unknown material)", "No PKM", line.QuantityBoxes, 0,
                    Domain.Enums.AllocationStatus.Failed, "Material code not found in Material Master.", [], IsConfirmed: true));
                continue;
            }

            var (transactionLine, reservedRows, allocation) =
                await InwardSubmissionLogic.AllocateAndReserveLineAsync(db, allocator, transaction, material, line.QuantityBoxes, mfgMonth: null, request.WarehouseId, cancellationToken);

            resultLines.Add(new InwardLineResultDto(
                material.MaterialNumber,
                material.Description,
                "No PKM",
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

        await db.SaveChangesAsync(cancellationToken);

        return new InwardResultDto(transaction.Id, transaction.ReferenceNumber, transaction.CreatedAt, resultLines, IsConfirmed: resultLines.All(l => l.IsConfirmed));
    }
}
