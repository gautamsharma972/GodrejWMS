namespace GodrejWMS.Application.Features.StockMaster.Dtos;

/// <summary>One row of an Inventory Master upload: Material Code, Total Stock in CFB, Mfg Month,
/// Pallet Position, read from the same column positions <see cref="StockMasterRowDto"/> is
/// exported to - the description/design-type/velocity/season/active columns are informational
/// only on import since they're derived from the Material master, not stored per stock batch.</summary>
public sealed record StockMasterImportRow(
    long MaterialNumber,
    decimal QuantityBoxes,
    string MfgMonthText,
    string PalletPositionCode,
    int RowNumber);

/// <summary>Mirrors "Format inventory master" / "Format invent pullout-download": Material Code, Material Desc.,
/// Design Type, Total Stock in CFB, Mfg Month, Pallet Position.</summary>
public sealed record StockMasterRowDto(
    long MaterialNumber,
    string MaterialDescription,
    string DesignType,
    decimal QuantityBoxes,
    string MfgMonthLabel,
    string PalletPositionCode,
    string MovementTypeCode = "SlowMoving",
    string MovementTypeName = "Slow-moving",
    string SeasonCode = "Rainy",
    string SeasonName = "Rainy",
    bool IsActive = true,
    int StockBatchId = 0,
    int MaterialId = 0,
    int PalletPositionId = 0,
    int MfgMonth = 0,
    string StockSubtypeName = "Good");

public sealed record StockSummaryDto(
    int TotalRacks,
    int TotalPalletPositions,
    int OccupiedPalletPositions,
    decimal OccupancyPercent,
    int TotalCapacityBoxes,
    decimal TotalStockBoxes,
    int TotalActiveMaterials,
    int PendingPutawayTransactions,
    int PendingPulloutTransactions,
    decimal PendingPutawayBoxes,
    int ReservedLocationCount,
    int ExceptionLineCount,
    int TodayInwardTransactions,
    decimal TodayInwardBoxes,
    decimal TodayConfirmedBoxes,
    int TodayPulloutTransactions,
    decimal TodayPickedBoxes,
    decimal TodayPulloutBoxes,
    int FullLocationCount,
    decimal FreeCapacityBoxes,
    int LowStockLocationCount,
    IReadOnlyList<SeasonBreakdownDto> BySeason);

public sealed record LowStockLocationDto(
    int MaterialId,
    long MaterialNumber,
    string MaterialDescription,
    int MfgMonth,
    string MfgMonthLabel,
    int PalletPositionId,
    string LocationCode,
    decimal QuantityBoxes,
    decimal MrpPrice,
    int PalletCapacityBoxes,
    decimal ThresholdBoxes);

public sealed record LowStockMovementImportRow(
    long MaterialNumber,
    string MfgMonthText,
    string SourceLocationCode,
    string DestinationLocationCode,
    decimal QuantityBoxes,
    string ReasonCode,
    int RowNumber);

public sealed record SeasonBreakdownDto(int SeasonId, string SeasonCode, string SeasonName, decimal TotalQuantityBoxes, int MaterialCount);
