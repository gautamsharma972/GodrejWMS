using ClosedXML.Excel;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PulloutDownloadSortTests
{
    [Fact]
    public void PicklistAndLoadingSheet_AreSortedByDescriptionAscending_CaseInsensitive()
    {
        PulloutDownloadRowDto[] rows =
        [
            new(3, "PA GM Shaving Brush", 1, "OCT|2026", "B-01-01", "P1"),
            new(1, "*Cin Health+ 100g", 2, "OCT|2026", "A-01-01", "P1"),
            new(2, "hit cik 320ml", 3, "OCT|2026", "C-01-01", "P1"),
            new(2, "hit cik 320ml", 4, "OCT|2026", "C-02-01", "P1")
        ];
        PulloutLoadingSheetRowDto[] sheetRows =
        [
            new(3, "PA GM Shaving Brush", 1, AllocationStatus.Fulfilled),
            new(2, "HIT CIK 320ML", 7, AllocationStatus.Fulfilled),
            new(1, "*Cin Health+ 100g", 2, AllocationStatus.Fulfilled)
        ];

        var bytes = new ClosedXmlExcelService().WritePulloutDownload(
            rows, new PulloutLoadingSheetHeaderDto("PICK-1", DateTimeOffset.UtcNow, "P1"), sheetRows);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));

        var pick = workbook.Worksheet("Pullout Download");
        Assert.Equal("*Cin Health+ 100g", pick.Cell(11, 2).GetString());
        Assert.Equal("hit cik 320ml", pick.Cell(12, 2).GetString());
        Assert.Equal("C-01-01", pick.Cell(12, 5).GetString()); // stable for equal descriptions
        Assert.Equal("C-02-01", pick.Cell(13, 5).GetString());
        Assert.Equal("PA GM Shaving Brush", pick.Cell(14, 2).GetString());

        Assert.Equal("General", pick.Cell(11, 3).Style.NumberFormat.Format);

        var loading = workbook.Worksheet("Loading Sheet");
        Assert.Equal("*Cin Health+ 100g", loading.Cell(12, 2).GetString());
        Assert.Equal("HIT CIK 320ML", loading.Cell(13, 2).GetString());
        Assert.Equal("PA GM Shaving Brush", loading.Cell(14, 2).GetString());
    }
}
