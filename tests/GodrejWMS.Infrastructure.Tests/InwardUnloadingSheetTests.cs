using ClosedXML.Excel;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>The GRN Excel download's second sheet (Unloading Sheet), the Vehicle No. / LR No. columns, the
/// first seven upload rows repeated at the top, and Vehicle No. / LR No. on the two history screens.</summary>
public class InwardUnloadingSheetTests
{
    private readonly ClosedXmlExcelService _service = new();

    private static InwardUploadHeaderBlock SampleBlock() => new(
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

    private static InwardDownloadDto Package(bool withUploadHeader = true, bool rejected = false, bool manual = false)
    {
        var detail = new InwardDetailDto(
            7, "GRN-20261102-000002", new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero),
            [
                new InwardDetailLineDto(40039574, "Cin Health+ 100g", "X", "DEC|2024", 100, 100, AllocationStatus.Fulfilled, null,
                    [new InwardPutawayDto("A-44-01", 100, false, Id: 11)]),
                new InwardDetailLineDto(40041135, "PA GM Shaving Brush", "X", "AUG|2026", 100, 60, AllocationStatus.Partial, null,
                    [new InwardPutawayDto("A-28-01", 60, false, Id: 12)]),
                new InwardDetailLineDto(40050608, "HIT CIK 320ML", "X", "SEP|2026", 100, 0, AllocationStatus.Failed, "No capacity", [])
            ],
            IsConfirmed: false, IsRejected: rejected,
            VehicleNumber: manual ? null : "MH04AB1234", LrNumber: manual ? null : "11223344",
            UploadHeaderJson: withUploadHeader && !manual ? SampleBlock().ToJson() : null,
            ReceivedFrom: manual ? null : "NAGPUR");
        return InwardDownloadMapper.PackageFromDetail(detail);
    }

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    // ───────────── mapper ─────────────

    [Theory]
    [InlineData(AllocationStatus.Fulfilled, false, "Fully allocated")]
    [InlineData(AllocationStatus.Partial, false, "Partially allocated")]
    [InlineData(AllocationStatus.Failed, false, "Not allocated")]
    [InlineData(AllocationStatus.Fulfilled, true, "GRN rejected")]
    public void AllocationRemark_DescribesWhetherLocationsWereAllocated(AllocationStatus status, bool rejected, string expected) =>
        Assert.Equal(expected, InwardDownloadMapper.AllocationRemark(status, rejected));

    [Fact]
    public void PackageFromDetail_CarriesVehicleLrAndOneUnloadingRowPerLine()
    {
        var package = Package();

        Assert.Equal("MH04AB1234", package.Header.VehicleNumber);
        Assert.Equal("11223344", package.Header.LrNumber);
        Assert.All(package.Rows, r => Assert.Equal(("MH04AB1234", "11223344"), (r.VehicleNumber, r.LrNumber)));
        Assert.Equal(["Fully allocated", "Partially allocated", "Not allocated"], package.UnloadingRows.Select(r => r.Remarks));
    }

    // ───────────── sheet 1: Inward Download ─────────────

