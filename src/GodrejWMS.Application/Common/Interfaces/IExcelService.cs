using GodrejWMS.Application.Features.Inward.Dtos;
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

    /// <summary>Writes a sample "Format inventory pullout-upload" workbook.</summary>
    byte[] WritePulloutUploadTemplate();

    /// <summary>Parses a "Format inventory pullout-upload": Material Code, Total Stock in CFB.</summary>
    IReadOnlyList<PulloutImportRow> ReadPulloutUpload(Stream fileStream);

    /// <summary>Writes the "Format inventory master" / "Format invent pullout-download" layout.</summary>
    byte[] WriteStockMaster(IReadOnlyList<StockMasterRowDto> rows);

    /// <summary>Writes a pullout download workbook, mirroring "Format invent pullout-download".</summary>
    byte[] WritePulloutDownload(IReadOnlyList<StockMasterRowDto> rows);

    byte[] WriteOperationalReport(string title, IReadOnlyList<(string Header, Func<ReportRow, string> Value)> columns,
        IReadOnlyList<ReportRow> rows);
}

