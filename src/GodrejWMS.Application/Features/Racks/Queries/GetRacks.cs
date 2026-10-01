using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Racks.Queries;

public sealed record GetRacksQuery : IRequest<IReadOnlyList<RackDto>>;

public sealed class GetRacksHandler(IApplicationDbContext db) : IRequestHandler<GetRacksQuery, IReadOnlyList<RackDto>>
{
    public async Task<IReadOnlyList<RackDto>> Handle(GetRacksQuery request, CancellationToken cancellationToken)
    {
        var racks = await db.Racks
            .AsNoTracking()
            .OrderBy(r => r.Code)
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Columns,
                r.Levels,
                r.ShelfLengthMm,
                r.ShelfWidthMm,
                r.ShelfHeightMm,
                r.IsActive,
                TotalPositions = r.PalletPositions.Count,
                OccupiedPositions = r.PalletPositions.Count(p => p.StockBatches.Sum(b => b.QuantityBoxes) > 0),
                // Capacity is driven entirely by whichever material occupies a position (a
                // position only ever holds one material at a time) - an empty position has no
                // material context, so it contributes nothing to the rack's total capacity.
                Positions = r.PalletPositions.Select(p => new
                {
                    p.MaxPallets,
                    PalletCapacityBoxes = p.StockBatches
                        .Where(b => b.QuantityBoxes > 0)
                        .Select(b => (int?)b.Material.PalletCapacityBoxes)
                        .FirstOrDefault()
                })
            })
            .ToListAsync(cancellationToken);

        return racks
            .Select(r => new RackDto(
                r.Id, r.Code, r.Name, r.Columns, r.Levels, r.ShelfLengthMm, r.ShelfWidthMm, r.ShelfHeightMm,
                r.IsActive, r.TotalPositions,
                (int)r.Positions.Sum(p => p.PalletCapacityBoxes is { } cap ? PalletCapacityCalculator.EffectiveCapacityBoxes(p.MaxPallets, cap) : 0),
                r.OccupiedPositions,
                r.TotalPositions == 0 ? 0 : Math.Round(100m * r.OccupiedPositions / r.TotalPositions, 1)))
            .ToList();
    }
}
