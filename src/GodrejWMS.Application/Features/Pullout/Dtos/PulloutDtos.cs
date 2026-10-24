using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Pullout.Dtos;

/// <summary>One parsed row from a "Format inventory pullout-upload" Excel: Material Code, Total Stock in CFB,
/// and an optional, unvalidated Plant Name.</summary>
public sealed record PulloutImportRow(long MaterialCode, decimal QuantityBoxes, int RowNumber, string? PlantName = null);

/// <summary>One row of the Pullout pick-list download (post-confirmation export) - kept separate
/// from <c>StockMasterRowDto</c> since Plant Name is specific to Pullout, not to stock in general.</summary>
public sealed record PulloutDownloadRowDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal QuantityBoxes,
    string MfgMonthLabel,
    string PalletPositionCode,
    string? PlantName = null);

public sealed record PulloutPickDto(
    string LocationCode,
    string MfgMonthLabel,
    decimal QuantityBoxes,
    string? OverrideReason = null);

public sealed record PulloutPickChangeOptionDto(
    int PalletPositionId,
    string LocationCode,
    string MfgMonthLabel,
    decimal StockBoxes,
    decimal AvailableBoxes,
    bool IsCurrent);

public sealed record PulloutLineResultDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal RequestedQuantityBoxes,
    decimal PickedQuantityBoxes,
    AllocationStatus Status,
    string? Remarks,
    IReadOnlyList<PulloutPickDto> Picks,
    string? PlantName = null);

public sealed record PulloutResultDto(
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PulloutLineResultDto> Lines);

public sealed record PulloutHistoryDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    int LineCount,
    decimal TotalQuantityBoxes,
    AllocationStatus Status,
    bool IsConfirmed = true,
    bool IsRejected = false);

public sealed record PulloutDetailLineDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal RequestedQuantityBoxes,
    decimal PickedQuantityBoxes,
    AllocationStatus Status,
    string? Remarks,
    IReadOnlyList<PulloutPickDto> Picks,
    string? PlantName = null);

public sealed record PulloutDetailDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PulloutDetailLineDto> Lines,
    bool IsConfirmed = true,
    bool IsRejected = false,
    DateTimeOffset? RejectedAt = null,
    string? RejectedByUserName = null,
    string? RejectionReason = null);
