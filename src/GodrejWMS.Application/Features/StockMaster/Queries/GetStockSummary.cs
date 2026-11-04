using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GodrejWMS.Application.Features.StockMaster.Queries;

/// <summary>Dashboard KPIs: capacity utilization, active materials, stock breakdown, and live operation signals.</summary>
public sealed record GetStockSummaryQuery : IRequest<StockSummaryDto>;

/// <summary>Bound from the "Capacity" config section.</summary>
public sealed class StockCapacityOptions
{
    /// <summary>Boxes assumed for one pallet of an empty location (no stock, no pending put-away), whose material
    /// - and so its real pallet size - is not known yet.</summary>
    public int DefaultPalletCapacityBoxes { get; set; } = 40;
}

public sealed class GetStockSummaryHandler(IApplicationDbContext db, IOptions<StockCapacityOptions>? capacityOptions = null)
    : IRequestHandler<GetStockSummaryQuery, StockSummaryDto>
{
    public async Task<StockSummaryDto> Handle(GetStockSummaryQuery request, CancellationToken cancellationToken)
    {
        var today = new DateTimeOffset(DateTime.Today);
        var tomorrow = today.AddDays(1);

        var totalRacks = await db.Racks.CountAsync(r => r.IsActive, cancellationToken);
        var totalPositions = await db.PalletPositions.CountAsync(p => p.IsActive, cancellationToken);

        // Capacity of a position is driven by whichever material occupies (or is reserved against, via
        // a pending put-away) it - a position only ever holds one material at a time. A position with
        // neither has no material context, so it is counted at the default pallet size instead
        // (MaxPallets x DefaultPalletCapacityBoxes) - otherwise empty racks would look like zero room.
        var positionCapacities = await db.PalletPositions
            .Where(p => p.IsActive)
            .Select(p => new
            {
                p.MaxPallets,
                Occupied = p.StockBatches.Where(b => b.Material.IsActive).Sum(b => (decimal?)b.QuantityBoxes) ?? 0m,
                Reserved = db.InwardPutaways.Where(r => !r.IsConfirmed && r.PalletPositionId == p.Id).Sum(r => (decimal?)r.QuantityBoxes) ?? 0m,
                PalletCapacityBoxes = p.StockBatches.Where(b => b.Material.IsActive).Where(b => b.QuantityBoxes > 0).Select(b => (int?)b.Material.PalletCapacityBoxes).FirstOrDefault()
                    ?? db.InwardPutaways.Where(r => !r.IsConfirmed && r.PalletPositionId == p.Id)
                        .Select(r => (int?)r.InwardTransactionLine.Material.PalletCapacityBoxes).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var defaultPalletCapacity = Math.Max(0, capacityOptions?.Value.DefaultPalletCapacityBoxes ?? new StockCapacityOptions().DefaultPalletCapacityBoxes);
        var emptyLocations = positionCapacities.Where(p => p.PalletCapacityBoxes is null).ToList();
        var emptyCapacityBoxes = emptyLocations.Sum(p => p.MaxPallets * defaultPalletCapacity);
        var totalCapacityBoxes = (int)positionCapacities.Sum(p => p.MaxPallets * (p.PalletCapacityBoxes ?? 0)) + emptyCapacityBoxes;
        var fullLocationCount = positionCapacities.Count(p =>
            p.PalletCapacityBoxes is { } cap && p.Occupied + p.Reserved >= p.MaxPallets * cap);

        var occupiedPositions = await db.PalletPositions
            .Where(p => p.IsActive && p.StockBatches.Where(b => b.Material.IsActive).Sum(b => b.QuantityBoxes) > 0)
            .CountAsync(cancellationToken);

        var totalStockBoxes = await db.StockBatches.SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
        var pendingPutawayBoxes = await db.InwardPutaways
            .Where(p => !p.IsConfirmed)
            .SumAsync(p => (decimal?)p.QuantityBoxes, cancellationToken) ?? 0m;

        var totalActiveMaterials = await db.Materials.CountAsync(m => m.IsActive, cancellationToken);

        var pendingPutawayTransactions = await db.InwardTransactions
            .Where(t => t.Lines.SelectMany(l => l.Putaways).Any(p => !p.IsConfirmed))
            .CountAsync(cancellationToken);

        var pendingPulloutTransactions = await db.PulloutTransactions
            .CountAsync(t => !t.IsConfirmed && !t.IsRejected, cancellationToken);

        var reservedLocationCount = await db.InwardPutaways
            .Where(p => !p.IsConfirmed)
            .Select(p => p.PalletPositionId)
            .Distinct()
            .CountAsync(cancellationToken);

        var inwardExceptionLines = await db.InwardTransactionLines
            .CountAsync(l => l.Status != AllocationStatus.Fulfilled, cancellationToken);

        var pulloutExceptionLines = await db.PulloutTransactionLines
            .Where(l => l.PulloutTransaction.IsConfirmed)
            .CountAsync(l => l.Status != AllocationStatus.Fulfilled, cancellationToken);

        var todayInwardTransactions = await db.InwardTransactions
            .CountAsync(t => !t.IsRejected && t.CreatedAt >= today && t.CreatedAt < tomorrow, cancellationToken);

        var todayInwardBoxes = await db.InwardTransactionLines
            .Where(l => !l.InwardTransaction.IsRejected)
            .Where(l => l.InwardTransaction.CreatedAt >= today && l.InwardTransaction.CreatedAt < tomorrow)
            .SumAsync(l => (decimal?)l.RequestedQuantityBoxes, cancellationToken) ?? 0m;

        var todayConfirmedBoxes = await db.InwardPutaways
            .Where(p => p.ConfirmedAt >= today && p.ConfirmedAt < tomorrow)
            .SumAsync(p => (decimal?)p.QuantityBoxes, cancellationToken) ?? 0m;

        var todayPulloutTransactions = await db.PulloutTransactions
            .CountAsync(t => !t.IsRejected && t.CreatedAt >= today && t.CreatedAt < tomorrow, cancellationToken);

        var todayPickedBoxes = await db.PulloutTransactionLines
            .Where(l => !l.PulloutTransaction.IsRejected)
            .Where(l => l.PulloutTransaction.CreatedAt >= today && l.PulloutTransaction.CreatedAt < tomorrow)
            .SumAsync(l => (decimal?)l.PickedQuantityBoxes, cancellationToken) ?? 0m;

        var todayPulloutBoxes = await db.PulloutTransactionLines
            .Where(l => l.PulloutTransaction.IsConfirmed && !l.PulloutTransaction.IsRejected)
            .Where(l => l.PulloutTransaction.CreatedAt >= today && l.PulloutTransaction.CreatedAt < tomorrow)
            .SumAsync(l => (decimal?)l.PickedQuantityBoxes, cancellationToken) ?? 0m;

        var freeCapacityBoxes = Math.Max(0, totalCapacityBoxes - totalStockBoxes - pendingPutawayBoxes);

        var lowStockLocationCount = await db.StockBatches
            .Where(b => b.QuantityBoxes > 0
                && b.Material.PalletCapacityBoxes > 0
                && b.QuantityBoxes <= b.Material.PalletCapacityBoxes * 0.10m)
            .Select(b => b.PalletPositionId)
            .Distinct()
            .CountAsync(cancellationToken);

        var bySeason = await db.StockBatches
            .Where(b => b.QuantityBoxes > 0)
            .GroupBy(b => new { b.Material.SeasonId, Code = b.Material.Season.Code, Name = b.Material.Season.DisplayName })
            .Select(g => new SeasonBreakdownDto(
                g.Key.SeasonId,
                g.Key.Code,
                g.Key.Name,
                g.Sum(x => x.QuantityBoxes),
                g.Select(x => x.MaterialId).Distinct().Count()))
            .ToListAsync(cancellationToken);

        return new StockSummaryDto(
            totalRacks,
            totalPositions,
            occupiedPositions,
            totalPositions == 0 ? 0 : Math.Round(100m * occupiedPositions / totalPositions, 1),
            totalCapacityBoxes,
            totalStockBoxes,
            totalActiveMaterials,
            pendingPutawayTransactions,
            pendingPulloutTransactions,
            pendingPutawayBoxes,
            reservedLocationCount,
            inwardExceptionLines + pulloutExceptionLines,
            todayInwardTransactions,
            todayInwardBoxes,
            todayConfirmedBoxes,
            todayPulloutTransactions,
            todayPickedBoxes,
            todayPulloutBoxes,
            fullLocationCount,
            freeCapacityBoxes,
            lowStockLocationCount,
            bySeason,
            emptyLocations.Count,
            emptyCapacityBoxes,
            defaultPalletCapacity);
    }
}
