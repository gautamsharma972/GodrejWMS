using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Queries;

public sealed record GetRacksQuery(int? WarehouseId = null) : IRequest<IReadOnlyList<RackDto>>;

public sealed class GetRacksHandler(IApplicationDbContext db) : IRequestHandler<GetRacksQuery, IReadOnlyList<RackDto>>
{
    public async Task<IReadOnlyList<RackDto>> Handle(GetRacksQuery request, CancellationToken cancellationToken)
    {
        // Two flat queries instead of one correlated subquery per pallet position (the previous
        // shape: for every position, a nested filtered/ordered lookup into StockBatches joined to
        // Material) - that pattern is what made this page slow to load as rack/position counts
        // grew, since the database had to evaluate one correlated subquery per position with no
        // index led by PalletPositionId to seek on. A single GROUP BY over StockBatches (which can
        // use such an index) gives the same per-position occupancy/capacity data in one pass.
        var racks = await db.Racks
            .AsNoTracking()
            .Where(r => request.WarehouseId == null || r.WarehouseId == request.WarehouseId)
            .OrderBy(r => r.Code)
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Columns,
                r.FromColumn,
                r.Levels,
                r.ShelfLengthMm,
                r.ShelfWidthMm,
                r.ShelfHeightMm,
                r.IsActive,
                Positions = r.PalletPositions.Select(p => new { p.Id, p.MaxPallets }).ToList()
            })
            .ToListAsync(cancellationToken);

        var positionIds = racks.SelectMany(r => r.Positions).Select(p => p.Id).ToList();

        // Capacity is driven entirely by whichever material occupies a position (a position only
        // ever holds one material at a time) - an empty position has no material context, so it
        // contributes nothing to the rack's total capacity.
        var stockByPosition = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.QuantityBoxes > 0 && positionIds.Contains(b.PalletPositionId))
            .GroupBy(b => b.PalletPositionId)
            .Select(g => new { PalletPositionId = g.Key, PalletCapacityBoxes = g.Max(b => b.Material.PalletCapacityBoxes) })
            .ToDictionaryAsync(x => x.PalletPositionId, x => x.PalletCapacityBoxes, cancellationToken);

        return racks
            .Select(r =>
            {
                var totalPositions = r.Positions.Count;
                var occupiedPositions = r.Positions.Count(p => stockByPosition.ContainsKey(p.Id));
                var totalCapacityBoxes = (int)r.Positions.Sum(p =>
                    stockByPosition.TryGetValue(p.Id, out var cap) ? PalletCapacityCalculator.EffectiveCapacityBoxes(p.MaxPallets, cap) : 0);

                return new RackDto(
                    r.Id, r.Code, r.Name, r.Columns, r.FromColumn, r.FromColumn + r.Columns - 1, r.Levels,
                    r.ShelfLengthMm, r.ShelfWidthMm, r.ShelfHeightMm,
                    r.IsActive, totalPositions, totalCapacityBoxes, occupiedPositions,
                    totalPositions == 0 ? 0 : Math.Round(100m * occupiedPositions / totalPositions, 1));
            })
            .ToList();
    }
}
