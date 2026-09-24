using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryMovement.Queries;

/// <summary>Movement Detail screen (§30).</summary>
public sealed record GetInventoryMovementDetailQuery(int Id) : IRequest<InventoryMovementDto>;

public sealed class GetInventoryMovementDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetInventoryMovementDetailQuery, InventoryMovementDto>
{
    public async Task<InventoryMovementDto> Handle(GetInventoryMovementDetailQuery request, CancellationToken cancellationToken)
    {
        var m = await db.StockMovements
            .AsNoTracking()
            .Include(x => x.Material)
            .Include(x => x.SourcePalletPosition)
            .Include(x => x.DestinationPalletPosition)
            .Include(x => x.Reason)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(StockMovement), request.Id);

        return new InventoryMovementDto(
            m.Id, m.MovementNumber, m.CreatedAt, m.Material.MaterialNumber, m.Material.Description,
            MfgMonthParser.Format(m.MfgMonth), m.SourcePalletPosition.LocationCode, m.DestinationPalletPosition.LocationCode,
            m.QuantityBoxes, m.Reason.Code, m.Reason.DisplayName, m.PerformedByUserName, m.Status.ToString());
    }
}
