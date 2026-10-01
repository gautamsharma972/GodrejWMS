using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Reports.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

/// <summary>Inventory Reports - "Valuation (at MRP)" tab: total stock value per material, computed
/// as QuantityBoxes x Material.PackSize x Material.MrpPrice - retail MRP, not a landed-cost field
/// (none exists on Material today), so this is deliberately labeled "at MRP". Access is enforced
/// here server-side as well as in the UI.</summary>
public sealed record GetInventoryValuationQuery(string? Search = null) : IRequest<IReadOnlyList<InventoryValuationRowDto>>;

public sealed class GetInventoryValuationHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetInventoryValuationQuery, IReadOnlyList<InventoryValuationRowDto>>
{
    public async Task<IReadOnlyList<InventoryValuationRowDto>> Handle(
        GetInventoryValuationQuery request, CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole("Admin") &&
            !currentUser.IsInRole("Supervisor") &&
            !currentUser.IsInRole("Operator"))
        {
            throw new UnauthorizedAccessException("Inventory valuation requires an authorized warehouse role.");
        }

        var query = db.StockBatches.AsNoTracking().Where(b => b.QuantityBoxes > 0);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(b =>
                b.Material.MaterialNumber.ToString().Contains(term) ||
                b.Material.Description.Contains(term));
        }

        var perBatch = await query
            .Select(b => new
            {
                b.MaterialId,
                b.Material.MaterialNumber,
                MaterialDescription = b.Material.Description,
                b.Material.DesignType,
                SeasonName = b.Material.Season.DisplayName,
                b.QuantityBoxes,
                Value = b.QuantityBoxes * b.Material.PackSize * b.Material.MrpPrice
            })
            .ToListAsync(cancellationToken);

        return perBatch
            .GroupBy(b => new { b.MaterialId, b.MaterialNumber, b.MaterialDescription, b.DesignType, b.SeasonName })
            .Select(g => new InventoryValuationRowDto(
                g.Key.MaterialId, g.Key.MaterialNumber, g.Key.MaterialDescription, g.Key.DesignType, g.Key.SeasonName,
                g.Sum(x => x.QuantityBoxes), g.Sum(x => x.Value)))
            .OrderByDescending(r => r.TotalValue)
            .ToList();
    }
}
