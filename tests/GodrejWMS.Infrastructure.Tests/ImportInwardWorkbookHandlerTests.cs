using ClosedXML.Excel;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using MediatR;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>End-to-end: a multi-sheet Unloading Sheet workbook upload creates one GRN per order
/// sheet, with the two user-specified duplicate-safety rules - E2 (GRN Reference Number) already
/// present, then same-day LR No + Vehicle No + Received From match - checked in that order, plus
/// the "every row needs a Pkd/Batch or the whole sheet is skipped" rule. Parser-level behavior is
/// covered by <see cref="ReadInwardWorkbookTests"/>; this confirms the handler wires it to those
/// rules and to the existing <see cref="SubmitInwardCommand"/>/put-away pipeline correctly - and
/// that two GRNs created in the same request never collide on their reference number (the bug this
/// feature surfaced in the former second-precision "GRN-yyyyMMdd-HHmmss" reference).</summary>
public class ImportInwardWorkbookHandlerTests
{
    private static (AppDbContext Db, ImportInwardWorkbookHandler Handler) CreateHandler()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 2, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 10 });
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 2, LocationCode = "A-01-02", MaxPallets = 10 });
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40041315, Description = "Test Material", DesignType = "X",
            PackSize = 24, GrossWeightKg = 0.5m, LengthMm = 100, WidthMm = 100, HeightMm = 100,
            PalletCapacityBoxes = 1000, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();

        var submitHandler = new SubmitInwardHandler(db, new PalletAllocationService(db, new TestClock()), new TestClock(), new TestCurrentUser());
        var mediator = new SingleHandlerMediator(submitHandler);
        var handler = new ImportInwardWorkbookHandler(new ClosedXmlExcelService(), mediator, db, new TestClock());
        return (db, handler);
    }

    private static MemoryStream BuildWorkbook(params (string Name, string? Lr, string? Vehicle, string? ReceivedFrom, string? GrnRef, long? Code, decimal Qty, string? Pkm)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var s in sheets)
        {
            var ws = workbook.Worksheets.Add(s.Name);
            if (s.Lr is not null) ws.Cell(2, 4).Value = s.Lr;
            if (s.Vehicle is not null) ws.Cell(3, 4).Value = s.Vehicle;
            if (s.ReceivedFrom is not null) ws.Cell(5, 6).Value = s.ReceivedFrom;
            if (s.GrnRef is not null) ws.Cell(2, 5).Value = s.GrnRef;
            ws.Cell(8, 1).Value = "Code";
            ws.Cell(8, 2).Value = "Desc";
            ws.Cell(8, 3).Value = "Qty";
            ws.Cell(8, 6).Value = "Pkd/Batch";
            if (s.Code is { } code)
            {
                ws.Cell(9, 1).Value = code;
                ws.Cell(9, 2).Value = "Test Material";
                ws.Cell(9, 3).Value = s.Qty;
                if (s.Pkm is not null) ws.Cell(9, 6).Value = s.Pkm;
                ws.Cell(10, 1).Value = "Remark";
            }
            else
            {
                ws.Cell(9, 1).Value = "Remark";
            }
        }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task MultipleOrderSheets_EachCreatesItsOwnGrn_WithDistinctReferences()
    {
        var (db, handler) = CreateHandler();
        using var upload = BuildWorkbook(
            ("Sheet1", "77872", "HR68C6373", "Tepla PWH", null, 40041315, 10, "SEP|2026"),
            ("Sheet2", "77873", "HR68C9999", "Purna RWH", null, 40041315, 20, "SEP|2026"));

        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(upload, 1), CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(InwardSheetImportStatus.Created, o.Status));
        Assert.Equal(2, outcomes.Select(o => o.ReferenceNumber).Distinct().Count());
        Assert.Equal(2, db.InwardTransactions.Count());
    }

    [Fact]
    public async Task TwoSheetsInSameRequest_NeverCollideOnReferenceNumber_RegressionForFormerTimestampReference()
    {
        // The old "GRN-{yyyyMMdd-HHmmss}" reference collided whenever two GRNs were created
        // within the same second - routine for a multi-sheet upload processed in one request.
        var (db, handler) = CreateHandler();
        using var upload = BuildWorkbook(
            ("Sheet1", "77872", "HR68C6373", "Tepla PWH", null, 40041315, 10, "SEP|2026"),
            ("Sheet2", "77873", "HR68C9999", "Purna RWH", null, 40041315, 20, "SEP|2026"),
            ("Sheet3", "77874", "HR68C0000", "Indore RWH", null, 40041315, 15, "SEP|2026"));

        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(upload, 1), CancellationToken.None);

        Assert.All(outcomes, o => Assert.Equal(InwardSheetImportStatus.Created, o.Status));
        var references = outcomes.Select(o => o.ReferenceNumber).ToList();
        Assert.Equal(3, references.Distinct().Count());
        Assert.Equal(3, db.InwardTransactions.Select(t => t.ReferenceNumber).Distinct().Count());
    }

    [Fact]
    public async Task SheetWithGrnReferenceAlreadyAtE2_IsSkipped_NoDbLookupNeeded()
    {
        var (db, handler) = CreateHandler();
        using var upload = BuildWorkbook(
            ("Sheet1", "77872", "HR68C6373", "Tepla PWH", "GRN-20261031-000005", 40041315, 10, "SEP|2026"));

        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(upload, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(InwardSheetImportStatus.SkippedDuplicateReference, outcome.Status);
        Assert.Equal("GRN-20261031-000005", outcome.ReferenceNumber);
        Assert.Empty(db.InwardTransactions);
    }

    [Fact]
    public async Task SameLrVehicleReceivedFrom_AlreadyReceivedToday_IsSkipped()
    {
        var (db, handler) = CreateHandler();
        using var first = BuildWorkbook(("Sheet1", "77872", "HR68C6373", "Tepla PWH", null, 40041315, 10, "SEP|2026"));
        await handler.Handle(new ImportInwardWorkbookCommand(first, 1), CancellationToken.None);

        using var second = BuildWorkbook(("Sheet1-resent", "77872", "HR68C6373", "Tepla PWH", null, 40041315, 15, "SEP|2026"));
        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(second, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(InwardSheetImportStatus.SkippedDuplicateLrVehicleSource, outcome.Status);
        Assert.Single(db.InwardTransactions); // only the first upload's GRN exists
    }

    [Fact]
    public async Task DifferentLrNumber_SameVehicleAndSource_IsNotTreatedAsDuplicate()
    {
        var (db, handler) = CreateHandler();
        using var first = BuildWorkbook(("Sheet1", "77872", "HR68C6373", "Tepla PWH", null, 40041315, 10, "SEP|2026"));
        await handler.Handle(new ImportInwardWorkbookCommand(first, 1), CancellationToken.None);

        using var second = BuildWorkbook(("Sheet2", "99999", "HR68C6373", "Tepla PWH", null, 40041315, 15, "SEP|2026"));
        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(second, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(InwardSheetImportStatus.Created, outcome.Status);
        Assert.Equal(2, db.InwardTransactions.Count());
    }

    [Fact]
    public async Task SheetWithAnyRowMissingPkdBatch_SkipsTheWholeSheet()
    {
        var (db, handler) = CreateHandler();
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Sheet1");
        ws.Cell(3, 4).Value = "HR68C6373";
        ws.Cell(2, 4).Value = "77872";
        ws.Cell(5, 6).Value = "Tepla PWH";
        ws.Cell(8, 1).Value = "Code";
        ws.Cell(9, 1).Value = 40041315;
        ws.Cell(9, 2).Value = "Test Material";
        ws.Cell(9, 3).Value = 10;
        ws.Cell(9, 6).Value = "SEP|2026";
        ws.Cell(10, 1).Value = 40041315;
        ws.Cell(10, 2).Value = "Another Material";
        ws.Cell(10, 3).Value = 5;
        // row 10 col 6 (Pkd/Batch) left blank
        ws.Cell(11, 1).Value = "Remark";
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(stream, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(InwardSheetImportStatus.SkippedMissingPkm, outcome.Status);
        Assert.Contains("row(s) 10", outcome.Message);
        Assert.Empty(db.InwardTransactions);
    }

    [Fact]
    public async Task RejectedGrn_DoesNotCountAsAlreadyReceivedToday()
    {
        var (db, handler) = CreateHandler();
        using var first = BuildWorkbook(("Sheet1", "77872", "HR68C6373", "Tepla PWH", null, 40041315, 10, "SEP|2026"));
        await handler.Handle(new ImportInwardWorkbookCommand(first, 1), CancellationToken.None);
        db.InwardTransactions.Single().IsRejected = true;
        await db.SaveChangesAsync(CancellationToken.None);

        using var second = BuildWorkbook(("Sheet1-retry", "77872", "HR68C6373", "Tepla PWH", null, 40041315, 10, "SEP|2026"));
        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(second, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(InwardSheetImportStatus.Created, outcome.Status);
    }

    [Fact]
    public async Task NonOrderSheet_And_NoDataSheet_AreReportedAsSkipped_NotErrors()
    {
        var (db, handler) = CreateHandler();
        using var workbook = new XLWorkbook();
        var mis = workbook.Worksheets.Add("MIS");
        mis.Cell(1, 1).Value = "PLANT";
        var blank = workbook.Worksheets.Add("Sheet19");
        blank.Cell(8, 1).Value = "Code";
        blank.Cell(9, 1).Value = "Remark";
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var outcomes = await handler.Handle(new ImportInwardWorkbookCommand(stream, 1), CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(InwardSheetImportStatus.SkippedNotOrderSheet, outcomes[0].Status);
        Assert.Equal(InwardSheetImportStatus.SkippedNoData, outcomes[1].Status);
        Assert.Empty(db.InwardTransactions);
    }

    private sealed class TestCurrentUser : GodrejWMS.Application.Common.Interfaces.ICurrentUserService
    {
        public string? UserId => "test-user";
        public string? UserName => "test@godrejwms.local";
        public bool IsInRole(string role) => true;
        public IReadOnlyList<string> Roles => ["Superadmin"];
    }

    private sealed class TestClock : GodrejWMS.Application.Common.Interfaces.IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 10, 31);
    }

    /// <summary>Forwards only <see cref="SubmitInwardCommand"/> to a real handler - enough for
    /// <see cref="ImportInwardWorkbookHandler"/>, which never sends anything else.</summary>
    private sealed class SingleHandlerMediator(SubmitInwardHandler submitHandler) : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is SubmitInwardCommand command)
            {
                return (Task<TResponse>)(object)submitHandler.Handle(command, cancellationToken);
            }
            throw new NotSupportedException($"Unexpected request type: {request.GetType()}");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            throw new NotSupportedException();
    }
}
