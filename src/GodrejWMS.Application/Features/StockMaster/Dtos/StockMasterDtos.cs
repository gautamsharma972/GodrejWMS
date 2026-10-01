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
    bool IsActive = true);

public sealed record StockSummaryDto(
    int TotalRacks,
    int TotalPalletPositions,
    int OccupiedPalletPositions,
    decimal OccupancyPercent,
    int TotalCapacityBoxes,
    decimal TotalStockBoxes,
    int TotalActiveMaterials,
    int PendingPutawayTransactions,
    decimal PendingPutawayBoxes,
    int ReservedLocationCount,
    int ExceptionLineCount,
    decimal TodayInwardBoxes,
    decimal TodayConfirmedBoxes,
    decimal TodayPulloutBoxes,
    int FullLocationCount,
    decimal FreeCapacityBoxes,
    IReadOnlyList<SeasonBreakdownDto> BySeason);

public sealed record SeasonBreakdownDto(int SeasonId, string SeasonCode, string SeasonName, decimal TotalQuantityBoxes, int MaterialCount);
