using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Queries;

public static class ReconciliationChecks
{
    public const string LedgerBalance = "Inventory vs ledger";
    public const string NegativeStock = "Negative stock";
    public const string OverAllocated = "GRN over-allocated";
    public const string AllocationTotal = "GRN allocation total";
    public const string MissingStock = "Put-away without stock";
    public const string ClosedReservation = "Reservation on rejected GRN";
    public const string MixedLocation = "Location holds several materials";

    public static readonly IReadOnlyList<string> All =
        [LedgerBalance, NegativeStock, OverAllocated, AllocationTotal, MissingStock, ClosedReservation, MixedLocation];
}

public sealed record ReconciliationIssueDto(
    string Check,
    string Severity,
    string Reference,
    string Location,
    long? MaterialNumber,
    string? Pkm,
    decimal Expected,
    decimal Actual,
    string Detail)
{
    public decimal Difference => Actual - Expected;
}

public sealed record StockReconciliationReportDto(
    IReadOnlyList<ReconciliationIssueDto> Issues,
    IReadOnlyDictionary<string, int> CountsByCheck,
    int BatchesChecked,
    int GrnLinesChecked);

/// <summary>Reconciliation check: compares Current Inventory (StockBatches) with the stock ledger and with what posted GRNs say should
/// be on the shelf, and lists every disagreement. Read-only - it never changes stock; the Detail column says what to do. Checks:
/// (1) inventory vs the last ledger balance of the same material/PKM/location, (2) negative stock, (3) a GRN line with more boxes put away
/// than received, (4) a GRN line whose put-away rows do not add up to its allocated total, (5) an unplaced put-away of a posted GRN whose
/// cell holds fewer boxes than the put-aways expect, (6) an open reservation left on a rejected GRN, (7) a location holding more than
/// one material.</summary>
public sealed record GetStockReconciliationReportQuery(int? WarehouseId = null) : IRequest<StockReconciliationReportDto>;

