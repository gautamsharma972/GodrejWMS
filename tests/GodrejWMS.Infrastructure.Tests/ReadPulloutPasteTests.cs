using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers parsing a Loading Sheet pasted straight from Excel (tab-separated cells,
/// newline-separated rows) - the clipboard-paste companion to the .xlsx upload path, built so a
/// client can submit each same-day order the moment it comes in instead of maintaining one growing
/// workbook. Mirrors <see cref="ClosedXmlExcelService.ReadPulloutUpload"/>'s exact column/label
/// assumptions, just reading a parsed text grid instead of cells.</summary>
public class ReadPulloutPasteTests
{
    private readonly ClosedXmlExcelService _service = new();

    private static string BuildPaste(string? indentCell = null)
    {
        var rows = new[]
        {
            "Transport\tKAPOOR\t\tGATE NO:\t\tPlan Time:\t12:10\t",
            "\t\t\t808950690\t\t\t\tWeight 17MT",
            "Vehicle no\tNL01AG5387\t\t\t\tDate\t30.10.2026\t",
            "\t\t\t\t\t\t\tVolume 55,000.00",
            "Destination\tPONDY-1\t\t\t\tStart Time\t\t",
            "Driver No\tRR. 7534849193\t\t\t\tEnd Time\t\t",
            $"\t\t\t\t\tIndent\t{indentCell ?? ""}\t",
            "Code\tDescription\tDel Qty\tPriority\tMRP\tPKD\tLoad Q\tRemarks",
            "40063045\tTest Material\t10\tHigh\t\t\t\t",
            "40064688\tAnother Material\t5\t\t\t\t\t",
        };
        return string.Join('\n', rows);
    }

    [Fact]
    public void ParsesDataRows_CodeDescriptionQtyPriority()
    {
        var (rows, _, _) = _service.ReadPulloutPaste(BuildPaste());

        Assert.Equal(2, rows.Count);
        Assert.Equal(40063045, rows[0].MaterialCode);
        Assert.Equal("Test Material", rows[0].MaterialDescription);
        Assert.Equal(10, rows[0].QuantityBoxes);
        Assert.Equal("High", rows[0].Priority);
        Assert.Equal(40064688, rows[1].MaterialCode);
        Assert.Null(rows[1].Priority);
    }

    [Fact]
    public void ParsesHeaderFields_ByLabel()
    {
        var (_, fields, _) = _service.ReadPulloutPaste(BuildPaste());

        Assert.Equal("KAPOOR", fields.TransportName);
        Assert.Equal("NL01AG5387", fields.VehicleNumber);
        Assert.Equal("808950690", fields.GateNumber);
        Assert.Equal("RR. 7534849193", fields.DriverNumber);
        Assert.Equal("12:10", fields.PlanTime);
        Assert.Equal("30.10.2026", fields.DateText);
        Assert.Equal("Weight 17MT", fields.WeightText);
        Assert.Equal("Volume 55,000.00", fields.VolumeText);
    }

    [Fact]
    public void ParsesPlainIndentValue_InSameRowAsLabel_OurOwnDownloadFormat()
    {
        var (_, _, indentText) = _service.ReadPulloutPaste(BuildPaste("GCPL-C121-O-12950"));

        Assert.Equal("GCPL-C121-O-12950", indentText);
    }

    [Fact]
    public void ParsesIndentValue_OneRowBelowLabel_ClientsNativeSheetFormat()
    {
        // Real client sheets put the Indent value one row below the label, same column - the same
        // layout "GATE NO" uses - not in the next column on the same row like our own download does.
        var rows = new[]
        {
            "Transport\tKAPOOR\t\tGATE NO:\t\tPlan Time:\t12:10\t",
            "\t\t\t\t\tIndent\t\t",
            "\t\t\t\t\tGCPL-C121-O-12950\t",
            "Code\tDescription\tDel Qty\tPriority",
            "40063045\tTest Material\t10\t",
        };
        var text = string.Join('\n', rows);

        var (dataRows, _, indentText) = _service.ReadPulloutPaste(text);

        Assert.Equal("GCPL-C121-O-12950", indentText);
        Assert.Single(dataRows);
    }

    [Fact]
    public void ParsesIndentCell_WithEmbeddedLineBreak_AsOneCell_NotTwoRows()
    {
        // Excel's clipboard format wraps a cell containing its own line break in double quotes -
        // this must NOT be treated as a row separator, or every row after it would misalign.
        var quotedIndent = "\"PICK-20261031-000009\nGCPL-C121-O-12950\"";
        var (rows, _, indentText) = _service.ReadPulloutPaste(BuildPaste(quotedIndent));

        Assert.Equal("PICK-20261031-000009\nGCPL-C121-O-12950", indentText);
        // Both data rows still parsed correctly - proof the embedded newline didn't shift anything.
        Assert.Equal(2, rows.Count);
        Assert.Equal(40063045, rows[0].MaterialCode);
        Assert.Equal(40064688, rows[1].MaterialCode);
    }

    [Fact]
    public void NoIndentCell_ReturnsNullIndentText()
    {
        var (_, _, indentText) = _service.ReadPulloutPaste(BuildPaste());

        Assert.Null(indentText);
    }

    [Fact]
    public void StopsAtFirstRow_WhereCodeColumnIsBlank()
    {
        var text = BuildPaste() + "\n\tTrailing row with no code\t\t";
        var (rows, _, _) = _service.ReadPulloutPaste(text);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void PasteWithOnlyDataRows_NoCodeHeaderRowAtAll_StillParsesEveryRow()
    {
        // The client selected and copied just the material lines, not the "Code" column-header row
        // above them (or anything above that). Without a header row to anchor on, the first pasted
        // line must still be treated as real data, not silently dropped as a phantom header.
        var text = string.Join('\n',
        [
            "40063045\tTest Material\t10\tHigh",
            "40064688\tAnother Material\t5\t"
        ]);

        var (rows, fields, indentText) = _service.ReadPulloutPaste(text);

        Assert.Equal(2, rows.Count);
        Assert.Equal(40063045, rows[0].MaterialCode);
        Assert.Equal("Test Material", rows[0].MaterialDescription);
        Assert.Equal(10, rows[0].QuantityBoxes);
        Assert.Equal(40064688, rows[1].MaterialCode);
        Assert.Null(indentText);
        Assert.Null(fields.TransportName);
    }

    [Fact]
    public void PasteWithCodeHeaderRowButNoUpperHeaderBlock_ParsesAllDataRows()
    {
        // The client included the "Code/Description/Del Qty/Priority" header row but not the
        // Transport/Vehicle/Indent block above it.
        var text = string.Join('\n',
        [
            "Code\tDescription\tDel Qty\tPriority",
            "40063045\tTest Material\t10\tHigh",
            "40064688\tAnother Material\t5\t"
        ]);

        var (rows, fields, indentText) = _service.ReadPulloutPaste(text);

        Assert.Equal(2, rows.Count);
        Assert.Equal(40063045, rows[0].MaterialCode);
        Assert.Equal(40064688, rows[1].MaterialCode);
        Assert.Null(indentText);
        Assert.Null(fields.TransportName);
    }

    [Fact]
    public void EmptyPaste_ReturnsNoRows()
    {
        var (rows, _, indentText) = _service.ReadPulloutPaste(string.Empty);

        Assert.Empty(rows);
        Assert.Null(indentText);
    }
}
