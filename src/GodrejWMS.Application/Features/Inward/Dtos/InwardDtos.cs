using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Inward.Dtos;

/// <summary>One parsed row from an "inward" Excel upload: Material Code, Total Stock in CFB(qty), Mfg Month.</summary>
public sealed record InwardImportRow(long MaterialCode, decimal QuantityBoxes, string MfgMonthText, int RowNumber, string? Description = null);

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
    IReadOnlyList<int> RowsMissingPkm,
    string? HeaderBlockJson = null,
    IReadOnlyList<InwardUnsavedLineInput>? UnreadableRows = null);

/// <summary>An uploaded sheet row that cannot become a GRN line (its code is not a valid material number) but
/// carries a quantity, so it is recorded against the GRN instead of being dropped silently.</summary>
public sealed record InwardUnsavedLineInput(string CodeText, decimal QuantityBoxes, string Reason, int? SourceRow, string? Description = null);

/// <summary>An uploaded line the GRN could not put away because it was never saved as a line.</summary>
public sealed record InwardUnsavedLineDto(string CodeText, decimal QuantityBoxes, InwardUnsavedLineKind Kind, string Reason, int? SourceRow, string? Description = null, int? MfgMonth = null);

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
    string Message,
    IReadOnlyList<InwardSkippedLine>? SkippedLines = null);

/// <summary>An uploaded row that was dropped (or only partly allocated) while creating the GRN,
/// and why - so the user can see why the GRN total is lower than the sheet total.</summary>
public sealed record InwardSkippedLine(long MaterialNumber, string MfgMonthLabel, decimal RequestedQuantityBoxes, decimal AllocatedQuantityBoxes, string Reason);

public static class InwardSkippedLineMapper
{
    public static List<InwardSkippedLine> From(IEnumerable<InwardLineResultDto> lines) => lines
        .Where(l => l.AllocatedQuantityBoxes < l.RequestedQuantityBoxes)
        .Select(l => new InwardSkippedLine(l.MaterialNumber, l.MfgMonthLabel, l.RequestedQuantityBoxes, l.AllocatedQuantityBoxes,
            l.Remarks ?? "Not fully allocated."))
        .ToList();
}

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

public sealed record InwardAllocationLineDto(string LocationCode, decimal QuantityBoxes, string? Reason = null, bool IsConfirmed = false, int? PutawayId = null, bool IsVirtual = false);

public sealed record InwardResultDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<InwardLineResultDto> Lines,
    bool IsConfirmed = false,
    bool IsRejected = false,
    string? VehicleNumber = null,
    string? LrNumber = null,
    string? UploadHeaderJson = null,
    string? ReceivedFrom = null,
    IReadOnlyList<InwardUnsavedLineDto>? UnsavedLines = null);

public sealed record InwardHistoryDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    int LineCount,
    decimal TotalQuantityBoxes,
    AllocationStatus Status,
    bool IsConfirmed,
    bool IsRejected,
    bool IsPkmless = false,
    string? VehicleNumber = null,
    string? LrNumber = null,
    int LocationCount = 0,
    int SubmittedLocationCount = 0,
    int VirtualLocationCount = 0);

public sealed record InwardPutawayDto(
    string LocationCode,
    decimal QuantityBoxes,
    bool IsConfirmed,
    int Id = 0,
    string? Reason = null,
    string? OverrideReason = null,
    DateTimeOffset? ConfirmedAt = null,
    string? ConfirmedByUserName = null,
    string? ExcludedLocation = null,
    int PalletPositionId = 0,
    bool IsPickerPlaced = false,
    bool IsExcludedLocked = false,
    bool IsVirtual = false);

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
    string? RejectionReason = null,
    string? VehicleNumber = null,
    string? LrNumber = null,
    string? UploadHeaderJson = null,
    string? ReceivedFrom = null,
    IReadOnlyList<InwardUnsavedLineDto>? UnsavedLines = null,
    bool IsPkmless = false,
    DateTimeOffset? PostedAt = null);
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
    int? PutawayId = null,
    string? VehicleNumber = null,
    string? LrNumber = null);

/// <summary>Per-GRN context for the Unloading Sheet: vehicle/LR, the GRN reference, and the first
/// seven rows of the original upload (see <c>InwardUploadHeaderBlock</c>), if there was one.</summary>
public sealed record InwardDownloadHeaderDto(
    string ReferenceNumber,
    string? VehicleNumber,
    string? LrNumber,
    string? UploadHeaderJson);

/// <summary>One uploaded line on the Unloading Sheet, with the system's allocation verdict in
/// <see cref="Remarks"/> ("Fully allocated", "Partially allocated" or "Not allocated").</summary>
public sealed record InwardUnloadingSheetRowDto(
    string Code,
    string MaterialDescription,
    decimal RequestedQuantityBoxes,
    string MfgMonthLabel,
    string Remarks);

/// <summary>Everything the two-sheet GRN workbook needs: sheet 1 (Inward Download) rows, and the
/// header plus per-line remarks for sheet 2 (Unloading Sheet).</summary>
public sealed record InwardDownloadDto(
    IReadOnlyList<InwardDownloadRow> Rows,
    InwardDownloadHeaderDto Header,
    IReadOnlyList<InwardUnloadingSheetRowDto> UnloadingRows);

/// <summary>One row read back from a GRN allocation workbook for location reassignment.</summary>
public sealed record InwardPutawayReassignmentImportRow(
    string? ReferenceNumber,
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
