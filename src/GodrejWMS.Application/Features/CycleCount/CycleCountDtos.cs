using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Features.CycleCount;

public sealed record CycleCountRackDto(string RackCode, CycleCountRackStatus Status, int TotalCards, int CompletedCards, int PendingCards, string? SubmittedByUserName, DateTimeOffset? SubmittedAt, bool IncludesDamageStock = false,
    /// <summary>The rack's name as the floor knows it (several rack rows can share one name; the code stays the unique key).</summary>
    string RackName = "", string? FirstLocation = null, string? LastLocation = null);

public sealed record CycleCountBatchDto(
    int Id, string BatchNumber, int WarehouseId, string WarehouseName,
    DateTimeOffset ScheduleFrom, DateTimeOffset ScheduleTo, DateTimeOffset? ActualStartAt, DateTimeOffset? ActualEndAt,
    CycleCountBatchStatus Status, string? Remarks, string? DropReason, bool CardsGenerated, bool AutoReleaseFreezeAtScheduleEnd,
    IReadOnlyList<CycleCountRackDto> Racks,
    int TotalLocations, int TotalCards, int CompletedCards, int PendingCards, int SubmittedCards, int ExcludedCards, int PendingReviewCount,
    decimal CompletionPercent, string? CreatedByUserName, DateTimeOffset CreatedAt);

public sealed record CycleCountHistoryDto(
    long Id, string Action, string? RackCode, string? PreviousValue, string? NewValue, string? Reason, string? UserName, DateTimeOffset OccurredAt);

public sealed record CycleCountBatchDetailDto(CycleCountBatchDto Batch, IReadOnlyList<CycleCountHistoryDto> History);

public sealed record CycleCountRackOptionDto(string RackCode, int Locations, string RackName = "");

public sealed record CycleCountScheduleWarningsDto(int OpenInwardOrders, int OpenPullouts, IReadOnlyList<string> Messages);

public sealed record CycleCountFreezeDto(bool IsFrozen, string? BatchNumber, string? Message);

public sealed record CycleCountLocationDto(int PalletPositionId, string LocationCode, int TotalCards, int PendingCards);

public sealed record CycleCountCardDto(
    int Id, string RackCode, int PalletPositionId, string LocationCode,
    int? SystemMaterialId, long? SystemMaterialNumber, string? SystemDescription, string? SystemPkm, decimal SystemQuantityBoxes, int SystemStockSubtypeId,
    int? PhysicalMaterialId, long? PhysicalMaterialNumber, string? PhysicalDescription, string? PhysicalPkm, decimal? PhysicalQuantityBoxes, int? PhysicalStockSubtypeId,
    decimal? DifferenceBoxes, CycleCountCardState State, bool IsExtra, bool IsExcluded, bool IsSubmitted,
    string? CountedByUserName, DateTimeOffset? CountedAt, uint RowVersion)
{
    /// <summary>Completed = the counter confirmed or corrected it (submitted or not).</summary>
    public bool IsCompleted => State != CycleCountCardState.Pending;

    /// <summary>A card whose physical figures differ from the system (quantity, material or PKM), or stock found where the system had none.</summary>
    public bool IsDiscrepancy => State == CycleCountCardState.Corrected;

    public string StockType => (PhysicalStockSubtypeId ?? SystemStockSubtypeId) == LocationSubtypeIds.Damage ? "Damage" : "Good";
}

public sealed record CycleCountCardsDto(
    int BatchId, string BatchNumber, string RackCode, CycleCountBatchStatus BatchStatus, CycleCountRackStatus RackStatus,
    IReadOnlyList<CycleCountCardDto> Cards, bool IncludesDamageStock, int TotalCards, int CompletedCards, int PendingCards, decimal CompletionPercent);

public sealed record CycleCountSubmitSummaryDto(
    int BatchId, string BatchNumber, string WarehouseName, string RackCode, CycleCountRackStatus RackStatus,
    int TotalCards, int VerifiedCards, int CorrectedCards, int PendingCards, int ExcludedCards, decimal TotalDifferenceBoxes, decimal TotalAbsoluteDifferenceBoxes,
    bool CanSubmit, string? Blocker, int UncountedDamageCards = 0);

public sealed record CycleCountSubmitResultDto(
    string BatchNumber, string RackCode, bool AlreadySubmitted, int VerifiedCards, int CorrectedCards, int StockChanges, CycleCountBatchStatus BatchStatus);

public enum CycleCountRowKind
{
    /// <summary>System and physical agree.</summary>
    Matched = 1,
    /// <summary>Quantity differs.</summary>
    Discrepancy = 2,
    /// <summary>A new material / PKM combination was found, or the material / PKM differs from the system's: needs a closer look.</summary>
    Exceptional = 3
}

public sealed record CycleCountReviewListItemDto(
    int BatchId, string BatchNumber, string WarehouseName, string RackCode, string? SubmittedByUserName, DateTimeOffset? SubmittedAt,
    int TotalCards, int VerifiedCards, int CorrectedCards, int Discrepancies, decimal TotalDifferenceBoxes,
    CycleCountReviewStatus ReviewStatus, string? ReviewedByUserName, DateTimeOffset? ReviewedAt, string? ReviewRemarks);

public sealed record CycleCountRowDto(
    int CardId, int BatchId, string BatchNumber, string WarehouseName, string RackCode, string LocationCode,
    long? MaterialNumber, string? Description, string? Pkm, decimal SystemQuantityBoxes, decimal? PhysicalQuantityBoxes, decimal? DifferenceBoxes,
    string StockType, string CountedAs, CycleCountRowKind Kind, string CountStatus, string? CountedByUserName, DateTimeOffset? CountedAt, DateTimeOffset? SubmittedAt, bool IsExtra);

public sealed record CycleCountReviewDetailDto(
    CycleCountReviewListItemDto Header, IReadOnlyList<CycleCountRowDto> Rows, IReadOnlyList<CycleCountHistoryDto> History);

public sealed record CycleCountReportFilter(
    DateTimeOffset? From = null, DateTimeOffset? To = null, int? WarehouseId = null, string? BatchNumber = null, string? RackCode = null,
    string? Location = null, long? MaterialNumber = null, string? CountStatus = null, CycleCountReviewStatus? ReviewStatus = null, string? CountedBy = null);

public sealed record CycleCountBatchSummaryRowDto(
    string BatchNumber, string WarehouseName, DateTimeOffset ScheduleFrom, DateTimeOffset ScheduleTo, DateTimeOffset? ActualStartAt, DateTimeOffset? ActualEndAt,
    string Racks, int TotalLocations, int TotalCards, int CompletedCards, int PendingCards, int VerifiedCards, int CorrectedCards, decimal CompletionPercent,
    string? SubmittedBy, string? ReviewedBy, CycleCountBatchStatus Status);

public sealed record CycleCountUserRowDto(
    string UserName, string BatchNumber, string WarehouseName, string RackCode, int AssignedCards, int CompletedCards, int PendingCards, int CorrectedCards,
    DateTimeOffset? SubmittedAt, decimal CompletionPercent);

public sealed record CycleCountLocationOptionDto(string LocationCode, string RackCode);

public sealed record CycleCountMaterialOptionDto(long MaterialNumber, string Description);

/// <summary>The locations and materials that appear in count cards, for the searchable Location / Material code filters.</summary>
public sealed record CycleCountFilterOptionsDto(IReadOnlyList<CycleCountLocationOptionDto> Locations, IReadOnlyList<CycleCountMaterialOptionDto> Materials);
