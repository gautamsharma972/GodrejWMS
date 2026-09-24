using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using GodrejWMS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.StockMaster.Queries;

/// <summary>Dashboard KPIs: capacity utilization, active materials, stock breakdown, and live operation signals.</summary>
public sealed record GetStockSummaryQuery : IRequest<StockSummaryDto>;

public sealed class GetStockSummaryHandler(IApplicationDbContext db) : IRequestHandler<GetStockSummaryQuery, StockSummaryDto>
{
    public async Task<StockSummaryDto> Handle(GetStockSummaryQuery request, CancellationToken cancellationToken)
    {
        var today = new DateTimeOffset(DateTime.Today);
        var tomorrow = today.AddDays(1);

        var totalRacks = await db.Racks.CountAsync(r => r.IsActive, cancellationToken);
        var totalPositions = await db.PalletPositions.CountAsync(p => p.IsActive, cancellationToken);
        var totalCapacityBoxes = await db.PalletPositions.Where(p => p.IsActive).SumAsync(p => p.CapacityBoxes, cancellationToken);

        var occupiedPositions = await db.PalletPositions
            .Where(p => p.IsActive && p.StockBatches.Sum(b => b.QuantityBoxes) > 0)
            .CountAsync(cancellationToken);

        var totalStockBoxes = await db.StockBatches.SumAsync(b => (decimal?)b.QuantityBoxes, cancellationToken) ?? 0m;
        var pendingPutawayBoxes = await db.InwardPutaways
            .Where(p => !p.IsConfirmed)
            .SumAsync(p => (decimal?)p.QuantityBoxes, cancellationToken) ?? 0m;

        var totalActiveMaterials = await db.Materials.CountAsync(m => m.IsActive, cancellationToken);

        var pendingPutawayTransactions = await db.InwardTransactions
            .Where(t => t.Lines.SelectMany(l => l.Putaways).Any(p => !p.IsConfirmed))
            .CountAsync(cancellationToken);

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

        var todayInwardBoxes = await db.InwardTransactionLines
            .Where(l => l.InwardTransaction.CreatedAt >= today && l.InwardTransaction.CreatedAt < tomorrow)
            .SumAsync(l => (decimal?)l.RequestedQuantityBoxes, cancellationToken) ?? 0m;

        var todayConfirmedBoxes = await db.InwardPutaways
            .Where(p => p.ConfirmedAt >= today && p.ConfirmedAt < tomorrow)
            .SumAsync(p => (decimal?)p.QuantityBoxes, cancellationToken) ?? 0m;

        var todayPulloutBoxes = await db.PulloutTransactionLines
            .Where(l => l.PulloutTransaction.IsConfirmed)
            .Where(l => l.PulloutTransaction.CreatedAt >= today && l.PulloutTransaction.CreatedAt < tomorrow)
            .SumAsync(l => (decimal?)l.PickedQuantityBoxes, cancellationToken) ?? 0m;

        var fullLocationCount = await db.PalletPositions
            .Where(p => p.IsActive &&
                p.StockBatches.Sum(b => b.QuantityBoxes) +
                db.InwardPutaways.Where(r => !r.IsConfirmed && r.PalletPositionId == p.Id).Sum(r => r.QuantityBoxes) >= p.CapacityBoxes)
            .CountAsync(cancellationToken);

        var freeCapacityBoxes = Math.Max(0, totalCapacityBoxes - totalStockBoxes - pendingPutawayBoxes);

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
            pendingPutawayBoxes,
            reservedLocationCount,
            inwardExceptionLines + pulloutExceptionLines,
            todayInwardBoxes,
            todayConfirmedBoxes,
            todayPulloutBoxes,
            fullLocationCount,
            freeCapacityBoxes,
            bySeason);
    }
}
