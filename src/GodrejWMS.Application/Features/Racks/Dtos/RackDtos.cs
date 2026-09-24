namespace GodrejWMS.Application.Features.Racks.Dtos;

public sealed record RackDto(
    int Id,
    string Code,
    string? Name,
    int Columns,
    int Levels,
    decimal ShelfLengthMm,
    decimal ShelfWidthMm,
    decimal ShelfHeightMm,
    bool IsActive,
    int TotalPositions,
    int TotalCapacityBoxes,
    int OccupiedPositions,
    decimal OccupancyPercent);

public sealed record PalletPositionDto(
    int Id,
    int RackId,
    string LocationCode,
    string? FlatLabel,
    int Column,
    int Level,
    int LocationTypeId,
    string LocationTypeCode,
    string LocationTypeName,
    int LocationSubtypeId,
    string LocationSubtypeCode,
    string LocationSubtypeName,
    int ZoneTypeId,
    string ZoneTypeCode,
    string ZoneTypeName,
    int DistancePriority,
    int MaxPallets,
    int BoxesPerPallet,
    int CapacityBoxes,
    decimal OccupiedBoxes,
    decimal FreeBoxes,
    bool IsFull,
    bool IsActive,
    IReadOnlyList<PalletPositionStockDto> Stock);

public sealed record PalletPositionStockDto(
    long MaterialNumber,
    string MaterialDescription,
    string DesignType,
    string MfgMonthLabel,
    string StockSubtypeCode,
    string StockSubtypeName,
    decimal QuantityBoxes,
    int MaterialId = 0,
    int MfgMonth = 0);

public sealed record RackLayoutDto(
    int RackId,
    string RackCode,
    int Columns,
    int Levels,
    IReadOnlyList<PalletPositionDto> Positions);
