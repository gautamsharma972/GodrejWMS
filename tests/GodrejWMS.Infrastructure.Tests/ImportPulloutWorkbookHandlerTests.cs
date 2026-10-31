using ClosedXML.Excel;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using MediatR;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>End-to-end: a multi-sheet workbook upload creates one pullout per order sheet, with
/// the two user-specified duplicate-safety rules - H10 (Pick Reference Number) already present,
/// then same-day Destination+Vehicle No match - checked in that order. Parser-level behavior
/// (column layout, "Driver Sign" stop condition, non-order-sheet detection) is covered by
/// <see cref="ReadPulloutWorkbookTests"/>; this confirms the handler wires it to those rules and to
/// the existing <see cref="SubmitPulloutCommand"/> pipeline correctly.</summary>
public class ImportPulloutWorkbookHandlerTests
{
    private static (AppDbContext Db, ImportPulloutWorkbookHandler Handler) CreateHandler()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 10 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = new Material { MaterialNumber = 40063045, Description = "Test Material", DesignType = "X", SeasonId = SeasonIds.Rainy };
        db.Materials.Add(material);
        db.SaveChangesAsync().GetAwaiter().GetResult();
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 1000 });
        db.SaveChangesAsync().GetAwaiter().GetResult();

        var submitHandler = new SubmitPulloutHandler(db, new PulloutAllocationService(db), new TestClock(), new TestCurrentUser());
        var mediator = new SingleHandlerMediator(submitHandler);
        var handler = new ImportPulloutWorkbookHandler(new ClosedXmlExcelService(), mediator, db, new TestClock());
        return (db, handler);
    }

    private static MemoryStream BuildWorkbook(params (string Name, string? Vehicle, string? Destination, string? PickReference, long? Code, decimal Qty)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var s in sheets)
        {
            var ws = workbook.Worksheets.Add(s.Name);
            if (s.Vehicle is not null) ws.Cell(9, 2).Value = s.Vehicle;
            if (s.Destination is not null) ws.Cell(10, 2).Value = s.Destination;
            if (s.PickReference is not null) ws.Cell(10, 8).Value = s.PickReference;
            ws.Cell(12, 1).Value = "Code";
            ws.Cell(12, 2).Value = "Description";
            ws.Cell(12, 3).Value = "Del Qty";
            if (s.Code is { } code)
            {
                ws.Cell(13, 1).Value = code;
                ws.Cell(13, 2).Value = "Test Material";
                ws.Cell(13, 3).Value = s.Qty;
                ws.Cell(14, 1).Value = "Driver Sign";
            }
            else
            {
                ws.Cell(13, 1).Value = "Driver Sign";
            }
        }
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task MultipleOrderSheets_EachCreatesItsOwnPullout_WithDistinctReferences()
    {
        var (db, handler) = CreateHandler();
        using var upload = BuildWorkbook(
            ("PONDY-1", "NL01AG5387", "PONDY-1", null, 40063045, 10),
            ("AHMD-1", "RJ09GE6858", "AHMD-1", null, 40063045, 20));

        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(upload, 1), CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(PulloutSheetImportStatus.Created, o.Status));
        Assert.Equal(2, outcomes.Select(o => o.ReferenceNumber).Distinct().Count());
        Assert.Equal(2, db.PulloutTransactions.Count());
    }

    [Fact]
    public async Task SheetWithPickReferenceAlreadyAtH10_IsSkipped_NoDbLookupNeeded()
    {
        var (db, handler) = CreateHandler();
        using var upload = BuildWorkbook(
            ("PONDY-1", "NL01AG5387", "PONDY-1", "PICK-20261031-000005", 40063045, 10));

        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(upload, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(PulloutSheetImportStatus.SkippedDuplicateReference, outcome.Status);
        Assert.Equal("PICK-20261031-000005", outcome.ReferenceNumber);
        Assert.Empty(db.PulloutTransactions);
    }

    [Fact]
    public async Task SameDestinationAndVehicle_AlreadyPulledToday_IsSkipped()
    {
        var (db, handler) = CreateHandler();
        using var first = BuildWorkbook(("PONDY-1", "NL01AG5387", "PONDY-1", null, 40063045, 10));
        await handler.Handle(new ImportPulloutWorkbookCommand(first, 1), CancellationToken.None);

        using var second = BuildWorkbook(("PONDY-1-RESENT", "NL01AG5387", "PONDY-1", null, 40063045, 15));
        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(second, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(PulloutSheetImportStatus.SkippedDuplicateDestinationVehicle, outcome.Status);
        Assert.Single(db.PulloutTransactions); // only the first upload's pullout exists
    }

    [Fact]
    public async Task DifferentVehicle_SameDestination_IsNotTreatedAsDuplicate()
    {
        var (db, handler) = CreateHandler();
        using var first = BuildWorkbook(("PONDY-1", "NL01AG5387", "PONDY-1", null, 40063045, 10));
        await handler.Handle(new ImportPulloutWorkbookCommand(first, 1), CancellationToken.None);

        using var second = BuildWorkbook(("PONDY-2", "RJ09GE6858", "PONDY-1", null, 40063045, 15));
        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(second, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(PulloutSheetImportStatus.Created, outcome.Status);
        Assert.Equal(2, db.PulloutTransactions.Count());
    }

    [Fact]
    public async Task DifferentDestination_SameVehicle_IsNotTreatedAsDuplicate()
    {
        var (db, handler) = CreateHandler();
        using var first = BuildWorkbook(("PONDY-1", "NL01AG5387", "PONDY-1", null, 40063045, 10));
        await handler.Handle(new ImportPulloutWorkbookCommand(first, 1), CancellationToken.None);

        using var second = BuildWorkbook(("AHMD-1", "NL01AG5387", "AHMD-1", null, 40063045, 15));
        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(second, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(PulloutSheetImportStatus.Created, outcome.Status);
        Assert.Equal(2, db.PulloutTransactions.Count());
    }

    [Fact]
    public async Task RejectedPullout_DoesNotCountAsAlreadyPulledToday()
    {
        var (db, handler) = CreateHandler();
        using var first = BuildWorkbook(("PONDY-1", "NL01AG5387", "PONDY-1", null, 40063045, 10));
        await handler.Handle(new ImportPulloutWorkbookCommand(first, 1), CancellationToken.None);
        db.PulloutTransactions.Single().IsRejected = true;
        await db.SaveChangesAsync(CancellationToken.None);

        using var second = BuildWorkbook(("PONDY-1-RETRY", "NL01AG5387", "PONDY-1", null, 40063045, 10));
        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(second, 1), CancellationToken.None);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(PulloutSheetImportStatus.Created, outcome.Status);
    }

    [Fact]
    public async Task NonOrderSheet_And_NoDataSheet_AreReportedAsSkipped_NotErrors()
    {
        var (db, handler) = CreateHandler();
        using var workbook = new XLWorkbook();
        var mis = workbook.Worksheets.Add("MIS");
        mis.Cell(1, 1).Value = "PLANT";
        var blank = workbook.Worksheets.Add("Sheet19");
        blank.Cell(12, 1).Value = "Code";
        blank.Cell(13, 1).Value = "Driver Sign";
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(stream, 1), CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(PulloutSheetImportStatus.SkippedNotOrderSheet, outcomes[0].Status);
        Assert.Equal(PulloutSheetImportStatus.SkippedNoData, outcomes[1].Status);
        Assert.Empty(db.PulloutTransactions);
    }

    [Fact]
    public async Task SheetWithUnknownMaterialCode_StillCreatesAPullout_AndLaterSheetsStillProcess()
    {
        // A material code missing from Material Master doesn't throw - SubmitPulloutHandler
        // reports it as a Failed-status line on an otherwise normally-created pullout. The real
        // "doesn't abort the batch" guarantee is the try/catch around each sheet's SubmitPulloutCommand
        // call in ImportPulloutWorkbookHandler; this confirms that path doesn't accidentally skip
        // or short-circuit the rest of the batch for a sheet with this kind of data problem.
        var (db, handler) = CreateHandler();
        using var upload = BuildWorkbook(
            ("BadMaterial", "NL01AG5387", "PONDY-1", null, 99999999, 10),
            ("Good", "RJ09GE6858", "AHMD-1", null, 40063045, 10));

        var outcomes = await handler.Handle(new ImportPulloutWorkbookCommand(upload, 1), CancellationToken.None);

        Assert.Equal(2, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(PulloutSheetImportStatus.Created, o.Status));
        Assert.Equal(2, db.PulloutTransactions.Count());
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

    private sealed class SingleHandlerMediator(SubmitPulloutHandler submitHandler) : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is SubmitPulloutCommand command)
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
