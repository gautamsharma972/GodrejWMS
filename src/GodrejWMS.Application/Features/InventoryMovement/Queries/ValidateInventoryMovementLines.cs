using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using MediatR;

namespace GodrejWMS.Application.Features.InventoryMovement.Queries;

/// <summary>
/// Server-side validation before confirmation (§24), for one or more lines - the same request
/// shape both Move by Location (a single-element list) and Move by SKU (one element per selected
/// record) send. Nothing is persisted; each line is checked independently via
/// <see cref="IInventoryMovementService.ValidateAsync"/>, the exact same rules
/// <c>ConfirmInventoryMovementCommand</c> re-checks (and enforces) at commit time.
/// </summary>
public sealed record ValidateInventoryMovementLinesQuery(IReadOnlyList<InventoryMovementLineInput> Lines)
    : IRequest<IReadOnlyList<InventoryMovementValidationDto>>;

public sealed class ValidateInventoryMovementLinesHandler(IInventoryMovementService movementService)
    : IRequestHandler<ValidateInventoryMovementLinesQuery, IReadOnlyList<InventoryMovementValidationDto>>
{
    public async Task<IReadOnlyList<InventoryMovementValidationDto>> Handle(
        ValidateInventoryMovementLinesQuery request, CancellationToken cancellationToken)
    {
        var results = new List<InventoryMovementValidationDto>();

        foreach (var line in request.Lines)
        {
            var error = await movementService.ValidateAsync(
                new InventoryMovementLineRequest(line.MaterialId, line.MfgMonth, line.SourcePalletPositionId, line.DestinationPalletPositionId, line.QuantityBoxes),
                cancellationToken);

            results.Add(new InventoryMovementValidationDto(error is null, error));
        }

        return results;
    }
}
