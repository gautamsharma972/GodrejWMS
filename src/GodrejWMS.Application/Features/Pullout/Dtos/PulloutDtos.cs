using GodrejWMS.Domain.Enums;

namespace GodrejWMS.Application.Features.Pullout.Dtos;

/// <summary>One parsed row from a "GCPL Loading Sheet" upload: Code, Del Qty, an optional,
/// unvalidated Plant Name read once from the sheet's Destination field and applied to every row
/// (the sheet has no per-row destination column), the row's own Priority column, free text, and
/// the row's own Description column - used as a fallback label when the material code isn't in
/// Material Master yet, instead of a generic "(unknown material)" placeholder.</summary>
public sealed record PulloutImportRow(long MaterialCode, decimal QuantityBoxes, int RowNumber, string? PlantName = null, string? Priority = null, string? MaterialDescription = null);

/// <summary>One worksheet's worth of data from a multi-sheet Loading Sheet workbook upload (see
/// <see cref="GodrejWMS.Application.Common.Interfaces.IExcelService.ReadPulloutWorkbook"/>) - the
/// client's real daily workflow adds one sheet per order to the same growing file. Every field is
/// read from a fixed cell/row position (header row 12, Vehicle No at B9, Destination at B10, an
/// already-assigned Pick Reference Number at H10 if this sheet was already pulled before), not
/// label-scanned, since this reader assumes the client's own native layout exactly.</summary>
public sealed record PulloutSheetUploadData(
    string SheetName,
    bool IsOrderSheet,
    IReadOnlyList<PulloutImportRow> Rows,
    PulloutLoadingSheetFieldsDto HeaderFields,
    string? VehicleNumber,
    string? Destination,
    string? ExistingPickReference);

public enum PulloutSheetImportStatus
{
    Created,
    SkippedNotOrderSheet,
    SkippedNoData,
    SkippedDuplicateReference,
    SkippedDuplicateDestinationVehicle,
    Error
}

/// <summary>What happened for one sheet of an <see cref="GodrejWMS.Application.Features.Pullout.Commands.ImportPulloutWorkbookCommand"/> upload.</summary>
public sealed record PulloutSheetImportOutcome(
    string SheetName,
    PulloutSheetImportStatus Status,
    string? ReferenceNumber,
    string Message);

/// <summary>Loading Sheet header fields this app has no domain model of its own for (Transport,
/// Vehicle no, Gate No, Driver No, Plan/Start/End Time, Date, Weight, Volume) - captured verbatim
/// from an uploaded sheet, persisted on the <c>PulloutTransaction</c>, and carried through
/// unchanged to the downloaded Loading Sheet. All free text, never validated.</summary>
public sealed record PulloutLoadingSheetFieldsDto(
    string? TransportName = null,
    string? VehicleNumber = null,
    string? GateNumber = null,
    string? DriverNumber = null,
    string? PlanTime = null,
    string? DateText = null,
    string? StartTime = null,
    string? EndTime = null,
    string? WeightText = null,
    string? VolumeText = null);

/// <summary>One row of the Pullout pick-list download (post-confirmation export) - kept separate
/// from <c>StockMasterRowDto</c> since Plant Name is specific to Pullout, not to stock in general.</summary>
public sealed record PulloutDownloadRowDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal QuantityBoxes,
    string MfgMonthLabel,
    string PalletPositionCode,
    string? PlantName = null);

/// <summary>Sheet-wide header fields for the Loading Sheet tab download: Indent/Date/Destination
/// this app always has, plus whatever <see cref="PulloutLoadingSheetFieldsDto"/> fields were
/// captured on upload (passed through as-is) - null when the pullout wasn't created from an
/// upload, in which case those labels are reproduced but left blank.</summary>
public sealed record PulloutLoadingSheetHeaderDto(
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    string? PlantName,
    PulloutLoadingSheetFieldsDto? Fields = null,
    string? WarehouseAddress = null);

/// <summary>One data row of the Loading Sheet tab: Code/Description/Del Qty/Priority mirror what
/// was requested, same as the original uploaded sheet; Remarks is the one column this app fills
/// in, from the line's allocation outcome (see <see cref="AllocationStatus"/>).</summary>
public sealed record PulloutLoadingSheetRowDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal DelQtyBoxes,
    AllocationStatus Status,
    string? Priority = null);

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
    string? PlantName = null,
    string? Priority = null);

public sealed record PulloutResultDto(
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PulloutLineResultDto> Lines,
    PulloutLoadingSheetFieldsDto? HeaderFields = null,
    string? WarehouseAddress = null);

public sealed record PulloutHistoryDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    int LineCount,
    decimal TotalQuantityBoxes,
    AllocationStatus Status,
    bool IsConfirmed = true,
    bool IsRejected = false,
    string? Destination = null,
    string? VehicleNumber = null);

public sealed record PulloutDetailLineDto(
    long MaterialNumber,
    string MaterialDescription,
    decimal RequestedQuantityBoxes,
    decimal PickedQuantityBoxes,
    AllocationStatus Status,
    string? Remarks,
    IReadOnlyList<PulloutPickDto> Picks,
    string? PlantName = null,
    string? Priority = null);

public sealed record PulloutDetailDto(
    int Id,
    string ReferenceNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PulloutDetailLineDto> Lines,
    bool IsConfirmed = true,
    bool IsRejected = false,
    DateTimeOffset? RejectedAt = null,
    string? RejectedByUserName = null,
    string? RejectionReason = null,
    PulloutLoadingSheetFieldsDto? HeaderFields = null,
    string? WarehouseAddress = null);
