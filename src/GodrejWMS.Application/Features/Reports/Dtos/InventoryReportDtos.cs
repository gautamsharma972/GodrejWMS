namespace GodrejWMS.Application.Features.Reports.Dtos;

public sealed record MaterialStockReportRowDto(
    long MaterialCode,
    string MaterialDescription,
    int Pkm,
    decimal Mrp,
    decimal BoxQuantity,
    /// <summary>Book stock whose put-away is confirmed (BoxQuantity = InStockBoxes + GitInBoxes).</summary>
    decimal InStockBoxes = 0,
    /// <summary>Book stock posted by an inward upload whose put-away the picker has not confirmed yet.</summary>
    decimal GitInBoxes = 0,
    /// <summary>Boxes of unconfirmed picks: already deducted from the stock, still on the rack. NOT part of BoxQuantity.</summary>
    decimal GitOutBoxes = 0);

public sealed record LocationStockReportRowDto(
    string LocationCode,
    long? MaterialCode,
    string? MaterialDescription,
    string? AopDescription,
    string? DesignType,
    int? Pkm,
    decimal StockQty,
    int? Capacity,
    decimal InStockQty = 0,
    decimal GitInQty = 0,
    /// <summary>Boxes of unconfirmed picks at this location: already deducted, still on the rack. NOT part of StockQty.</summary>
    decimal GitOutQty = 0);

public sealed record InventoryValuationRowDto(
    int MaterialId,
    long MaterialNumber,
    string MaterialDescription,
    string DesignType,
    string SeasonName,
    decimal TotalBoxes,
    decimal TotalValue);

public sealed record InventoryAgingRowDto(
    string AgeBucket,
    string MovementTypeName,
    int BatchCount,
    decimal TotalBoxes);

public sealed record ZoneUtilizationRowDto(
    string ZoneCode,
    string ZoneName,
    int LocationCount,
    int CapacityBoxes,
    decimal OccupiedBoxes,
    decimal OccupancyPercent);

public sealed record LocationTypeUtilizationRowDto(
    string LocationTypeCode,
    string LocationTypeName,
    int LocationCount,
    int CapacityBoxes,
    decimal OccupiedBoxes,
    decimal OccupancyPercent);

public sealed record WarehouseUtilizationDto(
    IReadOnlyList<ZoneUtilizationRowDto> ByZone,
    IReadOnlyList<LocationTypeUtilizationRowDto> ByLocationType);
