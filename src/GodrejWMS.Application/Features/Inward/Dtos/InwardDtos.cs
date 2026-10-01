using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Inward.Dtos;

/// <summary>One parsed row from an "inward" Excel upload: Material Code, Total Stock in CFB(qty), Mfg Month.</summary>
public sealed record InwardImportRow(long MaterialCode, decimal QuantityBoxes, string MfgMonthText, int RowNumber);

public sealed record InwardLineResultDto(
    long MaterialNumber,
    string MaterialDescription,
    string MfgMonthLabel,
    decimal RequestedQuantityBoxes,
    decimal AllocatedQuantityBoxes,
    AllocationStatus Status,
    string? Remarks,
    IReadOnlyList<InwardAllocationLineDto> AllocatedTo,
    bool IsConfirmed = false);

public sealed record InwardAllocationLineDto(string LocationCode, decimal QuantityBoxes, string? Reason = null, bool IsConfirmed = false, int? PutawayId = null);

public sealed record InwardResultDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<InwardLineResultDto> Lines,
    bool IsConfirmed = false,
    bool IsRejected = false);

public sealed record InwardHistoryDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    int LineCount,
    decimal TotalQuantityBoxes,
    AllocationStatus Status,
    bool IsConfirmed,
    bool IsRejected);

public sealed record InwardPutawayDto(
    string LocationCode,
    decimal QuantityBoxes,
    bool IsConfirmed,
    int Id = 0,
    string? Reason = null,
    string? OverrideReason = null,
    DateTimeOffset? ConfirmedAt = null,
    string? ConfirmedByUserName = null);

public sealed record InwardDetailLineDto(
    long MaterialNumber,
    string MaterialDescription,
    string DesignType,
    string MfgMonthLabel,
    decimal RequestedQuantityBoxes,
    decimal AllocatedQuantityBoxes,
    AllocationStatus Status,
    string? Remarks,
    IReadOnlyList<InwardPutawayDto> Putaways);

public sealed record InwardDetailDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<InwardDetailLineDto> Lines,
    bool IsConfirmed,
    bool IsRejected = false,
    DateTimeOffset? RejectedAt = null,
    string? RejectedByUserName = null,
    string? RejectionReason = null);
/// <summary>
/// One row of the inward Excel download: one put-away location of one GRN line, or a single row with
/// no location for a line that received none.
/// </summary>
public sealed record InwardDownloadRow(
    string ReferenceNumber,
    long MaterialNumber,
    string MaterialDescription,
    string MfgMonthLabel,
    decimal RequestedQuantityBoxes,
    decimal AllocatedQuantityBoxes,
    string LineStatus,
    string LocationCode,
    decimal QuantityBoxes,
    string PutawayStatus);

public sealed record InwardPalletChangeOptionDto(
    int Id,
    string LocationCode,
    int LocationSubtypeId,
    string LocationSubtypeCode,
    string LocationSubtypeName,
    int ZoneTypeId,
    string ZoneTypeCode,
    string ZoneTypeName,
    int DistancePriority,
    int CapacityBoxes,
    decimal OccupiedBoxes,
    decimal FreeBoxes,
    bool IsCurrent);
