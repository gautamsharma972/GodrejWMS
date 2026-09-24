using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inventory Reports - "Aging / Slow-Moving Stock" tab: current stock bucketed by age
/// (from manufacturing month to today), cross-referenced with Material.MovementTypeId (velocity)
/// so slow-movers sitting long stand out from fast-movers that are simply mid-cycle.</summary>
public sealed record GetInventoryAgingQuery : IRequest<IReadOnlyList<InventoryAgingRowDto>>;

public sealed class GetInventoryAgingHandler(IApplicationDbContext db, IDateTimeProvider clock)
    : IRequestHandler<GetInventoryAgingQuery, IReadOnlyList<InventoryAgingRowDto>>
{
    public async Task<IReadOnlyList<InventoryAgingRowDto>> Handle(GetInventoryAgingQuery request, CancellationToken cancellationToken)
    {
        var batches = await db.StockBatches
            .AsNoTracking()
            .Where(b => b.QuantityBoxes > 0)
            .Select(b => new { b.MfgMonth, b.QuantityBoxes, MovementTypeName = b.Material.MovementType.DisplayName })
            .ToListAsync(cancellationToken);

        var today = clock.Today;

        return batches
            .Select(b => new
            {
                AgeDays = today.DayNumber - MfgMonthParser.ToDateOnly(b.MfgMonth).DayNumber,
                b.MovementTypeName,
                b.QuantityBoxes
            })
            .Select(b => new { Bucket = BucketFor(b.AgeDays), b.MovementTypeName, b.QuantityBoxes })
            .GroupBy(b => new { b.Bucket, b.MovementTypeName })
            .Select(g => new InventoryAgingRowDto(g.Key.Bucket, g.Key.MovementTypeName, g.Count(), g.Sum(x => x.QuantityBoxes)))
            .OrderBy(r => BucketSortOrder(r.AgeBucket))
            .ThenByDescending(r => r.TotalBoxes)
            .ToList();
    }

    private static string BucketFor(int ageDays) => ageDays switch
    {
        <= 30 => "0-30 days",
        <= 60 => "31-60 days",
        <= 90 => "61-90 days",
        _ => "90+ days"
    };

    private static int BucketSortOrder(string bucket) => bucket switch
    {
        "0-30 days" => 0,
        "31-60 days" => 1,
        "61-90 days" => 2,
        _ => 3
    };
}
