using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class InwardDownloadTests
{
    private static InwardDetailDto Detail(bool isRejected = false) => new(
        7, "GRN-1", new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero),
        [
            new InwardDetailLineDto(40034354, "HIT CIK", "XOLDH", "SEP|2026", 70, 70, AllocationStatus.Fulfilled, null,
                [new InwardPutawayDto("A-01-01", 40, true), new InwardPutawayDto("A-01-02", 30, false)]),
            new InwardDetailLineDto(40058047, "HIT CIK 400", "XOLDH", "SEP|2026", 20, 0, AllocationStatus.Failed, "Warehouse at capacity", [])
        ],
        IsConfirmed: false, IsRejected: isRejected);

    [Fact]
    public void FromDetail_ProducesOneRowPerLocation_AndOneLocationlessRowForUnplacedLine()
    {
        var rows = InwardDownloadMapper.FromDetail(Detail());

        Assert.Equal(3, rows.Count);
        Assert.Equal(("A-01-01", 40m, "Confirmed"), (rows[0].LocationCode, rows[0].QuantityBoxes, rows[0].PutawayStatus));
        Assert.Equal(("A-01-02", 30m, "Reserved"), (rows[1].LocationCode, rows[1].QuantityBoxes, rows[1].PutawayStatus));
        Assert.Equal((string.Empty, 0m, "No location", "Failed"), (rows[2].LocationCode, rows[2].QuantityBoxes, rows[2].PutawayStatus, rows[2].LineStatus));
        Assert.All(rows, r => Assert.Equal("GRN-1", r.ReferenceNumber));
    }

    [Fact]
    public void FromResult_MapsAllocatedToLocations_AndMarksRejected()
    {
        var result = new InwardResultDto(7, "GRN-2", DateTimeOffset.UtcNow,
            [new InwardLineResultDto(1, "M", "JUN|2026", 10, 10, AllocationStatus.Fulfilled, null, [new InwardAllocationLineDto("A-02-01", 10)])],
            IsConfirmed: false, IsRejected: true);

        var row = Assert.Single(InwardDownloadMapper.FromResult(result));

        Assert.Equal(("A-02-01", 10m, "Rejected"), (row.LocationCode, row.QuantityBoxes, row.PutawayStatus));
    }

    [Fact]
    public void WriteInwardDownload_WritesHeaderAndNumericQuantities()
    {
        var bytes = new ClosedXmlExcelService().WriteInwardDownload(InwardDownloadMapper.FromDetail(Detail()));

        using var stream = new MemoryStream(bytes);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet("Inward Download");

        Assert.Equal("GRN Reference", sheet.Cell("A1").GetString());
        Assert.Equal("Put-away Status", sheet.Cell("J1").GetString());
        Assert.Equal("GRN-1", sheet.Cell("A2").GetString());
        Assert.Equal(40034354, sheet.Cell("B2").GetValue<long>());
        Assert.Equal("SEP|2026", sheet.Cell("D2").GetString());
        Assert.Equal(70m, sheet.Cell("E2").GetValue<decimal>());
        Assert.Equal("A-01-01", sheet.Cell("H2").GetString());
        Assert.Equal(40m, sheet.Cell("I2").GetValue<decimal>());
        Assert.Equal("Confirmed", sheet.Cell("J2").GetString());
        Assert.Equal(4, sheet.LastRowUsed()!.RowNumber());
    }
}
