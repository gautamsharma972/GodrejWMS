using ClosedXML.Excel;
using GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Dtos;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class AopLocationPreferencesExportTests
{
    [Fact]
    public void Export_WritesOneRowPerMappingWithReadableColumns()
    {
        var bytes = new ClosedXmlExcelService().WriteAopLocationPreferences(
        [
            new AopDescriptionLocationPreferenceDto(1, 2, "AOP1", "Soap", 1, "WH1", 3, "A", 10, "A-01-01", 11, "A-05-01", true),
            new AopDescriptionLocationPreferenceDto(2, 3, "AOP2", null, 1, "WH1", 4, "B", 20, "B-01-01", 21, "B-02-01", false)
        ]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var ws = workbook.Worksheet(1);
        Assert.Equal("AOP Code", ws.Cell(1, 1).GetString());
        Assert.Equal("AOP1", ws.Cell(2, 1).GetString());
        Assert.Equal("A-05-01", ws.Cell(2, 6).GetString());
        Assert.Equal("Active", ws.Cell(2, 7).GetString());
        Assert.Equal("Inactive", ws.Cell(3, 7).GetString());
    }
}
