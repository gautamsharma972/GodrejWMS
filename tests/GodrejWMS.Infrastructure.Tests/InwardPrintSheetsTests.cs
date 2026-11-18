using ClosedXML.Excel;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>The printable (A4 landscape) allocation: the put-away sheet never shows Vehicle No. / LR No.; the unloading sheet keeps the client's header.</summary>
public class InwardPrintSheetsTests
{
    private static InwardUploadHeaderBlock Block() => new(
    [
        ["UNLOADING SHEET", "", "", "", "", ""],
        ["Invoice No:-", "INV-123", "LR NO :-", "11223344", "", ""],
        ["Invoice Date:-", "02/11/26", "Vehicle No:-", "MH04AB1234", "Plan", "THEPLA-1"],
        ["", "", "", "", "", ""],
        ["TRANSPORT", "ABC TRANS. LTD", "Unloading Check List", "", "Received from:", "NAGPUR"],
        ["DOCK No", "2", "Start Time :-", "", "Sup Name:", ""],
        ["Date:-", "02/11/26", "End Time :-", "", "", ""]
    ],
    ["A1:F1"]);

    private static InwardDownloadDto Download(bool withHeader = true) => new(
        [
            new InwardDownloadRow("GRN-1", 40000001, "Material One", "OCT|2026", 30, 30, "Fulfilled", "A-01-01", 30, "Reserved", 11, "MH04AB1234", "LR-77"),
            new InwardDownloadRow("GRN-1", 40000002, "Material Two", "NOV|2026", 10, 4, "Partial", "A-02-01", 4, "Confirmed", 12, "MH04AB1234", "LR-77")
        ],
        new InwardDownloadHeaderDto("GRN-1", "MH04AB1234", "LR-77", withHeader ? Block().ToJson() : null),
        [
            new InwardUnloadingSheetRowDto("40000001", "Material One", 30, "OCT|2026", "Fully allocated"),
            new InwardUnloadingSheetRowDto("40000009", "Unknown", 5, "", "Not allocated - Material code not found in Material Master.")
        ]);

    private static string[] AllText(PrintHeaderBlock block) => block.Rows.SelectMany(r => r).Select(c => c.Text).ToArray();

    [Fact]
    public void PutawaySheet_HasNoVehicleOrLr_InItsColumnsOrItsHeaderBlock()
    {
        Assert.DoesNotContain(InwardPrintSheets.PutawayHeaders, h => h.Contains("Vehicle") || h.StartsWith("LR"));
        Assert.All(InwardPrintSheets.PutawayRows(Download()), row => Assert.Equal(InwardPrintSheets.PutawayHeaders.Length, row.Length));
        Assert.DoesNotContain("MH04AB1234", InwardPrintSheets.PutawayRows(Download()).SelectMany(r => r));
        Assert.DoesNotContain("LR-77", InwardPrintSheets.PutawayRows(Download()).SelectMany(r => r));

        var text = AllText(InwardPrintSheets.HeaderBlock(Download(), hideVehicleAndLr: true)!);
        Assert.DoesNotContain(text, t => t.Contains("Vehicle No") || t.Contains("LR NO"));
        Assert.DoesNotContain("MH04AB1234", text);
        Assert.DoesNotContain("11223344", text);
        Assert.Contains("INV-123", text);   // the rest of the header stays
        Assert.Contains("NAGPUR", text);
    }

    [Fact]
    public void UnloadingSheet_KeepsTheClientsHeaderExactly_IncludingVehicleAndLr()
    {
        var text = AllText(InwardPrintSheets.HeaderBlock(Download(), hideVehicleAndLr: false)!);
        Assert.Contains("Vehicle No:-", text);
        Assert.Contains("MH04AB1234", text);
        Assert.Contains("LR NO :-", text);
        Assert.Contains("11223344", text);
        Assert.Equal(InwardPrintSheets.UnloadingHeaders.Length, InwardPrintSheets.UnloadingRows(Download())[0].Length);
    }

    [Fact]
    public void MergedTitleBecomesASpan_AndCoveredCellsAreNotRepeated()
    {
        var block = InwardPrintSheets.HeaderBlock(Download(), hideVehicleAndLr: false)!;
        var title = block.Rows[0][0];
        Assert.Equal("UNLOADING SHEET", title.Text);
        Assert.Equal(6, title.ColSpan);
        Assert.Single(block.Rows[0]); // A1:F1 merged: one cell in the first row
        Assert.Equal(6, block.Rows[1].Count);
    }

    [Fact]
    public void AHandEnteredGrnWithoutAnUploadHeader_HasNoHeaderBlock_ButStillPrintsItsRows()
    {
        Assert.Null(InwardPrintSheets.HeaderBlock(Download(withHeader: false), hideVehicleAndLr: true));
        Assert.Equal(2, InwardPrintSheets.PutawayRows(Download(withHeader: false)).Count);
    }

    [Fact]
    public void TheExcelDownload_IsReadyToPrint_A4Landscape_OneWide_NoVehicleOrLrColumns()
    {
        var bytes = new ClosedXmlExcelService().WriteInwardDownload(Download());
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        foreach (var name in new[] { "Inward Download", "Unloading Sheet" })
        {
            var setup = workbook.Worksheet(name).PageSetup;
            Assert.Equal(XLPaperSize.A4Paper, setup.PaperSize);
            Assert.Equal(XLPageOrientation.Landscape, setup.PageOrientation);
            Assert.Equal(1, setup.PagesWide);
            Assert.Single(setup.PrintAreas);
        }

        var putaway = workbook.Worksheet("Inward Download").PageSetup.PrintAreas.Single();
        Assert.Equal(9, putaway.RangeAddress.LastAddress.ColumnNumber); // stops at Put-away Status: no Put-away ID, Vehicle No. or LR No.
    }
}
