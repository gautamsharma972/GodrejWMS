using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Racks.Dtos;
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
                TotalCapacityBoxes = r.PalletPositions.Sum(p => p.CapacityBoxes),
                OccupiedPositions = r.PalletPositions.Count(p => p.StockBatches.Sum(b => b.QuantityBoxes) > 0)
            })
            .ToListAsync(cancellationToken);

        return racks
            .Select(r => new RackDto(
                r.Id, r.Code, r.Name, r.Columns, r.Levels, r.ShelfLengthMm, r.ShelfWidthMm, r.ShelfHeightMm,
                r.IsActive, r.TotalPositions, r.TotalCapacityBoxes, r.OccupiedPositions,
                r.TotalPositions == 0 ? 0 : Math.Round(100m * r.OccupiedPositions / r.TotalPositions, 1)))
            .ToList();
    }
}
