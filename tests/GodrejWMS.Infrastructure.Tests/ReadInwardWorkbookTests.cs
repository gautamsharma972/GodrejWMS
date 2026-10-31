using ClosedXML.Excel;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers the multi-sheet Unloading Sheet workbook reader behind the client's real daily
/// workflow: one growing workbook, one sheet per receipt, re-uploaded whole each time. Fixed
/// layout per sheet - header row 8, LR No at D2, Vehicle No at D3, Received From at F5, an
/// already-assigned GRN Reference Number at E2, data from row 9 until a "Remark" marker in
/// column A.</summary>
public class ReadInwardWorkbookTests
{
    private readonly ClosedXmlExcelService _service = new();

    private static IXLWorksheet AddOrderSheet(XLWorkbook workbook, string name, string? lrNumber = "77872", string? vehicle = "HR68C6373", string? receivedFrom = "Tepla PWH", string? grnReference = null)
    {
        var ws = workbook.Worksheets.Add(name);
        if (lrNumber is not null) ws.Cell(2, 4).Value = lrNumber;
        if (vehicle is not null) ws.Cell(3, 4).Value = vehicle;
        if (receivedFrom is not null) ws.Cell(5, 6).Value = receivedFrom;
        if (grnReference is not null) ws.Cell(2, 5).Value = grnReference;
        ws.Cell(8, 1).Value = "Code";
        ws.Cell(8, 2).Value = "Desc";
        ws.Cell(8, 3).Value = "Qty";
        ws.Cell(8, 4).Value = "Inbound Delv No";
        ws.Cell(8, 5).Value = "Physical Qty";
        ws.Cell(8, 6).Value = "Pkd/Batch";
        return ws;
    }

    private static void AddDataRow(IXLWorksheet ws, int row, long code, string description, decimal qty, string? pkm = "SEP|2026")
    {
        ws.Cell(row, 1).Value = code;
        ws.Cell(row, 2).Value = description;
        ws.Cell(row, 3).Value = qty;
        if (pkm is not null) ws.Cell(row, 6).Value = pkm;
    }

    [Fact]
    public void ParsesMultipleSheets_OneEntryPerSheet_InWorkbookOrder()
    {
        using var workbook = new XLWorkbook();
        var ws1 = AddOrderSheet(workbook, "Sheet1");
        AddDataRow(ws1, 9, 40041315, "Material A", 166);
        ws1.Cell(10, 1).Value = "Remark";

        var ws2 = AddOrderSheet(workbook, "Sheet2", vehicle: "HR68C9999");
        AddDataRow(ws2, 9, 40063045, "Material B", 20);
        ws2.Cell(10, 1).Value = "Remark";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheets = _service.ReadInwardWorkbook(stream);

        Assert.Equal(2, sheets.Count);
        Assert.Equal("Sheet1", sheets[0].SheetName);
        Assert.Equal("Sheet2", sheets[1].SheetName);
        Assert.True(sheets[0].IsOrderSheet);
        Assert.Single(sheets[0].Rows);
        Assert.Equal(40041315, sheets[0].Rows[0].MaterialCode);
        Assert.Single(sheets[1].Rows);
        Assert.Equal(40063045, sheets[1].Rows[0].MaterialCode);
    }

    [Fact]
    public void ReadsLrVehicleReceivedFromAndGrnReference_FromFixedCells()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "Sheet1", lrNumber: "77872", vehicle: "HR68C6373", receivedFrom: "Tepla PWH", grnReference: "GRN-20261031-000001");
        AddDataRow(ws, 9, 40041315, "Material A", 166);
        ws.Cell(10, 1).Value = "Remark";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        Assert.Equal("77872", sheet.LrNumber);
        Assert.Equal("HR68C6373", sheet.VehicleNumber);
        Assert.Equal("Tepla PWH", sheet.ReceivedFrom);
        Assert.Equal("GRN-20261031-000001", sheet.ExistingGrnReference);
    }

    [Fact]
    public void StopsAt_Remark_EvenWithBlankRowsInterruptingTheDataAbove()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "Sheet1");
        AddDataRow(ws, 9, 40041315, "Material A", 166);
        // row 10 intentionally blank (no Code) - must not stop parsing here.
        AddDataRow(ws, 11, 40063045, "Material B", 20);
        ws.Cell(12, 1).Value = "Remark";
        // Rows after Remark must never be read, even if they look like valid data.
        AddDataRow(ws, 16, 40065000, "Should never be read", 99);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(40041315, sheet.Rows[0].MaterialCode);
        Assert.Equal(40063045, sheet.Rows[1].MaterialCode);
    }

    [Fact]
    public void NonOrderSheet_NoCodeHeaderAtRow8_IsReportedNotAnOrderSheet()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("MIS");
        ws.Cell(1, 3).Value = "PLANT";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        Assert.False(sheet.IsOrderSheet);
        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public void OrderSheetStructure_ButNoDataRows_ReturnsEmptyRows_StillAnOrderSheet()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "Sheet1", lrNumber: null, vehicle: null, receivedFrom: null);
        ws.Cell(27, 1).Value = "Remark"; // footer present, but no data rows above it
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        Assert.True(sheet.IsOrderSheet);
        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public void RowsMissingPkdBatch_AreReportedByRowNumber()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "Sheet1");
        AddDataRow(ws, 9, 40041315, "Material A", 166, pkm: "SEP|2026");
        AddDataRow(ws, 10, 40063045, "Material B", 20, pkm: null);
        AddDataRow(ws, 11, 40064688, "Material C", 10, pkm: null);
        ws.Cell(12, 1).Value = "Remark";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        Assert.Equal(3, sheet.Rows.Count);
        Assert.Equal([10, 11], sheet.RowsMissingPkm);
    }

    [Fact]
    public void BlankQtyCell_DoesNotThrow_DegradesToZero()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "Sheet1");
        ws.Cell(9, 1).Value = 40041315;
        ws.Cell(9, 2).Value = "Material A";
        // Cell(9, 3) left blank - used to throw a raw ClosedXML cast exception elsewhere in this file.
        ws.Cell(9, 6).Value = "SEP|2026";
        ws.Cell(10, 1).Value = "Remark";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        var row = Assert.Single(sheet.Rows);
        Assert.Equal(0m, row.QuantityBoxes);
    }

    [Fact]
    public void SheetWithNoRemarkMarker_StopsAtEndOfUsedRange_DoesNotLoopForever()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "Sheet1");
        AddDataRow(ws, 9, 40041315, "Material A", 166);
        // No "Remark" anywhere - must still terminate.
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        Assert.Single(sheet.Rows);
    }
}
