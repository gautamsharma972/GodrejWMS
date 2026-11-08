using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Pullout.Queries;

public sealed record RelocationSuggestionDto(
    int PalletPositionId,
    string LocationCode,
    int Level,
    bool IsVirtual,
    decimal SpareBoxes,
    string Reason);

/// <summary>Where the boxes left at a pick location could go: locations of the warehouse that can take them under the pallet-size
/// rules - ones already holding the same SKU first (consolidation), then empty ground-level locations, then empty locations of
/// the lowest levels. Mixed locations of other materials are not suggested.</summary>
public sealed record GetRelocationSuggestionsQuery(int PickId, decimal QuantityBoxes, int Take = 8)
    : IRequest<IReadOnlyList<RelocationSuggestionDto>>;

public sealed class GetRelocationSuggestionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetRelocationSuggestionsQuery, IReadOnlyList<RelocationSuggestionDto>>
{
    public async Task<IReadOnlyList<RelocationSuggestionDto>> Handle(GetRelocationSuggestionsQuery request, CancellationToken cancellationToken)
    {
        var pick = await db.PulloutPicks.AsNoTracking()
            .Include(p => p.PulloutTransactionLine).ThenInclude(l => l.Material)
            .Include(p => p.PulloutTransactionLine).ThenInclude(l => l.PulloutTransaction)
            .FirstOrDefaultAsync(p => p.Id == request.PickId, cancellationToken)
            ?? throw new NotFoundException(nameof(PulloutPick), request.PickId);
        var materialId = pick.PulloutTransactionLine.MaterialId;
        var palletSize = pick.PulloutTransactionLine.Material.PalletCapacityBoxes;
        var warehouseId = pick.PulloutTransactionLine.PulloutTransaction.WarehouseId;
        var quantity = Math.Max(0m, request.QuantityBoxes);

        var positions = await db.PalletPositions.AsNoTracking()
            .Where(p => p.IsActive && p.Id != pick.PalletPositionId
                && p.LocationSubtypeId == LocationSubtypeIds.Good && p.Rack.WarehouseId == warehouseId)
            .Select(p => new
            {
                p.Id,
                p.LocationCode,
                p.Level,
                p.Column,
                RackCode = p.Rack.Code,
                p.IsVirtual,
                p.VirtualStatus,
                p.MaxPallets,
                Stock = p.StockBatches.Where(b => b.QuantityBoxes > 0 && b.Material.IsActive)
                    .Select(b => new { b.MaterialId, b.QuantityBoxes, b.Material.PalletCapacityBoxes }).ToList(),
                Reserved = db.InwardPutaways
                    .Where(r => r.PalletPositionId == p.Id && !r.IsConfirmed && r.ExcludedLocation == null
                        && !r.InwardTransactionLine.InwardTransaction.IsRejected)
                    .Sum(r => (decimal?)r.QuantityBoxes) ?? 0m
            })
            .ToListAsync(cancellationToken);

        var suggestions = new List<(int Rank, RelocationSuggestionDto Dto, int Level, string Rack, int Column)>();
        foreach (var p in positions)
        {
            // A free virtual location is not a bin to hand out here; an in-use one only helps when it holds this very SKU.
            var sameMaterial = p.Stock.Any(s => s.MaterialId == materialId);
            var empty = p.Stock.Count == 0 && p.Reserved == 0;
            if (p.IsVirtual && !sameMaterial)
            {
                continue;
            }

            if (!sameMaterial && !empty)
            {
                continue; // holds another material: not offered as a default
            }

            var capacity = PalletCapacityCalculator.EffectiveCapacityBoxes(
                p.MaxPallets, p.Stock.Select(s => s.PalletCapacityBoxes).Append(palletSize));
            var spare = capacity - p.Stock.Sum(s => s.QuantityBoxes) - p.Reserved;
            if (spare < quantity || spare <= 0)
            {
                continue;
            }

            var (rank, reason) = sameMaterial
                ? (0, $"Already holds this SKU - {spare:0.###} box(es) free")
                : p.Level == 1
                    ? (1, "Empty ground-level location")
                    : (2, $"Empty location (level {p.Level})");
            suggestions.Add((rank, new RelocationSuggestionDto(p.Id, p.LocationCode, p.Level, p.IsVirtual, spare, reason), p.Level, p.RackCode, p.Column));
        }

        return suggestions
            .OrderBy(s => s.Rank).ThenBy(s => s.Level).ThenBy(s => s.Rack, StringComparer.Ordinal).ThenBy(s => s.Column)
            .Take(Math.Max(1, request.Take))
            .Select(s => s.Dto)
            .ToList();
    }
}
