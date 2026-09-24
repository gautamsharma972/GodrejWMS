using ClosedXML.Excel;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Application.Features.Locations.Dtos;
using GodrejWMS.Application.Features.Materials.Dtos;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Application.Features.Reports.Core;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Infrastructure.Services;

/// <summary>
/// Reads/writes the exact spreadsheet layouts captured in the source "logic_rackproject.xlsx"
/// workbook. Column order follows the reference sheets 1:1 (MaterialMaster / inward /
/// "Format inventory pullout-upload" / "Format inventory master" / "Format invent pullout-download");
/// each reader locates its header row by scanning for a recognizable first-column label so a
/// stray title row above the header (as in the "inward" sheet) doesn't break parsing.
/// </summary>
public class ClosedXmlExcelService : IExcelService
{
    public byte[] WriteOperationalReport(string title,
        IReadOnlyList<(string Header, Func<ReportRow, string> Value)> columns,
        IReadOnlyList<ReportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(title.Length > 31 ? title[..31] : title);
        for (var column = 0; column < columns.Count; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = columns[column].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF2FF");
        }
        for (var row = 0; row < rows.Count; row++)
            for (var column = 0; column < columns.Count; column++)
                sheet.Cell(row + 2, column + 1).Value = columns[column].Value(rows[row]);
        sheet.SheetView.FreezeRows(1);
        sheet.RangeUsed()?.SetAutoFilter();
        sheet.Columns().AdjustToContents(8, 45);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
    public IReadOnlyList<MaterialImportRow> ReadMaterialMaster(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(ws, "Material Number");
        var rows = new List<MaterialImportRow>();

        var row = headerRow + 1;
        while (true)
        {
            var cellA = ws.Cell(row, 1);
            if (cellA.IsEmpty() || !cellA.TryGetValue(out long materialNumber) || materialNumber <= 0)
            {
                break;
            }

            var seasonCode = ParseSeasonCode(ws.Cell(row, 15).GetString());
            var movementTypeCode = ParseMovementTypeCode(ws.Cell(row, 16).GetString());

            rows.Add(new MaterialImportRow(
                MaterialNumber: materialNumber,
                Description: ws.Cell(row, 2).GetString().Trim(),
                DesignType: ws.Cell(row, 3).GetString().Trim(),
                CharacteristicValue: NullIfEmpty(ws.Cell(row, 6).GetString()),
                PackSize: (int)ws.Cell(row, 4).GetValue<double>(),
                MrpPrice: (decimal)ws.Cell(row, 5).GetValue<double>(),
                LengthMm: (decimal)ws.Cell(row, 7).GetValue<double>(),
                WidthMm: (decimal)ws.Cell(row, 8).GetValue<double>(),
                HeightMm: (decimal)ws.Cell(row, 9).GetValue<double>(),
                NetWeightKg: (decimal)ws.Cell(row, 11).GetValue<double>(),
                GrossWeightKg: (decimal)ws.Cell(row, 12).GetValue<double>(),
                PalletCapacityBoxes: (int)ws.Cell(row, 13).GetValue<double>(),
                MovementTypeCode: movementTypeCode,
                SeasonCode: seasonCode,
                RowNumber: row));

            row++;
        }

        return rows;
    }

    public byte[] WriteMaterialMasterTemplate()
    {
        string[] headers =
        [
            "Material Number",
            "Material Description",
            "Design Type",
            "PK Size",
            "MRP Price",
            "Characteristic Value",
            "Length",
            "Width",
            "Hight",
            "Volume",
            "Net Weight",
            "Gross Weight",
            "Pallet Size",
            "Weight (KG)",
            "Season",
            "Velocity"
        ];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("MaterialMaster");

        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        var samples = new object[][]
        {
            [40034354, "HIT CIK 125ML MRP75 P90", "XOLDH", 90, 75, "SHAC1", 50, 29.3, 34, 49810, 0.08, 0.152, 40, 6.08, "Rainy", "FastMoving"],
            [40063045, "HIT FIK 200ML M125P60 CAN .18MM MFT NEW", "XOLDI", 90, 125, "SHAF2", 55.5, 33.5, 23.7, 44040.225, 0.124, 0.207, 40, 8.28, "Summer", "SlowMoving"],
            [50010001, "GODREJ SOAP 100G CARTON", "SOAPBOX", 72, 45, "SOAP100", 42, 31, 26, 33852, 0.09, 0.12, 40, 4.8, "Winter", "SlowMoving"]
        };

        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(samples[r][c]);
            }
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<LocationImportRow> ReadLocationMaster(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(ws, "Location Code");
        var rows = new List<LocationImportRow>();

        var row = headerRow + 1;
        while (true)
        {
            var locationCode = ws.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(locationCode))
            {
                break;
            }

            rows.Add(new LocationImportRow(
                LocationCode: locationCode,
                FlatLabel: NullIfEmpty(ws.Cell(row, 2).GetString()),
                LocationTypeCode: NullIfEmpty(ws.Cell(row, 5).GetString()) ?? LocationTypeCodes.Rack,
                LocationSubtypeCode: NullIfEmpty(ws.Cell(row, 6).GetString()) ?? LocationSubtypeCodes.Good,
                ZoneTypeCode: ParseZoneTypeCode(ws.Cell(row, 7).GetString()),
                DistancePriority: ReadInt(ws.Cell(row, 8), 100),
                MaxPallets: ReadInt(ws.Cell(row, 9), 2),
                BoxesPerPallet: ReadInt(ws.Cell(row, 10), 40),
                IsActive: ParseActive(ws.Cell(row, 12).GetString()),
                RowNumber: row));

            row++;
        }

        return rows;
    }