public sealed class GetStockReconciliationReportHandler(IApplicationDbContext db, IPermissionService permissions, IWarehouseAccessService warehouseAccess)
    : IRequestHandler<GetStockReconciliationReportQuery, StockReconciliationReportDto>
{
    public async Task<StockReconciliationReportDto> Handle(GetStockReconciliationReportQuery request, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(PermissionKeys.Reports.View, cancellationToken))
            throw new UnauthorizedAccessException("Reports require an authorized warehouse role.");

        var accessible = (await warehouseAccess.GetAccessibleWarehousesAsync(cancellationToken)).Select(w => w.Id).ToList();
        if (request.WarehouseId is { } only)
        {
            accessible = accessible.Where(id => id == only).ToList();
        }

        var issues = new List<ReconciliationIssueDto>();

        var batches = await db.StockBatches.AsNoTracking()
            .Where(b => accessible.Contains(b.PalletPosition.Rack.WarehouseId))
            .Select(b => new
            {
                b.MaterialId, b.MfgMonth, b.StockSubtypeId, b.PalletPositionId, b.QuantityBoxes,
                Location = b.PalletPosition.LocationCode, b.PalletPosition.IsVirtual, ZoneTypeId = b.PalletPosition.ZoneTypeId,
                MaterialNumber = b.Material.MaterialNumber
            })
            .ToListAsync(cancellationToken);

        // 1 + 2: ledger balance and negatives.
        var ledger = await db.StockLedgerEntries.AsNoTracking()
            .Where(e => accessible.Contains(e.WarehouseId))
            .Select(e => new { e.Id, e.MaterialId, e.MfgMonth, e.StockSubtypeId, e.PalletPositionId, e.BalanceAfterBoxes })
            .ToListAsync(cancellationToken);
        var lastBalance = ledger
            .GroupBy(e => (e.MaterialId, e.MfgMonth, e.StockSubtypeId, e.PalletPositionId))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Id).First().BalanceAfterBoxes);

        var batchKeys = new HashSet<(int, int, int, int)>();
        foreach (var b in batches)
        {
            var key = (b.MaterialId, b.MfgMonth, b.StockSubtypeId, b.PalletPositionId);
            batchKeys.Add(key);
            if (b.QuantityBoxes < 0)
            {
                issues.Add(new(ReconciliationChecks.NegativeStock, "High", "-", b.Location, b.MaterialNumber, MfgMonthParser.Format(b.MfgMonth),
                    0, b.QuantityBoxes, "Stock is negative - a deduction was posted without enough boxes. Correct it with an inventory adjustment."));
            }

            if (!lastBalance.TryGetValue(key, out var balance))
            {
                issues.Add(new(ReconciliationChecks.LedgerBalance, "Info", "-", b.Location, b.MaterialNumber, MfgMonthParser.Format(b.MfgMonth),
                    0, b.QuantityBoxes, "Stock has no ledger history (loaded before the ledger existed)."));
            }
            else if (balance != b.QuantityBoxes)
            {
                issues.Add(new(ReconciliationChecks.LedgerBalance, "High", "-", b.Location, b.MaterialNumber, MfgMonthParser.Format(b.MfgMonth),
                    balance, b.QuantityBoxes, "Inventory differs from the last ledger balance - stock was changed without a ledger entry."));
            }
        }

        var orphaned = lastBalance.Where(kv => kv.Value != 0 && !batchKeys.Contains(kv.Key)).ToList();
        if (orphaned.Count > 0)
        {
            var positionIds = orphaned.Select(o => o.Key.PalletPositionId).Distinct().ToList();
            var materialIds = orphaned.Select(o => o.Key.MaterialId).Distinct().ToList();
            var positions = await db.PalletPositions.AsNoTracking().Where(p => positionIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.LocationCode, cancellationToken);
            var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.MaterialNumber, cancellationToken);
            foreach (var o in orphaned)
            {
                issues.Add(new(ReconciliationChecks.LedgerBalance, "High", "-",
                    positions.GetValueOrDefault(o.Key.PalletPositionId, "?"), materials.GetValueOrDefault(o.Key.MaterialId),
                    MfgMonthParser.Format(o.Key.MfgMonth), o.Value, 0, "The ledger says boxes are here but there is no stock row - stock was removed without a ledger entry."));
            }
        }

        // 3 + 4: GRN lines (posted, not rejected).
        var lines = await db.InwardTransactionLines.AsNoTracking()
            .Where(l => !l.InwardTransaction.IsRejected && l.InwardTransaction.PostedAt != null && accessible.Contains(l.InwardTransaction.WarehouseId))
            .Select(l => new
            {
                Reference = l.InwardTransaction.ReferenceNumber,
                MaterialNumber = l.Material.MaterialNumber,
                l.MfgMonth,
                l.RequestedQuantityBoxes,
                l.AllocatedQuantityBoxes,
                PutawayTotal = l.Putaways.Sum(p => (decimal?)p.QuantityBoxes) ?? 0m
            })
            .ToListAsync(cancellationToken);
        foreach (var l in lines)
        {
            var pkm = l.MfgMonth is { } m ? MfgMonthParser.Format(m) : "-";
            if (l.PutawayTotal > l.RequestedQuantityBoxes)
            {
                issues.Add(new(ReconciliationChecks.OverAllocated, "High", l.Reference, "-", l.MaterialNumber, pkm,
                    l.RequestedQuantityBoxes, l.PutawayTotal, "More boxes were put away than received - stock is over-posted. Run Refresh Put-away on the GRN."));
            }

            if (l.PutawayTotal != l.AllocatedQuantityBoxes)
            {
                issues.Add(new(ReconciliationChecks.AllocationTotal, "Medium", l.Reference, "-", l.MaterialNumber, pkm,
                    l.AllocatedQuantityBoxes, l.PutawayTotal, "The line's allocated total does not equal the sum of its put-away rows."));
            }
        }

        // 5: unplaced put-aways must be backed by stock at their cell.
        var untouched = await db.InwardPutaways.AsNoTracking()
            .Where(p => p.IsConfirmed && !p.IsPickerPlaced && !p.IsExcludedLocked && p.ExcludedLocation == null
                && !p.InwardTransactionLine.InwardTransaction.IsRejected && p.InwardTransactionLine.InwardTransaction.PostedAt != null
                && p.InwardTransactionLine.MfgMonth != null
                && accessible.Contains(p.InwardTransactionLine.InwardTransaction.WarehouseId))
            .Select(p => new
            {
                p.PalletPositionId, Location = p.PalletPosition.LocationCode,
                p.InwardTransactionLine.MaterialId, MaterialNumber = p.InwardTransactionLine.Material.MaterialNumber,
                Mfg = p.InwardTransactionLine.MfgMonth!.Value, p.QuantityBoxes,
                Reference = p.InwardTransactionLine.InwardTransaction.ReferenceNumber
            })
            .ToListAsync(cancellationToken);
        var onHand = batches.Where(b => b.StockSubtypeId == LocationSubtypeIds.Good).GroupBy(b => (b.MaterialId, b.MfgMonth, b.PalletPositionId))
            .ToDictionary(g => g.Key, g => g.Sum(b => b.QuantityBoxes));
        foreach (var g in untouched.GroupBy(u => (u.MaterialId, u.Mfg, u.PalletPositionId)))
        {
            var expected = g.Sum(u => u.QuantityBoxes);
            var actual = onHand.GetValueOrDefault(g.Key);
            if (actual < expected)
            {
                var first = g.First();
                issues.Add(new(ReconciliationChecks.MissingStock, "High", string.Join(", ", g.Select(u => u.Reference).Distinct()), first.Location,
                    first.MaterialNumber, MfgMonthParser.Format(first.Mfg), expected, actual,
                    "A posted put-away is still waiting for the picker but the cell holds fewer boxes - stock moved without the put-away being updated."));
            }
        }

        // 6: open reservations on rejected GRNs.
        var closed = await db.InwardPutaways.AsNoTracking()
            .Where(p => !p.IsConfirmed && p.InwardTransactionLine.InwardTransaction.IsRejected
                && accessible.Contains(p.InwardTransactionLine.InwardTransaction.WarehouseId))
            .Select(p => new
            {
                Reference = p.InwardTransactionLine.InwardTransaction.ReferenceNumber, Location = p.PalletPosition.LocationCode,
                MaterialNumber = p.InwardTransactionLine.Material.MaterialNumber, p.InwardTransactionLine.MfgMonth, p.QuantityBoxes
            })
            .ToListAsync(cancellationToken);
        foreach (var c in closed)
        {
            issues.Add(new(ReconciliationChecks.ClosedReservation, "Medium", c.Reference, c.Location, c.MaterialNumber,
                c.MfgMonth is { } m ? MfgMonthParser.Format(m) : "-", 0, c.QuantityBoxes,
                "A rejected GRN still reserves this location - it blocks the cell. Run the stale-reservation clean-up."));
        }

        // 7: one material per location (GIT and virtual cells aside).
        foreach (var g in batches.Where(b => b.QuantityBoxes > 0 && !b.IsVirtual && b.ZoneTypeId != ZoneTypeIds.Git).GroupBy(b => b.PalletPositionId))
        {
            var materials = g.Select(b => b.MaterialNumber).Distinct().ToList();
            if (materials.Count > 1)
            {
                issues.Add(new(ReconciliationChecks.MixedLocation, "Medium", "-", g.First().Location, null, null,
                    1, materials.Count, $"Holds materials {string.Join(", ", materials)}. Move one out or confirm it is intended."));
            }
        }

        var counts = ReconciliationChecks.All.ToDictionary(c => c, c => issues.Count(i => i.Check == c));
        return new StockReconciliationReportDto(
            issues.OrderBy(i => i.Severity == "High" ? 0 : i.Severity == "Medium" ? 1 : 2).ThenBy(i => i.Check).ThenBy(i => i.Location).ToList(),
            counts, batches.Count, lines.Count);
    }
}
