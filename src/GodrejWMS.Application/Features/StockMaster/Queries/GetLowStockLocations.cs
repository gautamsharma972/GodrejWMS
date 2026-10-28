using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster.Queries;

public sealed record GetLowStockLocationsQuery : IRequest<IReadOnlyList<LowStockLocationDto>>;

public sealed class GetLowStockLocationsHandler(IApplicationDbContext db)
    : IRequestHandler<GetLowStockLocationsQuery, IReadOnlyList<LowStockLocationDto>>
{
    public async Task<IReadOnlyList<LowStockLocationDto>> Handle(
        GetLowStockLocationsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.QuantityBoxes > 0
                && b.Material.PalletCapacityBoxes > 0
                && b.QuantityBoxes <= b.Material.PalletCapacityBoxes * 0.10m)
            .OrderBy(b => b.PalletPosition.LocationCode)
            .ThenBy(b => b.Material.MaterialNumber)
            .ThenBy(b => b.MfgMonth)
            .Select(b => new
            {
                b.MaterialId,
                b.Material.MaterialNumber,
                MaterialDescription = b.Material.Description,
                b.MfgMonth,
                b.PalletPositionId,
                b.PalletPosition.LocationCode,
                b.QuantityBoxes,
                b.Material.MrpPrice,
                b.Material.PalletCapacityBoxes
            })
            .ToListAsync(cancellationToken);

        return rows.Select(b => new LowStockLocationDto(
            b.MaterialId, b.MaterialNumber, b.MaterialDescription,
            b.MfgMonth, MfgMonthParser.Format(b.MfgMonth),
            b.PalletPositionId, b.LocationCode, b.QuantityBoxes, b.MrpPrice,
            b.PalletCapacityBoxes, b.PalletCapacityBoxes * 0.10m)).ToList();
    }
}
