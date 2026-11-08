using ClosedXML.Excel;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using MediatR;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>A GRN must account for every box that was uploaded. A line whose material code is not in the Material
/// Master, or a sheet row whose code cannot be read, used to vanish from the saved GRN - so the screen showed the
/// allocated volume as the inbound volume and "100% allocated" for an upload that was only partly placed. These
/// lines are now recorded, counted in the inbound volume, shown as not allocated, and listed on the Unloading Sheet.</summary>
public class InwardUnsavedLinesTests
{
    private const long KnownMaterial = 40063427;
    private const long UnknownMaterial = 49999999;

    private static (AppDbContext Db, Material Material) Seed()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 2, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 10 });
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 2, LocationCode = "A-01-02", MaxPallets = 10 });
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = KnownMaterial, Description = "EXPERT CREME NAT BWN", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 1000, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();
        return (db, material);
    }

    private static SubmitInwardHandler PkmHandler(AppDbContext db) =>
        new(db, TestServices.Placement(db, new PalletAllocationService(db, new Clock())), TestServices.Uow(db), new Clock(), new User());

    private static SubmitInwardWithoutPkmHandler NoPkmHandler(AppDbContext db) =>
        new(db, TestServices.Placement(db, new PalletAllocationService(db, new Clock())), TestServices.Uow(db), new Clock(), new User());

    // ───────────── totals ─────────────

    [Theory]
    [InlineData(1137, 1535, 74.07)]
    [InlineData(1535, 1535, 100)]
    [InlineData(0, 1535, 0)]
    [InlineData(10, 0, 0)]
    public void Percent_IsMeasuredAgainstTheWholeUpload(double allocated, double uploaded, double expected) =>
        Assert.Equal(expected, (double)InwardTotals.Percent((decimal)allocated, (decimal)uploaded), 2);

    // ───────────── submit: unknown material ─────────────

    [Fact]
    public async Task Submit_RecordsALineWhoseMaterialIsNotInTheMaster_AndCountsItInTheInboundVolume()
    {
        var (db, _) = Seed();

        var result = await PkmHandler(db).Handle(
            new SubmitInwardCommand(
            [
                new SubmitInwardLine(KnownMaterial, 100, "MAR|2026"),
                new SubmitInwardLine(UnknownMaterial, 50, "MAR|2026")
            ], WarehouseId: 1),
            CancellationToken.None);

        var saved = Assert.Single(db.InwardUnsavedLines);
        Assert.Equal(("49999999", 50m, InwardUnsavedLineKind.UnknownMaterial), (saved.CodeText, saved.QuantityBoxes, saved.Kind));
        Assert.Contains("not found in Material Master", saved.Reason);
        Assert.Single(db.InwardTransactionLines);                     // only the known material became a line
        Assert.Equal(150m, InwardTotals.Uploaded(result));            // the result already lists the failed line: no double count
        Assert.Equal(InwardUnsavedLineKind.UnknownMaterial, Assert.Single(result.UnsavedLines!).Kind);
    }

    [Fact]
    public async Task Detail_ReportsTheWholeUploadAsInboundVolume_AndAPercentageAgainstIt()
    {
        var (db, _) = Seed();
        var submitted = await PkmHandler(db).Handle(
            new SubmitInwardCommand(
            [
                new SubmitInwardLine(KnownMaterial, 100, "MAR|2026"),
                new SubmitInwardLine(UnknownMaterial, 50, "MAR|2026")
            ], WarehouseId: 1),
            CancellationToken.None);

        var detail = await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(submitted.Id), CancellationToken.None);

        Assert.Equal(150m, InwardTotals.Uploaded(detail));
        Assert.Equal(100m, InwardTotals.Allocated(detail));
        Assert.Equal(50m, InwardTotals.UnsavedBoxes(detail));
        Assert.Equal(66.67, (double)InwardTotals.Percent(InwardTotals.Allocated(detail), InwardTotals.Uploaded(detail)), 2);
        Assert.Equal("49999999", Assert.Single(detail.UnsavedLines!).CodeText);
    }

    [Fact]
    public async Task Detail_OfAFullyAllocatedGrn_HasNoUnsavedLines_AndTheSameTotalsAsBefore()
    {
        var (db, _) = Seed();
        var submitted = await PkmHandler(db).Handle(
            new SubmitInwardCommand([new SubmitInwardLine(KnownMaterial, 100, "MAR|2026")], WarehouseId: 1), CancellationToken.None);

        var detail = await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(submitted.Id), CancellationToken.None);

        Assert.Empty(detail.UnsavedLines!);
        Assert.Equal(100m, InwardTotals.Uploaded(detail));
        Assert.Equal(100, (double)InwardTotals.Percent(InwardTotals.Allocated(detail), InwardTotals.Uploaded(detail)));
    }

    [Fact]
    public async Task SubmitWithoutPkm_AlsoRecordsAnUnknownMaterialLine()
    {
        var (db, _) = Seed();

        var result = await NoPkmHandler(db).Handle(
            new SubmitInwardWithoutPkmCommand(
            [
                new SubmitInwardWithoutPkmLine(KnownMaterial, 100),
                new SubmitInwardWithoutPkmLine(UnknownMaterial, 398)
            ], WarehouseId: 1),
            CancellationToken.None);

        var saved = Assert.Single(db.InwardUnsavedLines);
        Assert.Equal(("49999999", 398m), (saved.CodeText, saved.QuantityBoxes));
        Assert.Equal(498m, InwardTotals.Uploaded(result));
    }

    // ───────────── submit: unreadable rows ─────────────

    [Fact]
    public async Task Submit_RecordsUnreadableSheetRows_AndCountsThemInTheInboundVolume()
    {
        var (db, _) = Seed();

        var result = await PkmHandler(db).Handle(
            new SubmitInwardCommand([new SubmitInwardLine(KnownMaterial, 100, "MAR|2026")], WarehouseId: 1,
                UnreadableRows: [new InwardUnsavedLineInput("4006342A", 40, "Row 11: \"4006342A\" is not a valid material code.", 11)]),
            CancellationToken.None);

        var saved = Assert.Single(db.InwardUnsavedLines);
        Assert.Equal((InwardUnsavedLineKind.UnreadableRow, 11, 40m), (saved.Kind, saved.SourceRow, saved.QuantityBoxes));
        Assert.Equal(140m, InwardTotals.Uploaded(result));            // unreadable rows are not in result.Lines, so they add on top
    }

    // ───────────── sheet reader ─────────────

    private static MemoryStream Sheet(Action<IXLWorksheet> rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");
        ws.Cell(2, 4).Value = "77872"; ws.Cell(3, 4).Value = "HR68C6373"; ws.Cell(5, 6).Value = "Tepla PWH";
        ws.Cell(8, 1).Value = "Code"; ws.Cell(8, 2).Value = "Desc"; ws.Cell(8, 3).Value = "Qty"; ws.Cell(8, 6).Value = "Pkd/Batch";
        rows(ws);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void ReadInwardWorkbook_RecordsARowWithAnUnreadableCodeAndQuantity_ButIgnoresBlankHeaderAndTotalRows()
    {
        using var stream = Sheet(ws =>
        {
            ws.Cell(9, 1).Value = KnownMaterial; ws.Cell(9, 3).Value = 100; ws.Cell(9, 6).Value = "SEP|2026";
            ws.Cell(10, 1).Value = "4006342A"; ws.Cell(10, 3).Value = 40;                 // unreadable code with boxes -> recorded
            ws.Cell(11, 3).Value = 5;                                                       // blank code -> ignored
            ws.Cell(12, 1).Value = "Code"; ws.Cell(12, 3).Value = "Qty";                   // repeated header -> ignored
            ws.Cell(13, 1).Value = "Total"; ws.Cell(13, 3).Value = 145;                     // total line -> ignored
            ws.Cell(14, 1).Value = "Remark";
        });

        var sheet = Assert.Single(new ClosedXmlExcelService().ReadInwardWorkbook(stream));

        Assert.Single(sheet.Rows);
        var unreadable = Assert.Single(sheet.UnreadableRows!);
        Assert.Equal(("4006342A", 40m, 10), (unreadable.CodeText, unreadable.QuantityBoxes, unreadable.SourceRow));
    }

    [Fact]
    public async Task Import_StoresTheUnreadableRow_OnTheGrn_AndMentionsItInTheSummary()
    {
        var (db, _) = Seed();
        var handler = new ImportInwardWorkbookHandler(new ClosedXmlExcelService(), new SubmitOnlyMediator(PkmHandler(db)), db, new Clock());
        using var stream = Sheet(ws =>
        {
            ws.Cell(9, 1).Value = KnownMaterial; ws.Cell(9, 3).Value = 100; ws.Cell(9, 6).Value = "SEP|2026";
            ws.Cell(10, 1).Value = "4006342A"; ws.Cell(10, 3).Value = 40;
            ws.Cell(11, 1).Value = "Remark";
        });

        var outcome = Assert.Single(await handler.Handle(new ImportInwardWorkbookCommand(stream, 1), CancellationToken.None));

        Assert.Equal(InwardSheetImportStatus.Created, outcome.Status);
        Assert.Contains("not a valid number", outcome.Message);
        Assert.Contains("40", outcome.Message);
        var saved = Assert.Single(db.InwardUnsavedLines);
        Assert.Equal(("4006342A", InwardUnsavedLineKind.UnreadableRow, 10), (saved.CodeText, saved.Kind, saved.SourceRow));
    }

    // ───────────── downloads ─────────────

    [Fact]
    public async Task DownloadFromDetail_ListsUnsavedLinesOnTheUnloadingSheet_AsNotAllocated()
    {
        var (db, _) = Seed();
        var submitted = await PkmHandler(db).Handle(
            new SubmitInwardCommand(
            [
                new SubmitInwardLine(KnownMaterial, 100, "MAR|2026"),
                new SubmitInwardLine(UnknownMaterial, 50, "MAR|2026")
            ], WarehouseId: 1,
            UnreadableRows: [new InwardUnsavedLineInput("4006342A", 40, "Row 11: \"4006342A\" is not a valid material code.", 11)]),
            CancellationToken.None);
        var detail = await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(submitted.Id), CancellationToken.None);

        using var workbook = new XLWorkbook(new MemoryStream(new ClosedXmlExcelService().WriteInwardDownload(InwardDownloadMapper.PackageFromDetail(detail))));
        var ws = workbook.Worksheet("Unloading Sheet");

        Assert.Equal("Fully allocated", ws.Cell("G9").GetString());
        // Unreadable rows keep their sheet position (they carry a row number); unknown-material lines have none, so follow them.
        Assert.Equal("4006342A", ws.Cell("A10").GetString());
        Assert.Equal(40m, ws.Cell("C10").GetValue<decimal>());
        Assert.StartsWith("Not allocated - Row 11", ws.Cell("G10").GetString());
        Assert.Equal("49999999", ws.Cell("A11").GetString());
        Assert.Equal(50m, ws.Cell("C11").GetValue<decimal>());
        Assert.StartsWith("Not allocated - Material code not found", ws.Cell("G11").GetString());
        Assert.Equal(string.Empty, ws.Cell("A12").GetString());                          // nothing listed twice
    }

    [Fact]
    public async Task DownloadFromResult_ListsEachUnsavedLineExactlyOnce()
    {
        var (db, _) = Seed();
        var result = await PkmHandler(db).Handle(
            new SubmitInwardCommand(
            [
                new SubmitInwardLine(KnownMaterial, 100, "MAR|2026"),
                new SubmitInwardLine(UnknownMaterial, 50, "MAR|2026")
            ], WarehouseId: 1,
            UnreadableRows: [new InwardUnsavedLineInput("4006342A", 40, "Row 11: bad code.", 11)]),
            CancellationToken.None);

        var package = InwardDownloadMapper.PackageFromResult(result);

        Assert.Equal(["40063427", "4006342A", "49999999"], package.UnloadingRows.Select(r => r.Code).Order(StringComparer.Ordinal));
        Assert.Equal(3, package.UnloadingRows.Count);
        Assert.StartsWith("Not allocated - Material code not found", package.UnloadingRows.Single(r => r.Code == "49999999").Remarks);
    }

    // ───────────── the name typed in the uploaded sheet is kept ─────────────

    [Fact]
    public async Task ImportKeepsTheNameFromTheSheet_ForAnUnknownMaterial_AndTheUnloadingSheetShowsIt()
    {
        var (db, _) = Seed();
        var handler = new ImportInwardWorkbookHandler(new ClosedXmlExcelService(), new SubmitOnlyMediator(PkmHandler(db)), db, new Clock());
        using var stream = Sheet(ws =>
        {
            ws.Cell(9, 1).Value = KnownMaterial; ws.Cell(9, 2).Value = "EXPERT CREME NAT BWN"; ws.Cell(9, 3).Value = 100; ws.Cell(9, 6).Value = "SEP|2026";
            ws.Cell(10, 1).Value = 400100202; ws.Cell(10, 2).Value = "HIT CIK 320ML M210.00 P50"; ws.Cell(10, 3).Value = 60; ws.Cell(10, 6).Value = "SEP|2026";
            ws.Cell(11, 1).Value = "4006342A"; ws.Cell(11, 2).Value = "PA GM Shaving Brush"; ws.Cell(11, 3).Value = 40;
            ws.Cell(12, 1).Value = "Remark";
        });

        await handler.Handle(new ImportInwardWorkbookCommand(stream, 1), CancellationToken.None);

        Assert.Equal(["HIT CIK 320ML M210.00 P50", "PA GM Shaving Brush"], db.InwardUnsavedLines.OrderBy(u => u.CodeText).Select(u => u.Description!).ToArray().Order().ToArray());
        var detail = await new GetInwardDetailHandler(db).Handle(new GetInwardDetailQuery(db.InwardTransactions.Single().Id), CancellationToken.None);
        using var workbook = new XLWorkbook(new MemoryStream(new ClosedXmlExcelService().WriteInwardDownload(InwardDownloadMapper.PackageFromDetail(detail))));
        var ws = workbook.Worksheet("Unloading Sheet");
        var byCode = Enumerable.Range(9, 3).ToDictionary(r => ws.Cell(r, 1).GetString(), r => ws.Cell(r, 2).GetString());

        Assert.Equal("EXPERT CREME NAT BWN", byCode["40063427"]);
        Assert.Equal("HIT CIK 320ML M210.00 P50", byCode["400100202"]);      // not "(unknown material)"
        Assert.Equal("PA GM Shaving Brush", byCode["4006342A"]);
        Assert.DoesNotContain(byCode.Values, v => v.Contains("unknown material"));
    }

    [Fact]
    public async Task DownloadRightAfterUpload_AlsoUsesTheSheetsName_AndLeavesItBlankWhenNoneWasGiven()
    {
        var (db, _) = Seed();
        var result = await PkmHandler(db).Handle(
            new SubmitInwardCommand(
            [
                new SubmitInwardLine(KnownMaterial, 100, "MAR|2026"),
                new SubmitInwardLine(400100202, 60, "MAR|2026", Description: "HIT CIK 320ML M210.00 P50"),
                new SubmitInwardLine(UnknownMaterial, 5, "MAR|2026")                       // manual entry: no name available
            ], WarehouseId: 1),
            CancellationToken.None);

        var rows = InwardDownloadMapper.PackageFromResult(result).UnloadingRows.ToDictionary(r => r.Code);

        Assert.Equal("HIT CIK 320ML M210.00 P50", rows["400100202"].MaterialDescription);
        Assert.Equal(string.Empty, rows["49999999"].MaterialDescription);                 // blank, not a placeholder
        Assert.StartsWith("Not allocated", rows["400100202"].Remarks);
        // the on-screen result still shows the uploaded name for the failed line, and a placeholder only without one
        Assert.Equal("HIT CIK 320ML M210.00 P50", result.Lines.Single(l => l.MaterialNumber == 400100202).MaterialDescription);
        Assert.Equal("(unknown material)", result.Lines.Single(l => l.MaterialNumber == UnknownMaterial).MaterialDescription);
    }

    // ───────────── test doubles ─────────────

    private sealed class Clock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

        public DateOnly Today => new(2026, 11, 2);
    }

    private sealed class User : ICurrentUserService
    {
        public string? UserId => "test-user";

        public string? UserName => "test@godrejwms.local";

        public bool IsInRole(string role) => true;

        public IReadOnlyList<string> Roles => ["Superadmin"];
    }

    private sealed class SubmitOnlyMediator(SubmitInwardHandler submit) : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            request is SubmitInwardCommand command
                ? (Task<TResponse>)(object)submit.Handle(command, cancellationToken)
                : throw new NotSupportedException(request.GetType().Name);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw new NotSupportedException();
    }
}