    public byte[] WriteLocationMasterTemplate()
    {
        string[] headers =
        [
            "Location Code",
            "Flat Label",
            "Level",
            "Column",
            "Location Type",
            "Location Subtype",
            "Zone Type",
            "Distance Priority",
            "Max Pallets",
            "Boxes Per Pallet",
            "Capacity Boxes",
            "Active"
        ];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Location Master");
        WriteHeader(ws, headers);

        var samples = new object[][]
        {
            ["A-01-01", "A1", "Ground", 1, "Rack", "Good", "Fast", 1, 2, 40, 80, "Active"],
            ["A-02-01", "A2", "Ground", 2, "Rack", "Hold", "DispatchNear", 2, 2, 40, 80, "Inactive"]
        };

        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(samples[r][c]);
            }
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] WriteLocationMaster(IReadOnlyList<PalletPositionDto> rows)
    {
        string[] headers =
        [
            "Location Code",
            "Flat Label",
            "Level",
            "Column",
            "Location Type",
            "Location Subtype",
            "Zone Type",
            "Distance Priority",
            "Max Pallets",
            "Boxes Per Pallet",
            "Capacity Boxes",
            "Active",
            "Occupied Boxes",
            "Free Boxes"
        ];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Location Master");
        WriteHeader(ws, headers);

        var rowNum = 2;
        foreach (var item in rows)
        {
            ws.Cell(rowNum, 1).Value = item.LocationCode;
            ws.Cell(rowNum, 2).Value = item.FlatLabel;
            ws.Cell(rowNum, 3).Value = item.Level == 1 ? "Ground" : (item.Level - 1).ToString();
            ws.Cell(rowNum, 4).Value = item.Column;
            ws.Cell(rowNum, 5).Value = item.LocationTypeCode;
            ws.Cell(rowNum, 6).Value = item.LocationSubtypeCode;
            ws.Cell(rowNum, 7).Value = item.ZoneTypeCode;
            ws.Cell(rowNum, 8).Value = item.DistancePriority;
            ws.Cell(rowNum, 9).Value = item.MaxPallets;
            ws.Cell(rowNum, 10).Value = item.BoxesPerPallet;
            ws.Cell(rowNum, 11).Value = item.CapacityBoxes;
            ws.Cell(rowNum, 12).Value = item.IsActive ? "Active" : "Inactive";
            ws.Cell(rowNum, 13).Value = item.OccupiedBoxes;
            ws.Cell(rowNum, 14).Value = item.FreeBoxes;
            rowNum++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] WriteInwardUploadTemplate()
    {
        string[] headers = ["Material Code", "Total Stock in CFB(qty)", "Mfg Year", "Mfg Month"];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Inward Upload");
        WriteHeader(ws, headers);

        var samples = new object[][]
        {
            [40000015, 0.008, 2010, "MAR"],
            [40000015, 0.008, 2010, "APR"],
            [40000015, 0.008, 2010, "MAY"]
        };

        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(samples[r][c]);
            }
        }

        ws.Column(1).Style.NumberFormat.Format = "0";
        ws.Column(2).Style.NumberFormat.Format = "0.000";
        ws.Column(3).Style.NumberFormat.Format = "0";

        // A dropdown restricted to the same MMM codes MfgMonthParser accepts, covering the sample
        // rows plus plenty of room for the user's own rows below them. An inline list validation's
        // formula must be a literal quoted string ("JAN,FEB,...") - without the surrounding quotes,
        // Excel treats the unquoted text as a range/name reference it can't resolve, and the
        // dropdown renders with no items instead of erroring.
        const int lastMonthDropdownRow = 200;
        ws.Range(2, 4, lastMonthDropdownRow, 4)
            .CreateDataValidation()
            .List("\"JAN,FEB,MAR,APR,MAY,JUN,JUL,AUG,SEP,OCT,NOV,DEC\"", true);

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<InwardImportRow> ReadInwardUpload(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(ws, "Material Code");
        var rows = new List<InwardImportRow>();

        var row = headerRow + 1;
        while (true)
        {
            var cellA = ws.Cell(row, 1);
            if (cellA.IsEmpty() || !cellA.TryGetValue(out long materialCode) || materialCode <= 0)
            {
                break;
            }

            var mfgYear = (int)ws.Cell(row, 3).GetValue<double>();
            var mfgMonth = ws.Cell(row, 4).GetString().Trim();

            rows.Add(new InwardImportRow(
                MaterialCode: materialCode,
                QuantityBoxes: (decimal)ws.Cell(row, 2).GetValue<double>(),
                MfgMonthText: $"{mfgMonth}|{mfgYear}",
                RowNumber: row));

            row++;
        }

        return rows;
    }

    public byte[] WritePulloutUploadTemplate()
    {
        string[] headers = ["Material Code", "Total Stock in CFB"];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Pullout Upload");
        WriteHeader(ws, headers);

        var samples = new object[][]
        {
            [40034354, 40],
            [40058047, 20],
            [50010001, 10]
        };

        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(samples[r][c]);
            }
        }

        ws.Column(1).Style.NumberFormat.Format = "0";
        ws.Column(2).Style.NumberFormat.Format = "0.###";
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
    public IReadOnlyList<PulloutImportRow> ReadPulloutUpload(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(ws, "Material Code");
        var rows = new List<PulloutImportRow>();

        var row = headerRow + 1;
        while (true)
        {
            var cellA = ws.Cell(row, 1);
            if (cellA.IsEmpty() || !cellA.TryGetValue(out long materialCode) || materialCode <= 0)
            {
                break;
            }

            rows.Add(new PulloutImportRow(
                MaterialCode: materialCode,
                QuantityBoxes: (decimal)ws.Cell(row, 2).GetValue<double>(),
                RowNumber: row));

            row++;
        }

        return rows;
    }

    public byte[] WriteStockMaster(IReadOnlyList<StockMasterRowDto> rows)
    {
        string[] headers = ["Material Code", "Material Desc.", "Design Type", "Velocity", "Season", "Active", "Total Stock in CFB", "Mfg Month", "Pallet Position"];

        return WriteRows("Inventory Master", headers, rows, (ws, r, i) =>
        {
            ws.Cell(r, 1).Value = i.MaterialNumber;
            ws.Cell(r, 2).Value = i.MaterialDescription;
            ws.Cell(r, 3).Value = i.DesignType;
            ws.Cell(r, 4).Value = i.MovementTypeCode;
            ws.Cell(r, 5).Value = i.SeasonCode;
            ws.Cell(r, 6).Value = i.IsActive ? "Active" : "Inactive";
            ws.Cell(r, 7).Value = i.QuantityBoxes;
            ws.Cell(r, 8).Value = i.MfgMonthLabel;
            ws.Cell(r, 9).Value = i.PalletPositionCode;
        });
    }

    public byte[] WritePulloutDownload(IReadOnlyList<StockMasterRowDto> rows)
    {
        string[] headers = ["Material Code", "Material Desc.", "Total Stock in CFB", "Mfg Month", "Pallet Position"];

        return WriteRows("Pullout Download", headers, rows, (ws, r, i) =>
        {
            ws.Cell(r, 1).Value = i.MaterialNumber;
            ws.Cell(r, 2).Value = i.MaterialDescription;
            ws.Cell(r, 3).Value = i.QuantityBoxes;
            ws.Cell(r, 4).Value = i.MfgMonthLabel;
            ws.Cell(r, 5).Value = i.PalletPositionCode;
        });
    }

    private static byte[] WriteRows(
        string sheetName,
        string[] headers,
        IReadOnlyList<StockMasterRowDto> rows,
        Action<IXLWorksheet, int, StockMasterRowDto> writeRow)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(sheetName);

        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }

        var rowNum = 2;
        foreach (var item in rows)
        {
            writeRow(ws, rowNum, item);
            rowNum++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteHeader(IXLWorksheet ws, string[] headers)
    {
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }
    }

    /// <summary>
    /// Opening a workbook can throw exceptions from deep inside ClosedXML's OpenXML parser -
    /// including a raw <see cref="NullReferenceException"/> on files it can't fully handle (a
    /// corrupted download, a .xls file renamed to .xlsx, one saved by software ClosedXML doesn't
    /// support). Every upload page's Razor catch surfaces ex.Message directly to the user, so
    /// leaving that uncaught would show "Object reference not set to an instance of an object."
    /// instead of something actionable.
    /// </summary>
    private static XLWorkbook LoadWorkbook(Stream fileStream)
    {
        try
        {
            return new XLWorkbook(fileStream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidOperationException(
                "Unable to read this Excel file. Make sure it's a valid, unmodified .xlsx file - " +
                "not .xls, not a renamed or corrupted file. Downloading and using the Sample Format " +
                "template usually resolves this.", ex);
        }
    }

    private static int FindHeaderRow(IXLWorksheet ws, string firstColumnLabelContains)
    {
        for (var row = 1; row <= 10; row++)
        {
            var text = ws.Cell(row, 1).GetString();
            if (text.Contains(firstColumnLabelContains, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        // Fall back to row 1 (a plain data-only upload with no header row).
        return 1;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ParseSeasonCode(string text)
    {
        var normalized = text.Trim();
        return normalized switch
        {
            _ when normalized.Equals(SeasonCodes.Summer, StringComparison.OrdinalIgnoreCase) => SeasonCodes.Summer,
            _ when normalized.Equals(SeasonCodes.Winter, StringComparison.OrdinalIgnoreCase) => SeasonCodes.Winter,
            _ => SeasonCodes.Rainy
        };
    }

    private static int ReadInt(IXLCell cell, int fallback) =>
        cell.TryGetValue(out int value) ? value : fallback;

    private static bool ParseActive(string text)
    {
        var normalized = text.Trim();
        return normalized.Equals("Active", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Yes", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("True", StringComparison.OrdinalIgnoreCase)
            || normalized == "1";
    }

    private static string ParseMovementTypeCode(string text)
    {
        var normalized = text.Trim().Replace("-", string.Empty).Replace(" ", string.Empty);
        return normalized.Equals(SkuMovementTypeCodes.FastMoving, StringComparison.OrdinalIgnoreCase)
            ? SkuMovementTypeCodes.FastMoving
            : SkuMovementTypeCodes.SlowMoving;
    }

    private static string ParseZoneTypeCode(string text)
    {
        var normalized = text.Trim().Replace("-", string.Empty).Replace(" ", string.Empty);
        return normalized switch
        {
            _ when normalized.Equals(ZoneTypeCodes.Fast, StringComparison.OrdinalIgnoreCase) => ZoneTypeCodes.Fast,
            _ when normalized.Equals(ZoneTypeCodes.Seasonal, StringComparison.OrdinalIgnoreCase) => ZoneTypeCodes.Seasonal,
            _ when normalized.Equals(ZoneTypeCodes.DispatchNear, StringComparison.OrdinalIgnoreCase) => ZoneTypeCodes.DispatchNear,
            _ => ZoneTypeCodes.Reserve
        };
    }
}
