using ClosedXML.Excel;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers the multi-sheet Loading Sheet workbook reader behind the client's real daily
/// workflow: one growing workbook, one sheet per order, re-uploaded whole each time. Fixed layout
/// per sheet - header row 12, Vehicle No at B9, Destination at B10, an already-assigned Pick
/// Reference Number at H10, data from row 13 until a "Driver Sign" marker in column A.</summary>
public class ReadPulloutWorkbookTests
{
    private readonly ClosedXmlExcelService _service = new();

    private static IXLWorksheet AddOrderSheet(XLWorkbook workbook, string name, string? vehicle = "RJ09GE1234", string? destination = "PONDY-1", string? pickReference = null)
    {
        var ws = workbook.Worksheets.Add(name);
        if (vehicle is not null) ws.Cell(9, 2).Value = vehicle;
        if (destination is not null) ws.Cell(10, 2).Value = destination;
        if (pickReference is not null) ws.Cell(10, 8).Value = pickReference;
        ws.Cell(12, 1).Value = "Code";
        ws.Cell(12, 2).Value = "Description";
        ws.Cell(12, 3).Value = "Del Qty";
        ws.Cell(12, 4).Value = "Priority";
        return ws;
    }

    private static void AddDataRow(IXLWorksheet ws, int row, long code, string description, decimal qty)
    {
        ws.Cell(row, 1).Value = code;
        ws.Cell(row, 2).Value = description;
        ws.Cell(row, 3).Value = qty;
    }

    [Fact]
    public void ParsesMultipleSheets_OneEntryPerSheet_InWorkbookOrder()
    {
        using var workbook = new XLWorkbook();
        var ws1 = AddOrderSheet(workbook, "PONDY-1");
        AddDataRow(ws1, 13, 40063045, "Material A", 10);
        ws1.Cell(14, 1).Value = "Driver Sign";

        var ws2 = AddOrderSheet(workbook, "AHMD-1", destination: "AHMD-1");
        AddDataRow(ws2, 13, 40064688, "Material B", 20);
        ws2.Cell(14, 1).Value = "Driver Sign";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheets = _service.ReadPulloutWorkbook(stream);

        Assert.Equal(2, sheets.Count);
        Assert.Equal("PONDY-1", sheets[0].SheetName);
        Assert.Equal("AHMD-1", sheets[1].SheetName);
        Assert.True(sheets[0].IsOrderSheet);
        Assert.Single(sheets[0].Rows);
        Assert.Equal(40063045, sheets[0].Rows[0].MaterialCode);
        Assert.Single(sheets[1].Rows);
        Assert.Equal(40064688, sheets[1].Rows[0].MaterialCode);
    }

    [Fact]
    public void ReadsVehicleDestinationAndPickReference_FromFixedCells()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "PONDY-1", vehicle: "NL01AG5387", destination: "PONDY-1", pickReference: "PICK-20261031-000001");
        AddDataRow(ws, 13, 40063045, "Material A", 10);
        ws.Cell(14, 1).Value = "Driver Sign";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadPulloutWorkbook(stream));

        Assert.Equal("NL01AG5387", sheet.VehicleNumber);
        Assert.Equal("PONDY-1", sheet.Destination);
        Assert.Equal("PICK-20261031-000001", sheet.ExistingPickReference);
    }

    [Fact]
    public void StopsAt_DriverSign_EvenWithBlankRowsInterruptingTheDataAbove()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "PONDY-1");
        AddDataRow(ws, 13, 40063045, "Material A", 10);
        // row 14 intentionally blank (no Code) - must not stop parsing here.
        AddDataRow(ws, 15, 40064688, "Material B", 20);
        ws.Cell(16, 1).Value = "Driver Sign";
        // Rows after Driver Sign must never be read, even if they look like valid data.
        AddDataRow(ws, 20, 40065000, "Should never be read", 99);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadPulloutWorkbook(stream));

        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(40063045, sheet.Rows[0].MaterialCode);
        Assert.Equal(40064688, sheet.Rows[1].MaterialCode);
    }

    [Fact]
    public void NonOrderSheet_NoCodeHeaderAtRow12_IsReportedNotAnOrderSheet()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("MIS");
        ws.Cell(1, 3).Value = "PLANT";
        ws.Cell(2, 3).Value = "C042";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadPulloutWorkbook(stream));

        Assert.False(sheet.IsOrderSheet);
        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public void OrderSheetStructure_ButNoDataRows_ReturnsEmptyRows_StillAnOrderSheet()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "Sheet19", vehicle: null, destination: null);
        ws.Cell(120, 1).Value = "Driver Sign"; // footer present, but no data rows above it
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadPulloutWorkbook(stream));

        Assert.True(sheet.IsOrderSheet);
        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public void BlankDelQtyCell_DoesNotThrow_DegradesToZero()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "PONDY-1");
        ws.Cell(13, 1).Value = 40063045;
        ws.Cell(13, 2).Value = "Material A";
        // Cell(13, 3) left blank - used to throw a raw ClosedXML cast exception.
        ws.Cell(14, 1).Value = "Driver Sign";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadPulloutWorkbook(stream));

        var row = Assert.Single(sheet.Rows);
        Assert.Equal(0m, row.QuantityBoxes);
    }

    [Fact]
    public void SheetWithNoDriverSign_StopsAtEndOfUsedRange_DoesNotLoopForever()
    {
        using var workbook = new XLWorkbook();
        var ws = AddOrderSheet(workbook, "PONDY-1");
        AddDataRow(ws, 13, 40063045, "Material A", 10);
        // No "Driver Sign" anywhere - must still terminate.
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadPulloutWorkbook(stream));

        Assert.Single(sheet.Rows);
    }
}
