using ClosedXML.Excel;
using GodrejWMS.Application.Features.Pullout;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>The printable (A4 landscape) pullout: the Picklist and the Loading Sheet show what the Excel download shows.</summary>
public class PulloutPrintSheetsTests
{
    private static PulloutDetailDto Detail(PulloutLoadingSheetFieldsDto? fields = null) => new(
        7, "PICK-20261118-000010", new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero),
        [
            new PulloutDetailLineDto(40000002, "ZETA spray", 10, 10, AllocationStatus.Fulfilled, null,
                [new PulloutPickDto("Q-38-01", "OCT|2026", 3), new PulloutPickDto("Q-21-02", "NOV|2026", 7)], "Transfer-3", "P1"),
            new PulloutDetailLineDto(40000001, "alpha card", 65, 20, AllocationStatus.Partial, "short", [new PulloutPickDto("Q-22-02", "NOV|2026", 20)], "Transfer-3"),
            new PulloutDetailLineDto(40000003, "Middle gel", 4, 0, AllocationStatus.Failed, "none", [], "Transfer-3")
        ],
        IsConfirmed: true, HeaderFields: fields, WarehouseAddress: "C/O RPM Logistics, Bhiwandi");

    [Fact]
    public void Package_HasOnePicklistRowPerPick_AndOneLoadingRowPerLine()
    {
        var package = PulloutPrintSheets.FromDetail(Detail());
        Assert.Equal(3, package.Rows.Count);        // 2 + 1 + 0 picks
        Assert.Equal(3, package.LoadingRows.Count);
        Assert.Equal("Transfer-3", package.Header.PlantName);
    }

    [Fact]
    public void BothSheets_AreSortedByDescription_AndRowsMatchTheHeaders()
    {
        var package = PulloutPrintSheets.FromDetail(Detail());
        var pick = PulloutPrintSheets.PicklistRows(package);
        Assert.Equal("alpha card", pick[0][1]);
        Assert.Equal("ZETA spray", pick[^1][1]);
        Assert.All(pick, r => Assert.Equal(PulloutPrintSheets.PicklistHeaders.Length, r.Length));

        var loading = PulloutPrintSheets.LoadingRows(package);
        Assert.Equal(new[] { "alpha card", "Middle gel", "ZETA spray" }, loading.Select(r => r[1]));
        Assert.All(loading, r => Assert.Equal(PulloutPrintSheets.LoadingHeaders.Length, r.Length));
        Assert.Equal("Partial Available", loading[0][7]);
        Assert.Equal("Not available", loading[1][7]);
        Assert.Equal("Available", loading[2][7]);
        Assert.Equal("P1", loading[2][3]); // priority carried through
    }

    [Fact]
    public void TransportBlock_UsesTheUploadedFields_AndFallsBackToTheCreationDate()
    {
        var withFields = PulloutPrintSheets.TransportRows(PulloutPrintSheets.FromDetail(Detail(
            new PulloutLoadingSheetFieldsDto("DHL", "MH04FP0003", "G1", "99887", "16:14:00", "18.11.2026", "10:00", "12:00"))).Header);
        Assert.Equal("DHL", withFields[0].Left.Value);
        Assert.Equal("G1", withFields[0].Middle.Value);
        Assert.Equal("16:14:00", withFields[0].Right.Value);
        Assert.Equal("MH04FP0003", withFields[1].Left.Value);
        Assert.Equal("Transfer-3", withFields[2].Left.Value);
        Assert.Equal("99887", withFields[3].Left.Value);

        var without = PulloutPrintSheets.TransportRows(PulloutPrintSheets.FromDetail(Detail()).Header);
        Assert.Equal("18.11.2026", without[1].Right.Value);
        Assert.Equal(string.Empty, without[0].Left.Value);
    }

    [Fact]
    public void Address_IsNA_WhenTheWarehouseHasNone()
    {
        var header = PulloutPrintSheets.FromDetail(Detail() with { WarehouseAddress = null }).Header;
        Assert.Equal("NA", PulloutPrintSheets.Address(header));
    }

    [Fact]
    public void TheExcelDownload_IsReadyToPrint_BothSheetsA4LandscapeOneWide()
    {
        var package = PulloutPrintSheets.FromDetail(Detail());
        var bytes = new ClosedXmlExcelService().WritePulloutDownload(package.Rows, package.Header, package.LoadingRows);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        foreach (var name in new[] { "Pullout Download", "Loading Sheet" })
        {
            var setup = workbook.Worksheet(name).PageSetup;
            Assert.Equal(XLPaperSize.A4Paper, setup.PaperSize);
            Assert.Equal(XLPageOrientation.Landscape, setup.PageOrientation);
            Assert.Equal(1, setup.PagesWide);
        }
    }
}
