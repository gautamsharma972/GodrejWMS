using ClosedXML.Excel;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Application.Features.InventoryMovement.Dtos;
using GodrejWMS.Application.Features.Locations.Dtos;
using GodrejWMS.Application.Features.Materials.Dtos;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Application.Features.Racks.Dtos;
using GodrejWMS.Application.Features.Reports.Core;
using GodrejWMS.Application.Features.StockMaster.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;

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
    public byte[] WriteLowStockLocations(IReadOnlyList<LowStockLocationDto> rows, IReadOnlyList<MovementReasonDto> reasons)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Low Stock Locations");
        string[] headers = ["Material Code", "Material Description", "PKM", "Source Location", "Box Qty", "MRP", "Destination Location", "Move Quantity", "Reason Code"];
        WriteHeader(ws, headers);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = i + 2;
            var item = rows[i];
            ws.Cell(row, 1).Value = item.MaterialNumber.ToString();
            ws.Cell(row, 1).Style.NumberFormat.Format = "@";
            ws.Cell(row, 2).Value = item.MaterialDescription;
            ws.Cell(row, 3).Value = item.MfgMonthLabel;
            ws.Cell(row, 4).Value = item.LocationCode;
            ws.Cell(row, 5).Value = item.QuantityBoxes;
            ws.Cell(row, 6).Value = item.MrpPrice;
            ws.Cell(row, 5).Style.NumberFormat.Format = "0.###";
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
        }
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns(1, 9).AdjustToContents(10, 45);
        double[] minimumWidths = [18, 34, 14, 18, 12, 12, 22, 16, 22];
        for (var column = 1; column <= minimumWidths.Length; column++)
            ws.Column(column).Width = Math.Max(ws.Column(column).Width, minimumWidths[column - 1]);
        ws.Range(2, 7, Math.Max(2, rows.Count + 1), 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF7D6");

        var reasonsSheet = workbook.Worksheets.Add("Movement Reasons");
        WriteHeader(reasonsSheet, ["Reason Code", "Description"]);
        var activeReasons = reasons.Where(reason => reason.IsActive).OrderBy(reason => reason.DisplayName).ToList();
        for (var i = 0; i < activeReasons.Count; i++)
        {
            reasonsSheet.Cell(i + 2, 1).Value = activeReasons[i].Code;
            reasonsSheet.Cell(i + 2, 2).Value = activeReasons[i].DisplayName;
        }
        if (activeReasons.Count > 0 && rows.Count > 0)
        {
            reasonsSheet.Range(2, 1, activeReasons.Count + 1, 1)
                .AddToNamed("LowStockMovementReasonCodes", XLScope.Workbook);
            var reasonValidation = ws.Range(2, 9, rows.Count + 1, 9).CreateDataValidation();
            reasonValidation.List("LowStockMovementReasonCodes");
            reasonValidation.IgnoreBlanks = true;
            reasonValidation.InCellDropdown = true;
            reasonValidation.ShowErrorMessage = true;
            reasonValidation.ErrorTitle = "Invalid reason code";
            reasonValidation.ErrorMessage = "Select an active reason code from the dropdown.";
        }
        reasonsSheet.SheetView.FreezeRows(1);
        reasonsSheet.RangeUsed()?.SetAutoFilter();
        reasonsSheet.Columns(1, 2).AdjustToContents(12, 45);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<LowStockMovementImportRow> ReadLowStockMovements(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();
        var headerRow = FindHeaderRow(ws, "Material Code");
        var rows = new List<LowStockMovementImportRow>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            if (ws.Cell(row, 1).IsEmpty() && ws.Cell(row, 7).IsEmpty()) continue;
            if (!ws.Cell(row, 1).TryGetValue(out long materialNumber) || materialNumber <= 0)
                throw new InvalidOperationException($"Row {row}: Material Code is required.");
            var destination = ws.Cell(row, 7).GetString().Trim();
            if (destination.Length == 0) continue;
            var quantity = ReadDecimal(ws.Cell(row, 8));
            if (quantity <= 0) throw new InvalidOperationException($"Row {row}: Move Quantity must be greater than zero.");
            var reason = ws.Cell(row, 9).GetString().Trim();
            if (reason.Length == 0) throw new InvalidOperationException($"Row {row}: Reason Code is required.");
            rows.Add(new LowStockMovementImportRow(
                materialNumber,
                ws.Cell(row, 3).GetString().Trim(),
                ws.Cell(row, 4).GetString().Trim(),
                destination,
                quantity,
                reason,
                row));
        }
        if (rows.Count == 0) throw new InvalidOperationException("No movement rows were found. Enter Destination Location, Move Quantity, and Reason Code.");
        return rows;
    }

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
                PackSize: (int)ReadDecimal(ws.Cell(row, 4)),
                MrpPrice: ReadDecimal(ws.Cell(row, 5)),
                LengthMm: ReadDecimal(ws.Cell(row, 7)),
                WidthMm: ReadDecimal(ws.Cell(row, 8)),
                HeightMm: ReadDecimal(ws.Cell(row, 9)),
                NetWeightKg: ReadDecimal(ws.Cell(row, 11)),
                GrossWeightKg: ReadDecimal(ws.Cell(row, 12)),
                PalletCapacityBoxes: (int)ReadDecimal(ws.Cell(row, 13)),
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

    public byte[] WriteMaterialMaster(IReadOnlyList<MaterialDto> rows)
    {
        string[] headers =
        [
            "Material Code", "Description", "Design Type", "Characteristic Value", "Pack Size",
            "MRP", "Velocity", "Length (mm)", "Width (mm)", "Height (mm)", "Volume (mm3)",
            "Net Weight (kg)", "Gross Weight (kg)", "Box Weight (kg)", "Pallet Size",
            "Pallet Weight (kg)", "Preferred Zone", "Season", "Status"
        ];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Material Master");
        WriteHeader(ws, headers);

        var rowNumber = 2;
        foreach (var item in rows)
        {
            ws.Cell(rowNumber, 1).Value = item.MaterialNumber;
            ws.Cell(rowNumber, 2).Value = item.Description;
            ws.Cell(rowNumber, 3).Value = item.DesignType;
            ws.Cell(rowNumber, 4).Value = item.CharacteristicValue ?? string.Empty;
            ws.Cell(rowNumber, 5).Value = item.PackSize;
            ws.Cell(rowNumber, 6).Value = item.MrpPrice;
            ws.Cell(rowNumber, 7).Value = item.MovementTypeName;
            ws.Cell(rowNumber, 8).Value = item.LengthMm;
            ws.Cell(rowNumber, 9).Value = item.WidthMm;
            ws.Cell(rowNumber, 10).Value = item.HeightMm;
            ws.Cell(rowNumber, 11).Value = item.VolumeMm3;
            ws.Cell(rowNumber, 12).Value = item.NetWeightKg;
            ws.Cell(rowNumber, 13).Value = item.GrossWeightKg;
            ws.Cell(rowNumber, 14).Value = item.BoxWeightKg;
            ws.Cell(rowNumber, 15).Value = item.PalletCapacityBoxes;
            ws.Cell(rowNumber, 16).Value = item.PalletWeightKg;
            ws.Cell(rowNumber, 17).Value = item.PreferredZoneTypeName is null
                ? "No preference"
                : item.PreferredZoneTypeName + (item.RequirePreferredZone ? " (required)" : string.Empty);
            ws.Cell(rowNumber, 18).Value = item.SeasonName;
            ws.Cell(rowNumber, 19).Value = item.IsActive ? "Active" : "Inactive";
            rowNumber++;
        }

        ws.Column(1).Style.NumberFormat.Format = "0";
        ws.Column(6).Style.NumberFormat.Format = "0.00";
        ws.Columns(8, 14).Style.NumberFormat.Format = "0.000";
        ws.Column(16).Style.NumberFormat.Format = "0.000";
        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents(8, 45);

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
                IsActive: ParseActive(ws.Cell(row, 10).GetString()),
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
            "Active"
        ];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Location Master");
        WriteHeader(ws, headers);

        var samples = new object[][]
        {
            ["A-01-01", "A1", "Ground", 1, "Rack", "Good", "Fast", 1, 2, "Active"],
            ["A-02-01", "A2", "Ground", 2, "Rack", "Hold", "DispatchNear", 2, 2, "Inactive"]
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
            "Active",
            "Occupied Boxes",
            "Capacity Boxes",
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
            ws.Cell(rowNum, 10).Value = item.IsActive ? "Active" : "Inactive";
            ws.Cell(rowNum, 11).Value = item.OccupiedBoxes;
            // Capacity depends on whichever material occupies this location (a position only
            // ever holds one material at a time); blank when the location is empty.
            ws.Cell(rowNum, 12).Value = XLCellValue.FromObject(item.CapacityBoxes);
            ws.Cell(rowNum, 13).Value = XLCellValue.FromObject(item.FreeBoxes);
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
            [40000015, 8, 2010, "MAR"],
            [40000015, 12, 2010, "APR"],
            [40000015, 16, 2010, "MAY"]
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
        var errors = new List<string>();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;

        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var materialCell = ws.Cell(row, 1);
            var quantityCell = ws.Cell(row, 2);
            var yearCell = ws.Cell(row, 3);
            var monthCell = ws.Cell(row, 4);

            if (materialCell.IsEmpty() && quantityCell.IsEmpty() && yearCell.IsEmpty() && monthCell.IsEmpty())
            {
                continue;
            }

            var rowErrors = new List<string>();
            if (!materialCell.TryGetValue(out long materialCode) || materialCode <= 0)
                rowErrors.Add("Material Code must be a positive whole number");

            if (!quantityCell.TryGetValue(out decimal quantityBoxes) || quantityBoxes <= 0)
                rowErrors.Add("Total Stock in CFB(qty) must be greater than zero");
            else if (!decimal.IsInteger(quantityBoxes))
                rowErrors.Add("Total Stock in CFB(qty) must be a whole number (no decimals)");

            if (!yearCell.TryGetValue(out int mfgYear) || mfgYear is < 1900 or > 9999)
                rowErrors.Add("Mfg Year must be a valid four-digit year");

            var mfgMonth = monthCell.GetString().Trim();
            var mfgMonthText = $"{mfgMonth}|{mfgYear}";
            if (string.IsNullOrWhiteSpace(mfgMonth) || !MfgMonthParser.TryParse(mfgMonthText, out _))
                rowErrors.Add("Mfg Month must be JAN through DEC");

            if (rowErrors.Count > 0)
            {
                errors.Add($"Row {row}: {string.Join("; ", rowErrors)}.");
                continue;
            }

            rows.Add(new InwardImportRow(
                MaterialCode: materialCode,
                QuantityBoxes: quantityBoxes,
                MfgMonthText: mfgMonthText,
                RowNumber: row));
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"The inward workbook contains invalid data. {string.Join(" ", errors.Take(10))}");

        if (rows.Count == 0)
            throw new InvalidOperationException(
                "No inward rows were found. Enter data below the header using Material Code, " +
                "a whole-number box quantity, Mfg Year, and Mfg Month.");

        return rows;
    }

    public byte[] WritePulloutUploadTemplate(IReadOnlyList<string> plantNames)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Loading Sheet");

        var samples = new (long Code, string Description, decimal DelQty)[]
        {
            (40034354, "Sample material A", 40),
            (40058047, "Sample material B", 20),
            (50010001, "Sample material C", 10)
        };

        BuildLoadingSheetLayout(ws, indent: null, date: null, destination: null, fields: null, dataRowCount: samples.Length);

        for (var i = 0; i < samples.Length; i++)
        {
            WriteLoadingSheetDataRow(ws, LoadingSheetFirstDataRow + i, samples[i].Code, samples[i].Description, samples[i].DelQty, priority: null, remarks: null);
        }

        // Destination is a convenience dropdown only - never validated on import (see
        // ReadPulloutUpload/SubmitPullout). An inline list formula is capped at 255 characters by
        // Excel, which a few dozen plant names can exceed, so the list lives on a hidden helper
        // sheet instead and the validation references that range.
        if (plantNames.Count > 0)
        {
            var lookupSheet = workbook.Worksheets.Add("PlantNameLookup");
            for (var i = 0; i < plantNames.Count; i++)
            {
                lookupSheet.Cell(i + 1, 1).Value = plantNames[i];
            }
            lookupSheet.Visibility = XLWorksheetVisibility.VeryHidden;

            ws.Cell(7, 2).CreateDataValidation().List(lookupSheet.Range(1, 1, plantNames.Count, 1), true);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public (IReadOnlyList<PulloutImportRow> Rows, PulloutLoadingSheetFieldsDto HeaderFields) ReadPulloutUpload(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(ws, "Code", maxRow: 20);
        var plantName = FindLabeledValue(ws, headerRow, "Destination");
        var headerFields = new PulloutLoadingSheetFieldsDto(
            TransportName: FindLabeledValue(ws, headerRow, "Transport"),
            VehicleNumber: FindLabeledValue(ws, headerRow, "Vehicle no"),
            GateNumber: FindLabeledValueBelow(ws, headerRow, "GATE NO"),
            DriverNumber: FindLabeledValue(ws, headerRow, "Driver No"),
            PlanTime: FindLabeledValue(ws, headerRow, "Plan Time:"),
            DateText: FindLabeledValue(ws, headerRow, "Date"),
            StartTime: FindLabeledValue(ws, headerRow, "Start Time"),
            EndTime: FindLabeledValue(ws, headerRow, "End Time"),
            WeightText: FindTextStartingWith(ws, headerRow, "Weight"),
            VolumeText: FindTextStartingWith(ws, headerRow, "Volume"));

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
                QuantityBoxes: (decimal)ws.Cell(row, 3).GetValue<double>(),
                RowNumber: row,
                PlantName: plantName,
                Priority: NullIfEmpty(ws.Cell(row, 4).GetString()),
                MaterialDescription: NullIfEmpty(ws.Cell(row, 2).GetString())));

            row++;
        }

        return (rows, headerFields);
    }

    public byte[] WritePulloutDownload(
        IReadOnlyList<PulloutDownloadRowDto> rows,
        PulloutLoadingSheetHeaderDto loadingSheetHeader,
        IReadOnlyList<PulloutLoadingSheetRowDto> loadingSheetRows)
    {
        using var workbook = new XLWorkbook();

        // Sheet 1: printable Picklist layout with the transport header followed by one row per
        // allocated pallet position. The underlying pick data and column order stay unchanged.
        var downloadSheet = workbook.Worksheets.Add("Pullout Download");
        BuildPicklistLayout(downloadSheet, loadingSheetHeader, rows.Count);
        var rowNum = PicklistFirstDataRow;
        foreach (var i in rows)
        {
            downloadSheet.Cell(rowNum, 1).Value = i.MaterialNumber.ToString();
            downloadSheet.Cell(rowNum, 1).Style.NumberFormat.Format = "@";
            downloadSheet.Cell(rowNum, 2).Value = i.MaterialDescription;
            downloadSheet.Cell(rowNum, 3).Value = i.QuantityBoxes;
            downloadSheet.Cell(rowNum, 3).Style.NumberFormat.Format = "0.###";
            downloadSheet.Cell(rowNum, 4).Value = i.MfgMonthLabel;
            downloadSheet.Cell(rowNum, 5).Value = i.PalletPositionCode;
            downloadSheet.Cell(rowNum, 6).Value = i.PlantName;
            rowNum++;
        }

        // Sheet 2: the GCPL Loading Sheet layout, Code/Description/Del Qty as requested and
        // Remarks set from each line's allocation outcome - every other data column (Priority,
        // MRP, PKD, Load Qty) stays blank, same as the original uploaded sheet.
        var loadingSheet = workbook.Worksheets.Add("Loading Sheet");
        BuildLoadingSheetLayout(loadingSheet,
            indent: loadingSheetHeader.ReferenceNumber,
            date: loadingSheetHeader.CreatedAt,
            destination: loadingSheetHeader.PlantName,
            fields: loadingSheetHeader.Fields,
            address: loadingSheetHeader.WarehouseAddress,
            dataRowCount: loadingSheetRows.Count);

        for (var i = 0; i < loadingSheetRows.Count; i++)
        {
            var line = loadingSheetRows[i];
            WriteLoadingSheetDataRow(loadingSheet, LoadingSheetFirstDataRow + i,
                line.MaterialNumber, line.MaterialDescription, line.DelQtyBoxes, line.Priority, DescribeAvailability(line.Status));
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private const int PicklistFirstDataRow = 11;

    private static void BuildPicklistLayout(
        IXLWorksheet ws,
        PulloutLoadingSheetHeaderDto header,
        int dataRowCount)
    {
        ws.ShowGridLines = false;
        ws.Column(1).Width = 17;
        ws.Column(2).Width = 52;
        ws.Column(3).Width = 21;
        ws.Column(4).Width = 13;
        ws.Column(5).Width = 17;
        ws.Column(6).Width = 15;
        ws.Column(7).Width = 13;
        ws.Column(8).Width = 13;

        var lastRow = Math.Max(PicklistFirstDataRow, PicklistFirstDataRow + dataRowCount - 1);
        var borderedRange = ws.Range(2, 1, lastRow, 8);
        borderedRange.Style.Font.FontName = "Arial";
        borderedRange.Style.Font.FontSize = 10;
        borderedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        borderedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        borderedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        borderedRange.Style.Border.OutsideBorderColor = XLColor.Black;
        borderedRange.Style.Border.InsideBorderColor = XLColor.Black;

        ws.Range("A2:H2").Merge();
        ws.Cell("A2").Value = "GODREJ CONSUMER PRODUCTS LTD";
        ws.Range("A3:H3").Merge();
        ws.Cell("A3").Value = string.IsNullOrWhiteSpace(header.WarehouseAddress) ? "NA" : header.WarehouseAddress;
        ws.Range("A4:H4").Merge();
        ws.Cell("A4").Value = "Picklist";

        ws.Range("A2:H4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A2:H4").Style.Font.Bold = true;
        ws.Cell("A4").Style.Font.FontSize = 15;

        void Label(string cell, string text)
        {
            ws.Cell(cell).Value = text;
            ws.Cell(cell).Style.Font.Bold = true;
        }

        ws.Range("B5:C5").Merge();
        ws.Range("B6:C6").Merge();
        ws.Range("B7:C7").Merge();
        ws.Range("B8:C8").Merge();
        ws.Range("G5:H5").Merge();
        ws.Range("G6:H6").Merge();
        ws.Range("G7:H7").Merge();
        ws.Range("G8:H8").Merge();

        Label("A5", "Transport");
        Label("D5", "GATE NO:");
        Label("F5", "Plan Time:");
        Label("A6", "Vehicle no");
        Label("F6", "Date");
        Label("A7", "Destination");
        Label("F7", "Start Time");
        Label("A8", "Driver No");
        Label("F8", "End Time");

        var fields = header.Fields;
        void SetText(string cell, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) ws.Cell(cell).Value = value;
        }

        SetText("B5", fields?.TransportName);
        SetText("E5", fields?.GateNumber);
        SetText("G5", fields?.PlanTime);
        SetText("B6", fields?.VehicleNumber);
        if (!string.IsNullOrWhiteSpace(fields?.DateText))
        {
            ws.Cell("G6").Value = fields.DateText;
        }
        else
        {
            ws.Cell("G6").Value = header.CreatedAt.ToString("dd.MM.yyyy");
        }
        SetText("B7", header.PlantName);
        SetText("G7", fields?.StartTime);
        SetText("B8", fields?.DriverNumber);
        SetText("G8", fields?.EndTime);

        string[] columnHeaders =
            ["Material Code", "Material Desc.", "Total Stock in CFB", "Mfg Month", "Pallet Position", "Plant Name"];
        for (var column = 0; column < columnHeaders.Length; column++)
        {
            ws.Cell(10, column + 1).Value = columnHeaders[column];
        }

        ws.Range("A10:H10").Style.Font.Bold = true;
        ws.Range("A10:H10").Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
        ws.Range("A10:H10").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        ws.Row(1).Height = 10;
        ws.Row(2).Height = 22;
        ws.Row(3).Height = 22;
        ws.Row(4).Height = 30;
        for (var row = 5; row <= 10; row++) ws.Row(row).Height = 22;
        for (var row = PicklistFirstDataRow; row <= lastRow; row++) ws.Row(row).Height = 22;

        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.SetRowsToRepeatAtTop(2, 8);
        ws.PageSetup.Margins.Top = 0.25;
        ws.PageSetup.Margins.Bottom = 0.25;
        ws.PageSetup.Margins.Left = 0.25;
        ws.PageSetup.Margins.Right = 0.25;
    }

    private static string DescribeAvailability(AllocationStatus status) => status switch
    {
        AllocationStatus.Fulfilled => "Available",
        AllocationStatus.Partial => "Partial Available",
        _ => "Not available"
    };

    private const int LoadingSheetFirstDataRow = 12;

    /// <summary>Reproduces the GCPL Loading Sheet's exact static design (merges, borders, fonts,
    /// column widths, section labels) - everything except the data table rows, which the caller
    /// fills in separately via <see cref="WriteLoadingSheetDataRow"/> since the row count varies
    /// per pullout. <paramref name="fields"/> carries through whatever was captured from the
    /// original upload (Transport, Vehicle no, Driver No, Gate No, Plan/Start/End Time, Weight,
    /// Volume); when null (no upload, e.g. the blank sample template), those labels are reproduced
    /// but left blank for manual fill-in. <paramref name="address"/> comes from the pullout's
    /// Warehouse master Address field - "NA" when that warehouse has none set.</summary>
    private static void BuildLoadingSheetLayout(IXLWorksheet ws, string? indent, DateTimeOffset? date, string? destination, PulloutLoadingSheetFieldsDto? fields, string? address = null, int dataRowCount = 0)
    {
        ws.Column(1).Width = 15;
        ws.Column(2).Width = 65;
        ws.Column(3).Width = 11;
        ws.Column(4).Width = 11;
        ws.Column(5).Width = 11;
        ws.Column(6).Width = 11;
        ws.Column(7).Width = 12;
        ws.Column(8).Width = 21;

        // The footer (Driver Sign / Loading Total / Sr.No table / Total) always sat at fixed rows
        // 35-43, which only left room for 23 data rows (12-34) before it started overwriting the
        // footer's own cells - this pushes the footer below however many data rows this pullout
        // actually has, while keeping the original 35-43 layout/spacing for the common case.
        var footerStartRow = Math.Max(35, LoadingSheetFirstDataRow + dataRowCount + 1);
        var lastRow = footerStartRow + 8;

        var whole = ws.Range(1, 1, lastRow, 8);
        whole.Style.Font.FontName = "Arial";
        whole.Style.Font.FontSize = 10;
        whole.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        whole.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        whole.Style.Border.OutsideBorderColor = XLColor.FromHtml("#222222");
        whole.Style.Border.InsideBorderColor = XLColor.FromHtml("#222222");
        whole.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        ws.Row(1).Height = 21;
        ws.Row(2).Height = 23;
        ws.Row(3).Height = 23;
        ws.Row(4).Height = 30;
        for (var r = 5; r <= 11; r++) ws.Row(r).Height = 21;

        ws.Range("A1:H1").Merge();

        ws.Range("A2:H2").Merge();
        ws.Cell(2, 1).Value = "GODREJ CONSUMER PRODUCTS LTD";
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Range("A3:H3").Merge();
        ws.Cell(3, 1).Value = string.IsNullOrWhiteSpace(address) ? "NA" : address;
        ws.Cell(3, 1).Style.Font.Bold = true;
        ws.Cell(3, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Range("A4:H4").Merge();
        ws.Cell(4, 1).Value = "Loading Sheet";
        ws.Cell(4, 1).Style.Font.Bold = true;
        ws.Cell(4, 1).Style.Font.FontSize = 15;
        ws.Cell(4, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        void Label(int row, int col, string text)
        {
            ws.Cell(row, col).Value = text;
            ws.Cell(row, col).Style.Font.Bold = true;
        }

        Label(5, 1, "Transport");
        ws.Range("B5:C5").Merge();
        if (fields?.TransportName is not null) ws.Cell(5, 2).Value = fields.TransportName;
        Label(5, 4, "GATE NO:");
        Label(5, 6, "Plan Time:");
        if (fields?.PlanTime is not null) ws.Cell(5, 7).Value = fields.PlanTime;
        if (fields?.WeightText is not null) ws.Cell(5, 8).Value = fields.WeightText;

        Label(6, 1, "Vehicle no");
        ws.Range("B6:C6").Merge();
        if (fields?.VehicleNumber is not null) ws.Cell(6, 2).Value = fields.VehicleNumber;
        if (fields?.GateNumber is not null) ws.Cell(6, 4).Value = fields.GateNumber;
        Label(6, 6, "Date");
        if (fields?.DateText is not null)
        {
            ws.Cell(6, 7).Value = fields.DateText;
        }
        else if (date is not null)
        {
            ws.Cell(6, 7).Value = date.Value.ToString("dd.MM.yyyy");
        }
        if (fields?.VolumeText is not null) ws.Cell(6, 8).Value = fields.VolumeText;

        Label(7, 1, "Destination");
        ws.Range("B7:C7").Merge();
        if (destination is not null)
        {
            ws.Cell(7, 2).Value = destination;
        }
        Label(7, 6, "Start Time");
        if (fields?.StartTime is not null) ws.Cell(7, 7).Value = fields.StartTime;

        Label(8, 1, "Driver No");
        ws.Range("B8:C8").Merge();
        if (fields?.DriverNumber is not null) ws.Cell(8, 2).Value = fields.DriverNumber;
        Label(8, 6, "End Time");
        if (fields?.EndTime is not null) ws.Cell(8, 7).Value = fields.EndTime;

        ws.Range("B9:C9").Merge();

        ws.Range("G10:H10").Merge();
        ws.Cell(10, 6).Value = "Indent";
        if (indent is not null)
        {
            ws.Cell(10, 7).Value = indent;
        }

        string[] columnHeaders = ["Code", "Description", "Del Qty", "Priority", "MRP", "PKD", "Load Qty", "Remarks"];
        for (var c = 0; c < columnHeaders.Length; c++)
        {
            Label(11, c + 1, columnHeaders[c]);
        }

        ws.Row(footerStartRow).Height = 21;

        var driverSignRow = footerStartRow + 1;
        ws.Range(driverSignRow, 1, driverSignRow, 2).Merge();
        ws.Cell(driverSignRow, 1).Value = "Driver Sign";
        ws.Cell(driverSignRow, 3).Value = "Loading Total";
        ws.Row(driverSignRow).Height = 21;

        var supervisorSignRow = footerStartRow + 2;
        ws.Range(supervisorSignRow, 1, supervisorSignRow, 2).Merge();
        ws.Cell(supervisorSignRow, 1).Value = "Supervisor Sign";
        ws.Cell(supervisorSignRow, 3).Value = "Checked By";
        ws.Row(supervisorSignRow).Height = 21;

        ws.Row(footerStartRow + 3).Height = 21;

        var deliveryHeaderRow = footerStartRow + 4;
        Label(deliveryHeaderRow, 1, "Sr.No.");
        Label(deliveryHeaderRow, 2, "Delivery No");
        Label(deliveryHeaderRow, 3, "Del Qty");
        Label(deliveryHeaderRow, 4, "Total Spending");
        ws.Row(deliveryHeaderRow).Height = 21;

        for (var i = 0; i < 3; i++)
        {
            var row = deliveryHeaderRow + 1 + i;
            ws.Cell(row, 1).Value = i + 1;
            ws.Row(row).Height = 21;
        }

        var totalRow = deliveryHeaderRow + 4;
        ws.Range(totalRow, 1, totalRow, 2).Merge();
        ws.Cell(totalRow, 1).Value = "Total";
        ws.Row(totalRow).Height = 21;
    }

    private static void WriteLoadingSheetDataRow(IXLWorksheet ws, int row, long code, string description, decimal delQty, string? priority, string? remarks)
    {
        ws.Row(row).Height = 20;
        ws.Cell(row, 1).Value = code.ToString();
        ws.Cell(row, 1).Style.NumberFormat.Format = "@";
        ws.Cell(row, 2).Value = description;
        ws.Cell(row, 3).Value = delQty;
        ws.Cell(row, 3).Style.NumberFormat.Format = "0";
        if (priority is not null)
        {
            ws.Cell(row, 4).Value = priority;
        }
        if (remarks is not null)
        {
            ws.Cell(row, 8).Value = remarks;
        }
    }

    /// <summary>Scans rows 1..maxRow, every column, for a cell whose text equals <paramref name="label"/>
    /// exactly, and returns the next column's text on that same row (trimmed), or null if not
    /// found/blank. Covers both the left label column (Transport, Vehicle no, ...) and the right
    /// one (Plan Time:, Date, Start Time, End Time), since both sit one column left of their value.</summary>
    private static string? FindLabeledValue(IXLWorksheet ws, int maxRow, string label)
    {
        for (var row = 1; row <= maxRow; row++)
        {
            for (var col = 1; col <= 7; col++)
            {
                if (ws.Cell(row, col).GetString().Trim().Equals(label, StringComparison.OrdinalIgnoreCase))
                {
                    return NullIfEmpty(ws.Cell(row, col + 1).GetString());
                }
            }
        }

        return null;
    }

    /// <summary>Scans rows 1..maxRow, every column, for a cell whose text contains
    /// <paramref name="labelContains"/>, and returns the text one row below in that same column
    /// (trimmed), or null if not found/blank. The Loading Sheet's "GATE NO:" label sits one row
    /// above its value, unlike every other header label.</summary>
    private static string? FindLabeledValueBelow(IXLWorksheet ws, int maxRow, string labelContains)
    {
        for (var row = 1; row <= maxRow; row++)
        {
            for (var col = 1; col <= 8; col++)
            {
                if (ws.Cell(row, col).GetString().Contains(labelContains, StringComparison.OrdinalIgnoreCase))
                {
                    return NullIfEmpty(ws.Cell(row + 1, col).GetString());
                }
            }
        }

        return null;
    }

    /// <summary>Scans rows 1..maxRow, every column, for a cell whose text starts with
    /// <paramref name="prefix"/> and returns that whole cell's text verbatim - Weight/Volume are a
    /// single combined label+value cell on the Loading Sheet (e.g. "Weight 35MT"), not a separate
    /// label/value pair.</summary>
    private static string? FindTextStartingWith(IXLWorksheet ws, int maxRow, string prefix)
    {
        for (var row = 1; row <= maxRow; row++)
        {
            for (var col = 1; col <= 8; col++)
            {
                var text = ws.Cell(row, col).GetString().Trim();
                if (text.Length > 0 && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }
        }

        return null;
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

    public IReadOnlyList<StockMasterImportRow> ReadStockMaster(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();

        var headerRow = FindHeaderRow(ws, "Material Code");
        var rows = new List<StockMasterImportRow>();

        var row = headerRow + 1;
        while (true)
        {
            var cellA = ws.Cell(row, 1);
            if (cellA.IsEmpty() || !cellA.TryGetValue(out long materialNumber) || materialNumber <= 0)
            {
                break;
            }

            rows.Add(new StockMasterImportRow(
                MaterialNumber: materialNumber,
                QuantityBoxes: (decimal)ws.Cell(row, 3).GetValue<double>(),
                MfgMonthText: ws.Cell(row, 4).GetString().Trim(),
                PalletPositionCode: ws.Cell(row, 5).GetString().Trim(),
                RowNumber: row));

            row++;
        }

        return rows;
    }

    public byte[] WriteStockMasterTemplate()
    {
        string[] headers = ["Material Code", "Active", "Total Stock in CFB", "Mfg Month", "Pallet Position"];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Inventory Master");
        WriteHeader(ws, headers);

        var samples = new object[][]
        {
            [40034354, "Active", 40, "MAR|2026", "A-01-01"],
            [40058047, "Active", 80, "MAR|2026", "A-01-02"],
            [40063045, "Active", 25, "JUN|2026", "B-02-01"]
        };

        for (var r = 0; r < samples.Length; r++)
        {
            for (var c = 0; c < samples[r].Length; c++)
            {
                ws.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(samples[r][c]);
            }
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] WriteInwardDownload(IReadOnlyList<InwardDownloadRow> rows)
    {
        string[] headers =
        [
            "GRN Reference", "Material Code", "Material Desc.", "Mfg Month", "Requested (CFB)", "Allocated (CFB)",
            "Line Status", "Pallet Position", "Qty at Position (CFB)", "Put-away Status"
            , "Put-away ID"
        ];

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Inward Download");
        WriteHeader(ws, headers);

        var rowNum = 2;
        foreach (var item in rows)
        {
            ws.Cell(rowNum, 1).Value = item.ReferenceNumber;
            ws.Cell(rowNum, 2).Value = item.MaterialNumber;
            ws.Cell(rowNum, 3).Value = item.MaterialDescription;
            ws.Cell(rowNum, 4).Value = item.MfgMonthLabel;
            ws.Cell(rowNum, 5).Value = item.RequestedQuantityBoxes;
            ws.Cell(rowNum, 6).Value = item.AllocatedQuantityBoxes;
            ws.Cell(rowNum, 7).Value = item.LineStatus;
            ws.Cell(rowNum, 8).Value = item.LocationCode;
            ws.Cell(rowNum, 9).Value = item.QuantityBoxes;
            ws.Cell(rowNum, 10).Value = item.PutawayStatus;
            ws.Cell(rowNum, 11).Value = XLCellValue.FromObject(item.PutawayId);
            var editableFill = item.PutawayStatus.Equals("Reserved", StringComparison.OrdinalIgnoreCase)
                ? XLColor.FromHtml("#EAF2FF")
                : XLColor.FromHtml("#F1F5F9");
            ws.Cell(rowNum, 8).Style.Fill.BackgroundColor = editableFill;
            ws.Cell(rowNum, 9).Style.Fill.BackgroundColor = editableFill;
            rowNum++;
        }

        ws.SheetView.FreezeRows(1);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Column(11).Hide();
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<InwardPutawayReassignmentImportRow> ReadInwardPutawayReassignment(Stream fileStream)
    {
        using var workbook = LoadWorkbook(fileStream);
        var ws = workbook.Worksheets.First();
        var headerRow = FindHeaderRow(ws, "GRN Reference");

        if (!ws.Cell(headerRow, 11).GetString().Trim().Equals("Put-away ID", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This workbook does not contain put-away identifiers. Download the GRN Excel again, edit only Pallet Position and Qty at Position, and re-upload it.");
        }

        var rows = new List<InwardPutawayReassignmentImportRow>();
        for (var row = headerRow + 1; ; row++)
        {
            var reference = ws.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(reference)) break;

            if (!ws.Cell(row, 2).TryGetValue(out long materialNumber) || materialNumber <= 0)
            {
                throw new InvalidOperationException($"Row {row}: Material Code is missing or invalid.");
            }

            var putawayId = ws.Cell(row, 11).TryGetValue(out int parsedId) && parsedId > 0
                ? parsedId
                : (int?)null;

            rows.Add(new InwardPutawayReassignmentImportRow(
                reference,
                materialNumber,
                ws.Cell(row, 4).GetString().Trim(),
                (decimal)ws.Cell(row, 5).GetValue<double>(),
                (decimal)ws.Cell(row, 6).GetValue<double>(),
                ws.Cell(row, 8).GetString().Trim(),
                (decimal)ws.Cell(row, 9).GetValue<double>(),
                ws.Cell(row, 10).GetString().Trim(),
                putawayId,
                row));
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("The workbook does not contain any GRN allocation rows.");
        }

        return rows;
    }

    private static byte[] WriteRows<T>(
        string sheetName,
        string[] headers,
        IReadOnlyList<T> rows,
        Action<IXLWorksheet, int, T> writeRow)
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

    private static int FindHeaderRow(IXLWorksheet ws, string firstColumnLabelContains, int maxRow = 10)
    {
        for (var row = 1; row <= maxRow; row++)
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

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    // A blank/non-numeric cell's .GetValue<double>() throws InvalidCastException, which used to
    // propagate all the way out of the reader before any row could be validated or reported as an
    // error - crashing the whole import with a cryptic "Cannot convert ...'s value to
    // System.Double" message instead of a clean per-row validation error. TryGetValue instead
    // degrades to 0, which the importer's own required-field checks then catch and report normally.
    private static decimal ReadDecimal(IXLCell cell) =>
        cell.TryGetValue(out double value) ? (decimal)value : 0m;

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
