using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using GodrejWMS.Application.Features.Locations.Dtos;
using GodrejWMS.Application.Features.Materials.Dtos;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Application.Features.Reports.Core;
using GodrejWMS.Application.Features.StockMaster.Dtos;

namespace GodrejWMS.Application.Common.Interfaces;

/// <summary>
/// Reads/writes the exact upload and download spreadsheet layouts captured in the source
/// "logic_rackproject.xlsx" workbook (sheets: "Format inventory master", "Format inventory
/// pullout-upload", "Format invent pullout-download", "inward", "MaterialMaster").
/// </summary>
public interface IExcelService
{
    /// <summary>Parses a "MaterialMaster" style upload (Material Number.. Season columns).</summary>
    IReadOnlyList<MaterialImportRow> ReadMaterialMaster(Stream fileStream);

    /// <summary>Writes a sample "MaterialMaster" upload workbook with the expected columns.</summary>
    byte[] WriteMaterialMasterTemplate();

    /// <summary>Writes the Material Master list, including calculated and classification fields.</summary>
    byte[] WriteMaterialMaster(IReadOnlyList<MaterialDto> rows);

    /// <summary>Parses Location Master updates by existing Location Code.</summary>
    IReadOnlyList<LocationImportRow> ReadLocationMaster(Stream fileStream);

    /// <summary>Writes a sample Location Master upload workbook.</summary>
    byte[] WriteLocationMasterTemplate();

    /// <summary>Writes a Location Master export workbook.</summary>
    byte[] WriteLocationMaster(IReadOnlyList<PalletPositionDto> rows);

    /// <summary>Writes a sample Inward upload workbook.</summary>
    byte[] WriteInwardUploadTemplate();

    /// <summary>Parses an "inward" upload: Material Code, Total Stock in CFB(qty), Mfg Month.</summary>
    IReadOnlyList<InwardImportRow> ReadInwardUpload(Stream fileStream);

    /// <summary>Parses every worksheet of a client-native Unloading Sheet workbook (one sheet per
    /// receipt, growing through the day) - fixed layout per sheet: header row 8, data from row 9
    /// until a row's Code column contains "Remark", LR No at D2, Vehicle No at D3, Received From at
    /// F5, an already-assigned GRN Reference Number at E2. A sheet whose row 8 doesn't start with
    /// "Code" (a non-GRN tab, or an unused blank placeholder sheet) is reported with
    /// <see cref="InwardSheetUploadData.IsOrderSheet"/> false rather than treated as an error.</summary>
    IReadOnlyList<InwardSheetUploadData> ReadInwardWorkbook(Stream fileStream);

    /// <summary>Sample workbook for the multi-sheet Inward upload - header row 8, LR No at D2,
    /// Vehicle No at D3, Received From at F5, a blank GRN Reference Number placeholder at E2, and a
    /// "Remark" footer marker - see <see cref="ReadInwardWorkbook"/> for what this layout must match.</summary>
    byte[] WriteInwardWorkbookSampleTemplate();

    /// <summary>Writes a sample "GCPL Loading Sheet" workbook (same layout pullout upload now
    /// expects). <paramref name="plantNames"/> populates the Destination field's dropdown list -
    /// purely a convenience, never validated on import.</summary>
    byte[] WritePulloutUploadTemplate(IReadOnlyList<string> plantNames);

    /// <summary>Parses a "GCPL Loading Sheet" upload: Code and Del Qty from the data table
    /// (header row found by matching "Code" in column A), Destination from the sheet header as a
    /// single Plant Name applied to every row, and the remaining header fields (Transport,
    /// Vehicle no, Gate No, Driver No, Plan/Start/End Time, Date, Weight, Volume) captured
    /// verbatim for pass-through to the eventual download.</summary>
    (IReadOnlyList<PulloutImportRow> Rows, PulloutLoadingSheetFieldsDto HeaderFields, string? IndentText) ReadPulloutUpload(Stream fileStream);

