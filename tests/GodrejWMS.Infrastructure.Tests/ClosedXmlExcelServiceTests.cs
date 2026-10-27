using System.IO.Compression;
using GodrejWMS.Infrastructure.Services;
using GodrejWMS.Application.Features.Materials.Dtos;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class ClosedXmlExcelServiceTests
{
    [Fact]
    public void WriteMaterialMaster_WritesCompleteExportWithTypedValues()
    {
        var material = new MaterialDto(
            1, 40019311, "Test material", "GGAF1", "CHAR", 48, 99.50m,
            41.7m, 23.9m, 22.4m, 22325m, 0.1m, 0.2m, 9.6m, 40, 8m,
            1, "FastMoving", "Fast-moving", 1, "Summer", "Summer", 2, "Fast zone", true, true);

        var bytes = new ClosedXmlExcelService().WriteMaterialMaster([material]);

        using var workbook = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Material Master");
        Assert.Equal("Material Code", sheet.Cell("A1").GetString());
        Assert.Equal(40019311L, sheet.Cell("A2").GetValue<long>());
        Assert.Equal(99.50m, sheet.Cell("F2").GetValue<decimal>());
        Assert.Equal("Fast zone (required)", sheet.Cell("Q2").GetString());
        Assert.Equal("Active", sheet.Cell("S2").GetString());
        Assert.True(sheet.AutoFilter.IsEnabled);
    }

    [Fact]
    public void WriteOperationalReport_WritesHeadersAndRows()
    {
        var service = new ClosedXmlExcelService();
        var bytes = service.WriteOperationalReport("Current Stock",
            [("Warehouse", r => r.Warehouse), ("Quantity", r => r.Quantity.ToString("0.###"))],
            [new GodrejWMS.Application.Features.Reports.Core.ReportRow { Warehouse = "GCPL", Quantity = 12.5m }]);
        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet("Current Stock");
        Assert.Equal("Warehouse", sheet.Cell("A1").GetString());
        Assert.Equal("GCPL", sheet.Cell("A2").GetString());
        Assert.Equal("12.5", sheet.Cell("B2").GetString());
    }

    [Fact]
    public void ReadInwardUpload_OnOwnGeneratedTemplate_RoundTripsWithoutError()
    {
        var service = new ClosedXmlExcelService();
        var bytes = service.WriteInwardUploadTemplate();

        using var stream = new MemoryStream(bytes);
        var rows = service.ReadInwardUpload(stream);

        Assert.NotEmpty(rows);
    }

    [Fact]
    public void WriteInwardUploadTemplate_MfgMonthDropdown_IsAQuotedInlineList()
    {
        // ClosedXML's own reader will echo back an inline list validation's Value regardless of
        // whether the underlying formula1 is actually quoted - so round-tripping through
        // ClosedXML alone can't catch this. Excel itself requires the formula for an inline list
        // to be a literal quoted string ("A,B,C"); without the quotes it's treated as an
        // unresolvable range/name reference and the dropdown silently renders with no items. This
        // asserts against the raw OOXML the way Excel actually reads it.
        var service = new ClosedXmlExcelService();
        var bytes = service.WriteInwardUploadTemplate();

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var sheetEntry = zip.Entries.Single(e => e.FullName == "xl/worksheets/sheet1.xml");
        using var reader = new StreamReader(sheetEntry.Open());
        var sheetXml = reader.ReadToEnd();

        var formulaStart = sheetXml.IndexOf("<x:formula1>", StringComparison.Ordinal) + "<x:formula1>".Length;
        var formulaEnd = sheetXml.IndexOf("</x:formula1>", formulaStart, StringComparison.Ordinal);
        var formula = sheetXml[formulaStart..formulaEnd];

        Assert.StartsWith("\"", formula);
        Assert.EndsWith("\"", formula);
        Assert.Contains("JAN", formula);
        Assert.Contains("DEC", formula);
    }

    [Fact]
    public void ReadInwardUpload_OnUnreadableFile_ThrowsFriendlyMessage_NotRawParserException()
    {
        // ClosedXML's OpenXML parser can throw internal exceptions - including a raw
        // NullReferenceException - on a file it can't fully open (corrupted, .xls renamed to
        // .xlsx, etc). This must surface as an actionable message, not leak that raw exception
        // straight to the Razor upload page's ex.Message display.
        var service = new ClosedXmlExcelService();
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var stream = new MemoryStream(garbage);

        var ex = Assert.Throws<InvalidOperationException>(() => service.ReadInwardUpload(stream));
        Assert.Contains("Unable to read this Excel file", ex.Message);
        Assert.NotNull(ex.InnerException);
    }
}
