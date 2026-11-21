using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.CycleCount.Queries;

/// <summary>Distinct locations and materials of the count cards (of the batches the caller can see), optionally narrowed to a warehouse, batch or rack.</summary>
[RequiresPermission(PermissionKeys.CycleCount.Review, PermissionKeys.CycleCount.Reports, PermissionKeys.CycleCount.Perform)]
public sealed record GetCycleCountFilterOptionsQuery(int? WarehouseId = null, int? BatchId = null, string? RackCode = null) : IRequest<CycleCountFilterOptionsDto>;

public sealed class GetCycleCountFilterOptionsHandler(IApplicationDbContext db, IWarehouseAccessService access)
    : IRequestHandler<GetCycleCountFilterOptionsQuery, CycleCountFilterOptionsDto>
{
    private const int Max = 5000;

    public async Task<CycleCountFilterOptionsDto> Handle(GetCycleCountFilterOptionsQuery request, CancellationToken cancellationToken)
    {
        if (request.WarehouseId is { } w && !await access.HasAccessAsync(w, cancellationToken))
        {
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        }

        var cards = db.CycleCountCards.AsNoTracking().Where(c => !c.IsExcluded);
        if (request.WarehouseId is { } wid) cards = cards.Where(c => c.CycleCountBatch.WarehouseId == wid);
        if (request.BatchId is { } bid) cards = cards.Where(c => c.CycleCountBatchId == bid);
        if (!string.IsNullOrWhiteSpace(request.RackCode)) { var rack = request.RackCode.Trim().ToUpper(); cards = cards.Where(c => c.RackCode.ToUpper() == rack); }

        var locations = await cards.Select(c => new { c.PalletPosition.LocationCode, c.RackCode }).Distinct().Take(Max).ToListAsync(cancellationToken);
        var system = await cards.Where(c => c.SystemMaterialId != null).Select(c => new { c.SystemMaterial!.MaterialNumber, c.SystemMaterial.Description }).Distinct().Take(Max).ToListAsync(cancellationToken);
        var physical = await cards.Where(c => c.PhysicalMaterialId != null).Select(c => new { c.PhysicalMaterial!.MaterialNumber, c.PhysicalMaterial.Description }).Distinct().Take(Max).ToListAsync(cancellationToken);

        return new CycleCountFilterOptionsDto(
            locations.OrderBy(l => l.LocationCode, StringComparer.Ordinal).Select(l => new CycleCountLocationOptionDto(l.LocationCode, l.RackCode)).ToList(),
            system.Concat(physical).GroupBy(m => m.MaterialNumber).Select(g => g.First()).OrderBy(m => m.MaterialNumber)
                .Select(m => new CycleCountMaterialOptionDto(m.MaterialNumber, m.Description)).ToList());
    }
}