    /// <summary>Same parsing as <see cref="ReadPulloutUpload"/>, but from a block of text pasted
    /// straight out of Excel (tab-separated cells, newline-separated rows) instead of an uploaded
    /// .xlsx file - for pasting a Loading Sheet's header block and data rows copied directly from a
    /// client's own working workbook. A cell containing an embedded line break (e.g. an Indent cell
    /// with both our reference number and the client's own number on separate lines) is recognized
    /// by Excel's own clipboard quoting (the cell wrapped in double quotes) and kept as one cell.</summary>
    (IReadOnlyList<PulloutImportRow> Rows, PulloutLoadingSheetFieldsDto HeaderFields, string? IndentText) ReadPulloutPaste(string pastedText);

    /// <summary>Parses every worksheet of a client-native Loading Sheet workbook (one sheet per
    /// order, growing through the day) - fixed layout per sheet: header row 12, data from row 13
    /// until a row's Code column contains "Driver Sign", Vehicle No at B9, Destination at B10, an
    /// already-assigned Pick Reference Number at H10. A sheet whose row 12 doesn't start with
    /// "Code" (the workbook's own dashboard tabs - MIS/APO/PUSH/Indent - or an unused blank
    /// placeholder sheet) is reported with <see cref="PulloutSheetUploadData.IsOrderSheet"/> false
    /// rather than treated as an error.</summary>
    IReadOnlyList<PulloutSheetUploadData> ReadPulloutWorkbook(Stream fileStream);

    /// <summary>Sample workbook for the multi-sheet upload - header row 12, Vehicle No at B9,
    /// Destination at B10, a blank Pick Reference Number placeholder at H10, and a "Driver Sign"
    /// footer marker - see <see cref="ReadPulloutWorkbook"/> for what this layout must match.</summary>
    byte[] WritePulloutWorkbookSampleTemplate();

    /// <summary>Writes the "Format inventory master" / "Format invent pullout-download" layout.</summary>
    byte[] WriteStockMaster(IReadOnlyList<StockMasterRowDto> rows);

    byte[] WriteLowStockLocations(IReadOnlyList<LowStockLocationDto> rows, IReadOnlyList<MovementReasonDto> reasons);

    IReadOnlyList<LowStockMovementImportRow> ReadLowStockMovements(Stream fileStream);

    /// <summary>Writes a sample Inventory Master upload workbook: the same layout as
    /// <see cref="WriteStockMaster"/>, pre-filled with example rows.</summary>
    byte[] WriteStockMasterTemplate();

    /// <summary>Parses an Inventory Master upload: Material Code, Total Stock in CFB, Mfg Month,
    /// Pallet Position. Directly places/replaces stock at an existing location - unlike the
    /// "inward" upload, it does not run the put-away allocation engine.</summary>
    IReadOnlyList<StockMasterImportRow> ReadStockMaster(Stream fileStream);

    /// <summary>Writes a pullout download workbook: a printable "Pullout Download" Picklist sheet
    /// with transport details and one row per allocated pallet position, plus the existing
    /// "Loading Sheet" tab with Remarks set per line from its allocation outcome.</summary>
    byte[] WritePulloutDownload(
        IReadOnlyList<PulloutDownloadRowDto> rows,
        PulloutLoadingSheetHeaderDto loadingSheetHeader,
        IReadOnlyList<PulloutLoadingSheetRowDto> loadingSheetRows);

    /// <summary>Writes an inward (GRN) allocation workbook: one row per put-away location.</summary>
    byte[] WriteInwardDownload(IReadOnlyList<InwardDownloadRow> rows);

    /// <summary>Reads a previously downloaded GRN allocation workbook after pallet locations were edited.</summary>
    IReadOnlyList<InwardPutawayReassignmentImportRow> ReadInwardPutawayReassignment(Stream fileStream);

    byte[] WriteOperationalReport(string title, IReadOnlyList<(string Header, Func<ReportRow, string> Value)> columns,
        IReadOnlyList<ReportRow> rows);
}
