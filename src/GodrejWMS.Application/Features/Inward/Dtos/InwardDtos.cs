using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Inward.Dtos;

/// <summary>One parsed row from an "inward" Excel upload: Material Code, Total Stock in CFB(qty), Mfg Month.</summary>
public sealed record InwardImportRow(long MaterialCode, decimal QuantityBoxes, string MfgMonthText, int RowNumber);

/// <summary>One worksheet's worth of data from a multi-sheet Unloading Sheet workbook upload (see
/// <see cref="GodrejWMS.Application.Common.Interfaces.IExcelService.ReadInwardWorkbook"/>) - the
/// client's real daily workflow adds one sheet per receipt to the same growing file. Every field is
/// read from a fixed cell/row position (header row 8, LR No at D2, Vehicle No at D3, Received From
/// at F5, an already-assigned GRN Reference Number at E2 if this sheet was already processed), not
/// label-scanned, since this reader assumes the client's own native layout exactly.</summary>
public sealed record InwardSheetUploadData(
    string SheetName,
    bool IsOrderSheet,
    IReadOnlyList<InwardImportRow> Rows,
    string? VehicleNumber,
    string? LrNumber,
    string? ReceivedFrom,
    string? ExistingGrnReference,
    IReadOnlyList<int> RowsMissingPkm);

public enum InwardSheetImportStatus
{
    Created,
    SkippedNotOrderSheet,
    SkippedNoData,
    SkippedMissingPkm,
    SkippedDuplicateReference,
    SkippedDuplicateLrVehicleSource,
    Error
}

/// <summary>What happened for one sheet of an <see cref="GodrejWMS.Application.Features.Inward.Commands.ImportInwardWorkbookCommand"/> upload.</summary>
public sealed record InwardSheetImportOutcome(
    string SheetName,
    InwardSheetImportStatus Status,
    string? ReferenceNumber,
    string Message);

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
    bool IsRejected,
    bool IsPkmless = false);

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
    string PutawayStatus,
    int? PutawayId = null);

/// <summary>One row read back from a GRN allocation workbook for location reassignment.</summary>
public sealed record InwardPutawayReassignmentImportRow(
    string ReferenceNumber,
    long MaterialNumber,
    string MfgMonthLabel,
    decimal RequestedQuantityBoxes,
    decimal AllocatedQuantityBoxes,
    string LocationCode,
    decimal QuantityBoxes,
    string PutawayStatus,
    int? PutawayId,
    int RowNumber);

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
