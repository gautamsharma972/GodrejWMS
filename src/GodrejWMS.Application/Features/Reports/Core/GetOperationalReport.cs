using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.Reports.Core;

public sealed class GetOperationalReportHandler(
    IApplicationDbContext db,
    ICurrentUserService user,
    IUserDisplayNameService userDisplayNames)
    : IRequestHandler<GetOperationalReportQuery, PaginatedList<ReportRow>>
{
    public async Task<PaginatedList<ReportRow>> Handle(GetOperationalReportQuery request, CancellationToken ct)
    {
        if (!user.IsInRole("Admin") && !user.IsInRole("Supervisor")) throw new UnauthorizedAccessException("Reports require Admin or Supervisor access.");
        if (!Enum.IsDefined(request.Kind) || request.Page < 1 || request.PageSize is < 1 or > 10000)
            throw new ArgumentException("Invalid report or pagination.");
        if (request.Filter.From > request.Filter.To) throw new ArgumentException("From date must be before To date.");
        if (request.Filter.Pkm is int month && (month / 100 < 1900 || month % 100 is < 1 or > 12))
            throw new ArgumentException("PKM must be yyyyMM.");
        if (request.Filter.WarehouseId is int warehouse &&
            (!await db.Warehouses.AnyAsync(w => w.Id == warehouse, ct) ||
             (!user.IsInRole("Admin") && !await db.UserWarehouses.AnyAsync(a => a.UserId == user.UserId && a.WarehouseId == warehouse, ct))))
            throw new UnauthorizedAccessException("Warehouse is not assigned to you.");
        if (request.Sort is not ("date" or "sku" or "location" or "quantity" or "reference"))
        {
            throw new ArgumentException("Unsupported sort column.");
        }

        var query = Build(request.Kind, request.Filter);

        // Sorting/pagination deliberately happens in memory, not as part of the EF query: Build()
        // already produces a query joined across 6+ tables (StockBatch/PalletPosition/Rack/
        // Material/Warehouse/ZoneType/LocationSubtype, each re-applying the warehouse-access
        // global query filter), and stacking a multi-key ORDER BY on top of that many nested
        // joins is more than Pomelo's MySQL translator can turn into one statement - it throws a
        // raw "could not be translated" exception regardless of which column is sorted on. The
        // total count is still computed in SQL; only the already-filtered row set is materialized
        // before sorting/paging, matching the same trade-off this report suite's other handlers
        // (e.g. GetInwardPutawayTurnaroundHandler) already make for complex in-memory shaping.
        var totalCount = await query.CountAsync(ct);
        var rows = await query.ToListAsync(ct);

        IOrderedEnumerable<ReportRow> ordered = (request.Sort, request.Descending) switch
        {
            ("date", false) => rows.OrderBy(r => r.Date),
            ("date", true) => rows.OrderByDescending(r => r.Date),
            ("sku", false) => rows.OrderBy(r => r.Sku),
            ("sku", true) => rows.OrderByDescending(r => r.Sku),
            ("location", false) => rows.OrderBy(r => r.Location),
            ("location", true) => rows.OrderByDescending(r => r.Location),
            ("quantity", false) => rows.OrderBy(r => r.Quantity),
            ("quantity", true) => rows.OrderByDescending(r => r.Quantity),
            ("reference", false) => rows.OrderBy(r => r.Reference),
            _ => rows.OrderByDescending(r => r.Reference)
        };

        var page = ordered
            .ThenBy(r => r.WarehouseId).ThenBy(r => r.Sku).ThenBy(r => r.Pkm).ThenBy(r => r.Location).ThenBy(r => r.Type).ThenBy(r => r.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var missingNames = page.Where(row => string.IsNullOrWhiteSpace(row.User) && !string.IsNullOrWhiteSpace(row.UserId)).ToList();
        if (missingNames.Count > 0)
        {
            var names = await userDisplayNames.GetDisplayNamesAsync(missingNames.Select(row => row.UserId), ct);
            foreach (var row in missingNames)
                row.User = names.GetValueOrDefault(row.UserId, "Unknown user");
        }

        return new PaginatedList<ReportRow>(page, totalCount, request.Page, request.PageSize);
    }

    private IQueryable<ReportRow> Apply(IQueryable<ReportRow> q, ReportFilter f)
    {
        if (f.WarehouseId.HasValue) q = q.Where(r => r.WarehouseId == f.WarehouseId);
        if (!string.IsNullOrWhiteSpace(f.Search)) q = q.Where(r => r.Description.Contains(f.Search) || r.Reference.Contains(f.Search) || r.Location.Contains(f.Search) || r.Sku.ToString()!.Contains(f.Search));
        if (!string.IsNullOrWhiteSpace(f.Location)) q = q.Where(r => r.Location == f.Location || r.Source == f.Location || r.Destination == f.Location);
        if (!string.IsNullOrWhiteSpace(f.Zone)) q = q.Where(r => r.Zone == f.Zone);
        if (!string.IsNullOrWhiteSpace(f.Rack)) q = q.Where(r => r.Rack == f.Rack);
        if (f.Column.HasValue) q = q.Where(r => r.Column == f.Column);
        if (f.Level.HasValue) q = q.Where(r => r.Level == f.Level);
        if (f.Sku.HasValue) q = q.Where(r => r.Sku == f.Sku);
        if (!string.IsNullOrWhiteSpace(f.Design)) q = q.Where(r => r.Design == f.Design);
        if (f.Pkm.HasValue) q = q.Where(r => r.Pkm == f.Pkm);
        if (f.From.HasValue) q = q.Where(r => r.Date >= f.From);
        if (f.To.HasValue) q = q.Where(r => r.Date < f.To);
        if (!string.IsNullOrWhiteSpace(f.Status)) q = q.Where(r => r.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.User)) q = q.Where(r => r.User.Contains(f.User));
        if (!string.IsNullOrWhiteSpace(f.Reference)) q = q.Where(r => r.Reference.Contains(f.Reference));
        return q;
    }

    private IQueryable<ReportRow> Stock() => db.StockBatches.AsNoTracking().Where(b => b.QuantityBoxes > 0).Select(b => new ReportRow
    {
        Id = b.Id, WarehouseId = b.PalletPosition.Rack.WarehouseId, Warehouse = b.PalletPosition.Rack.Warehouse.Code,
        Sku = b.Material.MaterialNumber, Description = b.Material.Description, Design = b.Material.DesignType,
        Pkm = b.MfgMonth, Quantity = b.QuantityBoxes, Location = b.PalletPosition.LocationCode,
        Zone = b.PalletPosition.ZoneType.Code, Rack = b.PalletPosition.Rack.Code, Column = b.PalletPosition.Column,
        Level = b.PalletPosition.Level, PalletSize = b.Material.PalletCapacityBoxes, Date = b.UpdatedAt ?? b.CreatedAt,
        Status = b.StockSubtype.Code
    });

    private IQueryable<ReportRow> Positions() => db.PalletPositions.AsNoTracking().Where(p => p.IsActive).Select(p => new ReportRow
    {
        Id = p.Id, WarehouseId = p.Rack.WarehouseId, Warehouse = p.Rack.Warehouse.Code, Location = p.LocationCode,
        Zone = p.ZoneType.Code, Rack = p.Rack.Code, Column = p.Column, Level = p.Level,
        Capacity = p.CapacityBoxes, Quantity = p.StockBatches.Sum(b => (decimal?)b.QuantityBoxes) ?? 0,
        ReservedCapacity = db.InwardPutaways.Where(a => a.PalletPositionId == p.Id && !a.IsConfirmed && !a.InwardTransactionLine.InwardTransaction.IsRejected).Sum(a => (decimal?)a.QuantityBoxes) ?? 0,
        Available = p.CapacityBoxes - (p.StockBatches.Sum(b => (decimal?)b.QuantityBoxes) ?? 0) -
            (db.InwardPutaways.Where(a => a.PalletPositionId == p.Id && !a.IsConfirmed && !a.InwardTransactionLine.InwardTransaction.IsRejected).Sum(a => (decimal?)a.QuantityBoxes) ?? 0),
        Status = "Active", Locations = 1,
        OccupiedLocations = p.StockBatches.Any(b => b.QuantityBoxes > 0) ? 1 : 0,
        SkuCount = p.StockBatches.Select(b => b.MaterialId).Distinct().Count()
    });

    private IQueryable<ReportRow> Inward() => db.InwardTransactionLines.AsNoTracking().Select(l => new ReportRow
    {
        Id = l.Id, WarehouseId = l.InwardTransaction.WarehouseId, Warehouse = l.InwardTransaction.Warehouse.Code,
        Reference = l.InwardTransaction.ReferenceNumber, Type = "Inward", Date = l.InwardTransaction.CreatedAt,
        Sku = l.Material.MaterialNumber, Description = l.Material.Description, Design = l.Material.DesignType,
        Pkm = l.MfgMonth, Quantity = l.RequestedQuantityBoxes, Requested = l.RequestedQuantityBoxes,
        Completed = l.Putaways.Where(p => p.IsConfirmed).Sum(p => (decimal?)p.QuantityBoxes) ?? 0,
        Pending = l.RequestedQuantityBoxes - (l.Putaways.Where(p => p.IsConfirmed).Sum(p => (decimal?)p.QuantityBoxes) ?? 0),
        Status = l.InwardTransaction.IsRejected ? "Rejected" : l.Status == Domain.Enums.AllocationStatus.Fulfilled ? "Allocated" : l.Status == Domain.Enums.AllocationStatus.Partial ? "Partial" : "Failed",
        UserId = l.InwardTransaction.CreatedByUserId ?? "", Reason = l.Remarks ?? ""
    });

    private IQueryable<ReportRow> Putaways() => db.InwardPutaways.AsNoTracking().Select(p => new ReportRow
    {
        Id = p.Id, WarehouseId = p.InwardTransactionLine.InwardTransaction.WarehouseId, Warehouse = p.InwardTransactionLine.InwardTransaction.Warehouse.Code,
        Reference = p.InwardTransactionLine.InwardTransaction.ReferenceNumber, Type = "Putaway", Date = p.ConfirmedAt,
        Sku = p.InwardTransactionLine.Material.MaterialNumber, Description = p.InwardTransactionLine.Material.Description,
        Design = p.InwardTransactionLine.Material.DesignType, Pkm = p.InwardTransactionLine.MfgMonth,
        Location = p.PalletPosition.LocationCode, Destination = p.PalletPosition.LocationCode,
        Rack = p.PalletPosition.Rack.Code, Zone = p.PalletPosition.ZoneType.Code, Column = p.PalletPosition.Column, Level = p.PalletPosition.Level,
        Quantity = p.QuantityBoxes, In = p.IsConfirmed ? p.QuantityBoxes : 0,
        Status = p.IsConfirmed ? "Confirmed" : "Pending", User = p.ConfirmedByUserName ?? "",
        UserId = p.ConfirmedByUserId ?? "", Reason = p.OverrideReason ?? p.AllocationReason ?? ""
    });

    private IQueryable<ReportRow> Outward() => db.PulloutTransactionLines.AsNoTracking().Select(l => new ReportRow
    {
        Id = l.Id, WarehouseId = l.PulloutTransaction.WarehouseId, Warehouse = l.PulloutTransaction.Warehouse.Code,
        Reference = l.PulloutTransaction.ReferenceNumber, Type = "Outward", Date = l.PulloutTransaction.ConfirmedAt ?? l.PulloutTransaction.CreatedAt,
        Sku = l.Material.MaterialNumber, Description = l.Material.Description, Design = l.Material.DesignType,
        Quantity = l.PickedQuantityBoxes, Requested = l.RequestedQuantityBoxes,
        Completed = l.PulloutTransaction.IsConfirmed ? l.PickedQuantityBoxes : 0,
        Pending = l.RequestedQuantityBoxes - (l.PulloutTransaction.IsConfirmed ? l.PickedQuantityBoxes : 0),
        Available = l.PickedQuantityBoxes,
        Status = !l.PulloutTransaction.IsConfirmed ? "Not confirmed" : l.Status == Domain.Enums.AllocationStatus.Fulfilled ? "Fulfilled" : l.Status == Domain.Enums.AllocationStatus.Partial ? "Partial" : "Failed",
        User = l.PulloutTransaction.ConfirmedByUserName ?? "", UserId = l.PulloutTransaction.ConfirmedByUserId ?? "", Reason = l.Remarks ?? ""
    });

    private IQueryable<ReportRow> Picks() => db.PulloutPicks.AsNoTracking().Where(p => p.PulloutTransactionLine.PulloutTransaction.IsConfirmed).Select(p => new ReportRow
    {
        Id = p.Id, WarehouseId = p.PulloutTransactionLine.PulloutTransaction.WarehouseId, Warehouse = p.PulloutTransactionLine.PulloutTransaction.Warehouse.Code,
        Reference = p.PulloutTransactionLine.PulloutTransaction.ReferenceNumber, Type = "Pick", Date = p.PulloutTransactionLine.PulloutTransaction.ConfirmedAt,
        Sku = p.PulloutTransactionLine.Material.MaterialNumber, Description = p.PulloutTransactionLine.Material.Description,
        Design = p.PulloutTransactionLine.Material.DesignType, Pkm = p.MfgMonth,
        Location = p.PalletPosition.LocationCode, Source = p.PalletPosition.LocationCode,
        Rack = p.PalletPosition.Rack.Code, Zone = p.PalletPosition.ZoneType.Code, Column = p.PalletPosition.Column, Level = p.PalletPosition.Level,
        Quantity = p.QuantityBoxes, Out = p.QuantityBoxes, Status = "Confirmed",
        User = p.PulloutTransactionLine.PulloutTransaction.ConfirmedByUserName ?? "",
        UserId = p.PulloutTransactionLine.PulloutTransaction.ConfirmedByUserId ?? ""
    });

    private IQueryable<ReportRow> Moves() => db.StockMovements.AsNoTracking().Select(m => new ReportRow
    {
        Id = m.Id, WarehouseId = m.SourcePalletPosition.Rack.WarehouseId, Warehouse = m.SourcePalletPosition.Rack.Warehouse.Code,
        Reference = m.MovementNumber, Type = "Movement", Date = m.CreatedAt, Sku = m.Material.MaterialNumber,
        Description = m.Material.Description, Design = m.Material.DesignType, Pkm = m.MfgMonth,
        Source = m.SourcePalletPosition.LocationCode, Destination = m.DestinationPalletPosition.LocationCode,
        Location = m.SourcePalletPosition.LocationCode, Zone = m.SourcePalletPosition.ZoneType.Code,
        Rack = m.SourcePalletPosition.Rack.Code, Column = m.SourcePalletPosition.Column, Level = m.SourcePalletPosition.Level,
        Quantity = m.QuantityBoxes, Status = "Completed", User = m.PerformedByUserName ?? "",
        UserId = m.PerformedByUserId ?? "", Reason = m.Reason.DisplayName
    });

    private IQueryable<ReportRow> Build(ReportKind kind, ReportFilter filter)
    {
        if (kind is ReportKind.CurrentStock or ReportKind.SkuStock or ReportKind.LocationStock or ReportKind.PkmStock)
        {
            var stock = Apply(Stock(), filter);
            if (kind is ReportKind.CurrentStock or ReportKind.LocationStock) return stock;
            if (kind == ReportKind.SkuStock) return stock.GroupBy(r => new { r.WarehouseId, r.Warehouse, r.Sku, r.Description, r.Design })
                .Select(g => new ReportRow { WarehouseId = g.Key.WarehouseId, Warehouse = g.Key.Warehouse, Sku = g.Key.Sku,
                    Description = g.Key.Description, Design = g.Key.Design, Quantity = g.Sum(r => r.Quantity),
                    Locations = g.Select(r => r.Location).Distinct().Count(), PkmVariants = g.Select(r => r.Pkm).Distinct().Count() });
            return stock.GroupBy(r => new { r.WarehouseId, r.Warehouse, r.Sku, r.Description, r.Design, r.Pkm })
                .Select(g => new ReportRow { WarehouseId = g.Key.WarehouseId, Warehouse = g.Key.Warehouse, Sku = g.Key.Sku,
                    Description = g.Key.Description, Design = g.Key.Design, Pkm = g.Key.Pkm,
                    Quantity = g.Sum(r => r.Quantity), Locations = g.Select(r => r.Location).Distinct().Count() });
        }
        if (kind is ReportKind.Capacity or ReportKind.AvailableLocations or ReportKind.RackStock or ReportKind.RackLevel or ReportKind.ZoneStock)
        {
            var positions = Apply(Positions(), filter);
            if (kind == ReportKind.AvailableLocations) return positions.Where(r => r.Available > 0);
            return positions.GroupBy(r => new { r.WarehouseId, r.Warehouse,
                Zone = kind == ReportKind.ZoneStock ? r.Zone : "",
                Rack = kind == ReportKind.RackStock || kind == ReportKind.RackLevel ? r.Rack : "",
                Level = kind == ReportKind.RackLevel ? r.Level : null })
                .Select(g => new ReportRow { WarehouseId = g.Key.WarehouseId, Warehouse = g.Key.Warehouse, Zone = g.Key.Zone,
                    Rack = g.Key.Rack, Level = g.Key.Level, Locations = g.Count(), OccupiedLocations = g.Sum(r => r.OccupiedLocations),
                    Quantity = g.Sum(r => r.Quantity), Capacity = g.Sum(r => r.Capacity),
                    ReservedCapacity = g.Sum(r => r.ReservedCapacity), Available = g.Sum(r => r.Available) });
        }
        var query = kind switch
        {
            ReportKind.InwardTransactions or ReportKind.InwardDetails or ReportKind.InwardPutaway => Inward(),
            ReportKind.InwardExceptions => Inward().Where(r => r.Status == "Partial" || r.Status == "Failed" || r.Status == "Rejected"),
            ReportKind.PutawayDetails => Putaways(),
            ReportKind.OutwardTransactions => Outward(),
            ReportKind.PendingPicking => Outward().Where(r => r.Pending > 0),
            ReportKind.OutwardShortages => Outward().Where(r => r.Requested > r.Available),
            ReportKind.PickingDetails => Picks(),
            ReportKind.MovementRegister => Moves(),
            ReportKind.LocationHistory or ReportKind.SkuHistory or ReportKind.Ledger =>
                Putaways().Concat(Picks()).Concat(Moves()),
            _ => throw new ArgumentException("Unsupported report.")
        };
        return Apply(query, filter);
    }
}
