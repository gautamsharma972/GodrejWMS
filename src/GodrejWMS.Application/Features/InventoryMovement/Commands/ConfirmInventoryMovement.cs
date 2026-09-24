using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryMovement.Commands;

/// <summary>
/// Confirms one or more Inventory Movement lines - the single shared backend both Move by
/// Location (a single-element list) and Move by SKU (one element per selected record) call (§1).
/// Each line is validated and executed independently (TEST14: "Each movement line is
/// independently validated and processed") via <see cref="IInventoryMovementService"/>, so one
/// line failing (e.g. a concurrent capacity change) never blocks the others in the same batch.
/// </summary>
public sealed record ConfirmInventoryMovementCommand(
    IReadOnlyList<InventoryMovementLineInput> Lines,
    int ReasonId) : IRequest<IReadOnlyList<InventoryMovementLineResultDto>>;

public sealed class ConfirmInventoryMovementValidator : AbstractValidator<ConfirmInventoryMovementCommand>
{
    public ConfirmInventoryMovementValidator(IApplicationDbContext db)
    {
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.MaterialId).GreaterThan(0);
            line.RuleFor(l => l.SourcePalletPositionId).GreaterThan(0);
            line.RuleFor(l => l.DestinationPalletPositionId).GreaterThan(0);
            line.RuleFor(l => l.QuantityBoxes).GreaterThan(0);
        });
        RuleFor(x => x.ReasonId)
            .GreaterThan(0)
            .WithMessage("A movement reason is required.")
            .MustAsync(async (id, ct) => await db.MovementReasons.AnyAsync(r => r.Id == id && r.IsActive, ct))
            .WithMessage("Select a valid movement reason.");
    }
}

public sealed class ConfirmInventoryMovementHandler(IApplicationDbContext db, IInventoryMovementService movementService)
    : IRequestHandler<ConfirmInventoryMovementCommand, IReadOnlyList<InventoryMovementLineResultDto>>
{
    public async Task<IReadOnlyList<InventoryMovementLineResultDto>> Handle(
        ConfirmInventoryMovementCommand request, CancellationToken cancellationToken)
    {
        var materialIds = request.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materialNumbersById = await db.Materials
            .AsNoTracking()
            .Where(m => materialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.MaterialNumber, cancellationToken);

        var positionIds = request.Lines
            .SelectMany(l => new[] { l.SourcePalletPositionId, l.DestinationPalletPositionId })
            .Distinct()
            .ToList();
        var locationCodesById = await db.PalletPositions
            .AsNoTracking()
            .Where(p => positionIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.LocationCode, cancellationToken);

        var results = new List<InventoryMovementLineResultDto>();

        foreach (var line in request.Lines)
        {
            var materialNumber = materialNumbersById.GetValueOrDefault(line.MaterialId);
            var sourceCode = locationCodesById.GetValueOrDefault(line.SourcePalletPositionId, "?");
            var destinationCode = locationCodesById.GetValueOrDefault(line.DestinationPalletPositionId, "?");
            var mfgMonthLabel = MfgMonthParser.Format(line.MfgMonth);

            var outcome = await movementService.ExecuteAsync(
                new InventoryMovementLineRequest(line.MaterialId, line.MfgMonth, line.SourcePalletPositionId, line.DestinationPalletPositionId, line.QuantityBoxes),
                request.ReasonId,
                cancellationToken);

            results.Add(new InventoryMovementLineResultDto(
                outcome.Success,
                outcome.ErrorMessage,
                outcome.Movement?.MovementNumber,
                materialNumber,
                mfgMonthLabel,
                sourceCode,
                destinationCode,
                line.QuantityBoxes));
        }

        return results;
    }
}
