using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.InventoryMovement.Queries;

/// <summary>Move by SKU's inventory listing: every stock cell for one material, across every
/// location it currently sits in (§11 of the Inventory Movement spec).</summary>
public sealed record GetStockByMaterialQuery(int MaterialId) : IRequest<IReadOnlyList<MaterialStockLineDto>>;

public sealed class GetStockByMaterialHandler(IApplicationDbContext db)
    : IRequestHandler<GetStockByMaterialQuery, IReadOnlyList<MaterialStockLineDto>>
{
    public async Task<IReadOnlyList<MaterialStockLineDto>> Handle(GetStockByMaterialQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.MaterialId == request.MaterialId && b.QuantityBoxes > 0)
            .OrderBy(b => b.PalletPosition.LocationCode)
            .ThenBy(b => b.MfgMonth)
            .Select(b => new
            {
                b.MaterialId,
                b.Material.MaterialNumber,
                MaterialDescription = b.Material.Description,
                b.Material.DesignType,
                b.MfgMonth,
                b.PalletPositionId,
                b.PalletPosition.LocationCode,
                StockSubtypeName = b.StockSubtype.DisplayName,
                b.QuantityBoxes
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(b => new MaterialStockLineDto(
                b.MaterialId, b.MaterialNumber, b.MaterialDescription, b.DesignType,
                b.MfgMonth, MfgMonthParser.Format(b.MfgMonth),
                b.PalletPositionId, b.LocationCode, b.StockSubtypeName, b.QuantityBoxes))
            .ToList();
    }
}
