namespace GodrejWMS.Application.Features.Reports.Dtos;

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
