using GodrejWMS.Application.Common.Models;
using MediatR;

namespace GodrejWMS.Application.Features.Reports.Core;

public enum ReportKind
{
    CurrentStock, SkuStock, LocationStock, PkmStock, InwardTransactions, InwardDetails,
    InwardPutaway, PutawayDetails, InwardExceptions, OutwardTransactions, PickingDetails,
    PendingPicking, OutwardShortages, MovementRegister, LocationHistory, SkuHistory,
    Ledger, RackStock, ZoneStock, Capacity, AvailableLocations, RackLevel
}

public sealed record ReportFilter(int? WarehouseId = null, string? Search = null, string? Location = null,
    string? Zone = null, string? Rack = null, int? Column = null, int? Level = null,
    long? Sku = null, string? Design = null, int? Pkm = null, DateTimeOffset? From = null,
    DateTimeOffset? To = null, string? Status = null, string? User = null, string? Reference = null);

public sealed record GetOperationalReportQuery(ReportKind Kind, ReportFilter Filter,
    int Page = 1, int PageSize = 50, string Sort = "reference", bool Descending = false)
    : IRequest<PaginatedList<ReportRow>>;

public sealed class ReportRow
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Warehouse { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTimeOffset? Date { get; set; }
    public long? Sku { get; set; }
    public string Description { get; set; } = "";
    public string Design { get; set; } = "";
    public int? Pkm { get; set; }
    public string Zone { get; set; } = "";
    public string Rack { get; set; } = "";
    public int? Column { get; set; }
    public int? Level { get; set; }
    public string Location { get; set; } = "";
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Requested { get; set; }
    public decimal Completed { get; set; }
    public decimal Pending { get; set; }
    public decimal Capacity { get; set; }
    public decimal ReservedCapacity { get; set; }
    public decimal Available { get; set; }
    public decimal In { get; set; }
    public decimal Out { get; set; }
    public int Locations { get; set; }
    public int OccupiedLocations { get; set; }
    public int PkmVariants { get; set; }
    public int SkuCount { get; set; }
    public int? PalletSize { get; set; }
    public string Status { get; set; } = "";
    public string User { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Reason { get; set; } = "";
}