    [Fact]
    public void InwardDownload_WithUploadHeader_RepeatsFirstSevenRows_ThenTableOnRowNine()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package()));
        var ws = workbook.Worksheet("Inward Download");

        Assert.Equal("PUTAWAY SHEET", ws.Cell("A1").GetString());   // the client's title is renamed on this sheet
        Assert.True(ws.Range("A1:F1").IsMerged());
        Assert.Equal("INV-123", ws.Cell("B2").GetString());
        Assert.Equal("11223344", ws.Cell("D2").GetString());
        Assert.Equal("NAGPUR", ws.Cell("F5").GetString());
        Assert.Equal("Material Code", ws.Cell("A9").GetString());
        Assert.Equal(40039574, ws.Cell("A10").GetValue<long>());
    }

    [Fact]
    public void InwardDownload_AppendsVehicleAndLrColumns_WithoutMovingExistingColumns()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package()));
        var ws = workbook.Worksheet("Inward Download");

        Assert.Equal("Put-away Status", ws.Cell("I9").GetString());
        Assert.Equal("Put-away ID", ws.Cell("J9").GetString());
        Assert.True(ws.Column(10).IsHidden);
        Assert.Equal("Vehicle No.", ws.Cell("K9").GetString());
        Assert.Equal("LR No.", ws.Cell("L9").GetString());
        Assert.Equal("MH04AB1234", ws.Cell("K10").GetString());
        Assert.Equal("11223344", ws.Cell("L10").GetString());
        Assert.Equal("11223344", ws.Cell("L12").GetString());
    }

    [Fact]
    public void InwardDownload_ForAHandEnteredGrn_KeepsTableOnRowOne()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package(manual: true)));
        var ws = workbook.Worksheet("Inward Download");

        Assert.Equal("Material Code", ws.Cell("A1").GetString());   // nothing was uploaded, so there is no header to repeat
        Assert.Equal("Vehicle No.", ws.Cell("K1").GetString());
        Assert.Equal(string.Empty, ws.Cell("K2").GetString());
    }

    [Fact]
    public void InwardDownload_ForAnEarlierUploadedGrn_GetsAHeaderRebuiltFromTheStoredFields()
    {
        // Uploaded before the seven rows were saved: only vehicle, LR and Received From were kept.
        using var workbook = Open(_service.WriteInwardDownload(Package(withUploadHeader: false)));
        var ws = workbook.Worksheet("Inward Download");

        Assert.Equal("PUTAWAY SHEET", ws.Cell("A1").GetString());
        Assert.True(ws.Range("A1:F1").IsMerged());
        Assert.Equal("LR NO :-", ws.Cell("C2").GetString());
        Assert.Equal("11223344", ws.Cell("D2").GetString());
        Assert.Equal("Vehicle No:-", ws.Cell("C3").GetString());
        Assert.Equal("MH04AB1234", ws.Cell("D3").GetString());
        Assert.Equal("NAGPUR", ws.Cell("F5").GetString());
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero).ToLocalTime().ToString("dd/MM/yy"), ws.Cell("B7").GetString()); // the GRN date, in local time
        Assert.Equal(string.Empty, ws.Cell("B2").GetString());       // invoice no was never stored: left blank, not invented
        Assert.Equal("Material Code", ws.Cell("A9").GetString());
    }

    [Fact]
    public void RebuiltHeader_StillRoundTripsThroughLocationReassignment()
    {
        var bytes = _service.WriteInwardDownload(Package(withUploadHeader: false));
        using var stream = new MemoryStream(bytes);

        var rows = _service.ReadInwardPutawayReassignment(stream);

        Assert.Equal(3, rows.Count);
        Assert.Equal(11, rows[0].PutawayId);
    }

    [Fact]
    public void ResolveHeaderJson_PrefersTheStoredRows_RebuildsOtherwise_AndSkipsHandEnteredGrns()
    {
        var stored = SampleBlock().ToJson();
        var created = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(stored, InwardDownloadMapper.ResolveHeaderJson(stored, "V", "L", "R", created));
        Assert.Null(InwardDownloadMapper.ResolveHeaderJson(null, null, "  ", null, created));

        var rebuilt = InwardUploadHeaderBlock.TryParse(InwardDownloadMapper.ResolveHeaderJson(null, "MH04AB1234", null, null, created));
        Assert.NotNull(rebuilt);
        Assert.Equal(InwardUploadHeaderBlock.RowCount, rebuilt!.Rows.Count);
        Assert.Equal("MH04AB1234", rebuilt.Rows[2][3]);
        Assert.Equal(string.Empty, rebuilt.Rows[1][3]);              // LR was not stored
    }

    [Fact]
    public void InwardDownload_StillRoundTripsThroughLocationReassignment_WhenHeaderRowsAreAdded()
    {
        var bytes = _service.WriteInwardDownload(Package());
        using var editable = new MemoryStream();
        using (var workbook = Open(bytes))
        {
            workbook.Worksheet("Inward Download").Cell("G10").Value = "B-07-05";
            workbook.SaveAs(editable);
        }
        editable.Position = 0;

        var rows = _service.ReadInwardPutawayReassignment(editable);

        Assert.Equal(3, rows.Count);
        Assert.Equal("B-07-05", rows[0].LocationCode);
        Assert.Equal(11, rows[0].PutawayId);
        Assert.Equal(12, rows[1].PutawayId);
    }

    [Fact]
    public void InwardDownload_HasNoGrnReferenceColumn_ButTheGrnNumberIsOnTheUnloadingSheet()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package()));
        var download = workbook.Worksheet("Inward Download");

        Assert.DoesNotContain(download.Row(9).CellsUsed(), c => c.GetString().Contains("GRN Reference"));
        Assert.DoesNotContain(download.CellsUsed(), c => c.GetString() == "GRN-20261102-000002");
        Assert.Equal("GRN-20261102-000002", workbook.Worksheet("Unloading Sheet").Cell("H2").GetString());
    }

    [Fact]
    public void ReadInwardPutawayReassignment_CurrentLayout_HasNoReference_AndReadsEveryColumn()
    {
        using var stream = new MemoryStream(_service.WriteInwardDownload(Package()));

        var rows = _service.ReadInwardPutawayReassignment(stream);

        Assert.All(rows, r => Assert.Null(r.ReferenceNumber));
        var first = rows[0];
        Assert.Equal(40039574, first.MaterialNumber);
        Assert.Equal("DEC|2024", first.MfgMonthLabel);
        Assert.Equal((100m, 100m), (first.RequestedQuantityBoxes, first.AllocatedQuantityBoxes));
        Assert.Equal(("A-44-01", 100m, "Reserved", 11), (first.LocationCode, first.QuantityBoxes, first.PutawayStatus, first.PutawayId));
    }

    [Fact]
    public void ReadInwardPutawayReassignment_StillReadsAnEarlierDownloadThatStartedWithGrnReference()
    {
        using var legacy = new XLWorkbook();
        var ws = legacy.Worksheets.Add("Inward Download");
        string[] headers = ["GRN Reference", "Material Code", "Material Desc.", "Mfg Month", "Requested (CFB)", "Allocated (CFB)",
            "Line Status", "Pallet Position", "Qty at Position (CFB)", "Put-away Status", "Put-away ID"];
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        ws.Cell(2, 1).Value = "GRN-OLD-1"; ws.Cell(2, 2).Value = 40039574; ws.Cell(2, 3).Value = "Cin Health+";
        ws.Cell(2, 4).Value = "DEC|2024"; ws.Cell(2, 5).Value = 100; ws.Cell(2, 6).Value = 100; ws.Cell(2, 7).Value = "Fulfilled";
        ws.Cell(2, 8).Value = "B-07-05"; ws.Cell(2, 9).Value = 100; ws.Cell(2, 10).Value = "Reserved"; ws.Cell(2, 11).Value = 21;
        using var stream = new MemoryStream();
        legacy.SaveAs(stream);
        stream.Position = 0;

        var row = Assert.Single(_service.ReadInwardPutawayReassignment(stream));

        Assert.Equal("GRN-OLD-1", row.ReferenceNumber);
        Assert.Equal(("B-07-05", 100m, "Reserved", 21), (row.LocationCode, row.QuantityBoxes, row.PutawayStatus, row.PutawayId));
        Assert.Equal(40039574, row.MaterialNumber);
    }

    [Fact]
    public void ReadInwardPutawayReassignment_RejectsAWorkbookThatIsNotAGrnDownload()
    {
        using var other = new XLWorkbook();
        other.Worksheets.Add("Sheet1").Cell(1, 1).Value = "Something else";
        using var stream = new MemoryStream();
        other.SaveAs(stream);
        stream.Position = 0;

        var ex = Assert.Throws<InvalidOperationException>(() => _service.ReadInwardPutawayReassignment(stream));

        Assert.Contains("not a GRN allocation download", ex.Message);
    }

    // ───────────── sheet 2: Unloading Sheet ─────────────

    [Fact]
    public void UnloadingSheet_IsTheSecondSheet_WithTheUploadHeaderOnTop()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package()));

        Assert.Equal(["Inward Download", "Unloading Sheet"], workbook.Worksheets.Select(w => w.Name));
        var ws = workbook.Worksheet("Unloading Sheet");
        Assert.Equal("UNLOADING SHEET", ws.Cell("A1").GetString());
        Assert.Equal("INV-123", ws.Cell("B2").GetString());
        Assert.Equal("MH04AB1234", ws.Cell("D3").GetString());
        Assert.Equal("GRN Reference", ws.Cell("H1").GetString());
        Assert.Equal("GRN-20261102-000002", ws.Cell("H2").GetString());
    }

    [Fact]
    public void UnloadingSheet_ListsEveryUploadedLine_WithAnAllocationRemark()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package()));
        var ws = workbook.Worksheet("Unloading Sheet");

        Assert.Equal(["Code", "Desc", "Qty", "Inbound Delv No", "Physical Qty", "Pkd/Batch", "Remarks"],
            Enumerable.Range(1, 7).Select(c => ws.Cell(8, c).GetString()));
        Assert.Equal("40039574", ws.Cell("A9").GetString());
        Assert.Equal(100m, ws.Cell("C9").GetValue<decimal>());
        Assert.Equal("DEC|2024", ws.Cell("F9").GetString());
        Assert.Equal("Fully allocated", ws.Cell("G9").GetString());
        Assert.Equal("Partially allocated", ws.Cell("G10").GetString());
        Assert.Equal("Not allocated", ws.Cell("G11").GetString());
    }

    [Fact]
    public void UnloadingSheet_ForRejectedGrn_MarksEveryLineRejected()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package(rejected: true)));
        var ws = workbook.Worksheet("Unloading Sheet");

        Assert.All(new[] { "G9", "G10", "G11" }, cell => Assert.Equal("GRN rejected", ws.Cell(cell).GetString()));
    }

    [Fact]
    public void UnloadingSheet_ForAHandEnteredGrn_WritesMinimalHeader()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package(manual: true)));
        var ws = workbook.Worksheet("Unloading Sheet");

        Assert.Equal("UNLOADING SHEET", ws.Cell("A1").GetString());
        Assert.Equal(string.Empty, ws.Cell("D2").GetString());
        Assert.Equal("LR NO :-", ws.Cell("C2").GetString());
        Assert.Equal("Code", ws.Cell("A8").GetString());
        Assert.Equal("Fully allocated", ws.Cell("G9").GetString());
    }

    [Fact]
    public void RowsOnlyOverload_StillWritesTheSingleInwardDownloadSheet()
    {
        using var workbook = Open(_service.WriteInwardDownload(Package().Rows));

        Assert.Equal(["Inward Download"], workbook.Worksheets.Select(w => w.Name));
        Assert.Equal("Material Code", workbook.Worksheet(1).Cell("A1").GetString());
    }

    // ───────────── upload reader ─────────────

    [Fact]
    public void ReadInwardWorkbook_CapturesTheFirstSevenRowsAndTheirMergedRanges()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");
        ws.Cell(1, 1).Value = "UNLOADING SHEET";
        ws.Range("A1:F1").Merge();
        ws.Cell(2, 1).Value = "Invoice No:-";
        ws.Cell(2, 2).Value = "INV-123";
        ws.Cell(2, 4).Value = 11223344;
        ws.Cell(3, 2).Value = new DateTime(2026, 11, 2);
        ws.Cell(3, 2).Style.DateFormat.Format = "dd/MM/yy";
        ws.Cell(3, 4).Value = "MH04AB1234";
        ws.Cell(5, 6).Value = "NAGPUR";
        ws.Cell(8, 1).Value = "Code";
        ws.Cell(9, 1).Value = 40039574;
        ws.Cell(9, 3).Value = 100;
        ws.Cell(9, 6).Value = "SEP|2026";
        ws.Cell(10, 1).Value = "Remark";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var sheet = Assert.Single(_service.ReadInwardWorkbook(stream));

        var block = InwardUploadHeaderBlock.TryParse(sheet.HeaderBlockJson);
        Assert.NotNull(block);
        Assert.Equal(7, block!.Rows.Count);
        Assert.Equal("UNLOADING SHEET", block.Rows[0][0]);
        Assert.Equal("INV-123", block.Rows[1][1]);
        Assert.Equal("11223344", block.Rows[1][3]);
        Assert.Equal("02/11/26", block.Rows[2][1]);
        Assert.Equal("NAGPUR", block.Rows[4][5]);
        Assert.Contains("A1:F1", block.MergedRanges);
        Assert.Equal("Code", ws.Cell(8, 1).GetString()); // row 8 (the table header) is not part of the block
        Assert.DoesNotContain(block.Rows, r => r.Contains("Code"));
    }

    [Fact]
    public void HeaderBlock_TryParse_ReturnsNullForMissingOrCorruptJson()
    {
        Assert.Null(InwardUploadHeaderBlock.TryParse(null));
        Assert.Null(InwardUploadHeaderBlock.TryParse("  "));
        Assert.Null(InwardUploadHeaderBlock.TryParse("{not json"));
    }

    // ───────────── history screens ─────────────

    private static (GodrejWMS.Infrastructure.Persistence.AppDbContext Db, Material Material) SeedHistory()
    {
        var db = TestDbContextFactory.Create();
        var material = new Material
        {
            MaterialNumber = 40039574, Description = "Cin Health+", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 100, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();
        return (db, material);
    }

    private static void AddGrn(GodrejWMS.Infrastructure.Persistence.AppDbContext db, Material material, string reference, string? vehicle, string? lr, bool pkmless)
    {
        db.InwardTransactions.Add(new InwardTransaction
        {
            ReferenceNumber = reference, VehicleNumber = vehicle, LrNumber = lr, IsPkmless = pkmless,
            CreatedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero),
            Lines = [new InwardTransactionLine { MaterialId = material.Id, RequestedQuantityBoxes = 10, Status = AllocationStatus.Fulfilled }]
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task InwardHistory_ReturnsVehicleAndLr_AndSearchMatchesThem()
    {
        var (db, material) = SeedHistory();
        AddGrn(db, material, "GRN-1", "MH04AB1234", "11223344", pkmless: false);
        AddGrn(db, material, "GRN-2", "HR68C6373", "77872", pkmless: false);
        var handler = new GetInwardHistoryHandler(db);

        var all = await handler.Handle(new GetInwardHistoryQuery(), CancellationToken.None);
        var byLr = await handler.Handle(new GetInwardHistoryQuery(Search: "77872"), CancellationToken.None);
        var byVehicle = await handler.Handle(new GetInwardHistoryQuery(Search: "MH04"), CancellationToken.None);

        Assert.Contains(all.Items, i => i is { ReferenceNumber: "GRN-1", VehicleNumber: "MH04AB1234", LrNumber: "11223344" });
        Assert.Equal("GRN-2", Assert.Single(byLr.Items).ReferenceNumber);
        Assert.Equal("GRN-1", Assert.Single(byVehicle.Items).ReferenceNumber);
    }

    [Fact]
    public async Task InwardNoPkmHistory_ReturnsVehicleAndLr_AndSearchMatchesThem()
    {
        var (db, material) = SeedHistory();
        AddGrn(db, material, "GRN-3", "MH04AB1234", "11223344", pkmless: true);
        AddGrn(db, material, "GRN-4", "HR68C6373", "77872", pkmless: true);
        var handler = new GetInwardHistoryWithoutPkmHandler(db);

        var all = await handler.Handle(new GetInwardHistoryWithoutPkmQuery(), CancellationToken.None);
        var byLr = await handler.Handle(new GetInwardHistoryWithoutPkmQuery(Search: "11223344"), CancellationToken.None);

        Assert.Contains(all.Items, i => i is { ReferenceNumber: "GRN-3", VehicleNumber: "MH04AB1234", LrNumber: "11223344" });
        Assert.Equal("GRN-3", Assert.Single(byLr.Items).ReferenceNumber);
    }
}
