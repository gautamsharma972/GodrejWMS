using System.Text;
using ClosedXML.Excel;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using MediatR;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers the real-time scenario behind the "Indent" duplicate check: a client re-uploads
/// a growing, same-day Loading Sheet workbook. The importer must recognize an order it already
/// turned into a pullout - whether by the client's own Indent text or by our own reference number
/// written back onto a previously-downloaded sheet - and reject the re-upload with a clear message
/// instead of creating a second pullout.</summary>
public class ImportPulloutDuplicateIndentTests
{
    private static (AppDbContext Db, ImportPulloutFromExcelHandler Handler) CreateHandler()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 10 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = new Material { MaterialNumber = 40063045, Description = "Test Material", DesignType = "X", SeasonId = SeasonIds.Rainy };
        db.Materials.Add(material);
        db.SaveChangesAsync().GetAwaiter().GetResult();
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 100 });
        db.SaveChangesAsync().GetAwaiter().GetResult();

        var submitHandler = new SubmitPulloutHandler(db, new PulloutAllocationService(db), new TestClock(), new TestCurrentUser());
        var mediator = new SingleHandlerMediator(submitHandler);
        var handler = new ImportPulloutFromExcelHandler(new ClosedXmlExcelService(), mediator, db);
        return (db, handler);
    }

    private static MemoryStream BuildUploadSheet(string? indentText)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Loading Sheet");
        ws.Cell(10, 6).Value = "Indent";
        if (indentText is not null)
        {
            ws.Cell(10, 7).Value = indentText;
        }

        string[] headers = ["Code", "Description", "Del Qty", "Priority"];
        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(11, c + 1).Value = headers[c];
        }

        ws.Cell(12, 1).Value = 40063045;
        ws.Cell(12, 2).Value = "Test Material";
        ws.Cell(12, 3).Value = 10;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task SecondUpload_WithSameClientIndentNumber_IsRejectedAsDuplicate()
    {
        var (db, handler) = CreateHandler();

        using var first = BuildUploadSheet("GCPL-C121-O-12950");
        var firstResult = await handler.Handle(new ImportPulloutFromExcelCommand(first, 1), CancellationToken.None);
        Assert.Single(db.PulloutTransactions);
        Assert.Equal("GCPL-C121-O-12950", db.PulloutTransactions.Single().ClientIndentNumber);

        using var second = BuildUploadSheet("GCPL-C121-O-12950");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new ImportPulloutFromExcelCommand(second, 1), CancellationToken.None));

        Assert.Contains(firstResult.ReferenceNumber, exception.Message);
        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(db.PulloutTransactions); // no second pullout was created
    }

    [Fact]
    public async Task SecondUpload_WithIndentCaseDifference_IsStillCaughtAsDuplicate()
    {
        var (db, handler) = CreateHandler();

        using var first = BuildUploadSheet("gcpl-c121-o-12950");
        await handler.Handle(new ImportPulloutFromExcelCommand(first, 1), CancellationToken.None);

        using var second = BuildUploadSheet("GCPL-C121-O-12950");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new ImportPulloutFromExcelCommand(second, 1), CancellationToken.None));

        Assert.Single(db.PulloutTransactions);
    }

    [Fact]
    public async Task Reupload_WithOurOwnPreviouslyIssuedReferenceNumberInIndentCell_IsRejectedAsDuplicate()
    {
        var (db, handler) = CreateHandler();

        using var first = BuildUploadSheet(indentText: null);
        var firstResult = await handler.Handle(new ImportPulloutFromExcelCommand(first, 1), CancellationToken.None);

        // Simulates the client downloading our Loading Sheet (which stamps the Indent cell with our
        // own ReferenceNumber) and later re-uploading that same file, or a sheet copied from it.
        using var reupload = BuildUploadSheet(firstResult.ReferenceNumber);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new ImportPulloutFromExcelCommand(reupload, 1), CancellationToken.None));

        Assert.Contains(firstResult.ReferenceNumber, exception.Message);
        Assert.Single(db.PulloutTransactions);
    }

    [Fact]
    public async Task Reupload_WithBothOurReferenceAndClientIndentStackedOnTwoLines_IsRejectedAsDuplicate()
    {
        var (db, handler) = CreateHandler();

        using var first = BuildUploadSheet("GCPL-C121-O-12950");
        var firstResult = await handler.Handle(new ImportPulloutFromExcelCommand(first, 1), CancellationToken.None);

        // The client's own manual note sits on a second line in the same Indent cell, below our
        // reference number that got written back onto their downloaded copy.
        using var reupload = BuildUploadSheet($"{firstResult.ReferenceNumber}\nGCPL-C121-O-12950");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new ImportPulloutFromExcelCommand(reupload, 1), CancellationToken.None));

        Assert.Single(db.PulloutTransactions);
    }

    [Fact]
    public async Task NewOrder_WithDifferentIndentNumber_CreatesANewPullout()
    {
        var (db, handler) = CreateHandler();

        using var first = BuildUploadSheet("GCPL-C121-O-12950");
        await handler.Handle(new ImportPulloutFromExcelCommand(first, 1), CancellationToken.None);

        using var second = BuildUploadSheet("GCPL-C121-O-99999");
        await handler.Handle(new ImportPulloutFromExcelCommand(second, 1), CancellationToken.None);

        Assert.Equal(2, db.PulloutTransactions.Count());
    }

    [Fact]
    public async Task RejectedPulloutsIndentNumber_CanBeReused_WithoutTriggeringDuplicate()
    {
        var (db, handler) = CreateHandler();

        using var first = BuildUploadSheet("GCPL-C121-O-12950");
        var firstResult = await handler.Handle(new ImportPulloutFromExcelCommand(first, 1), CancellationToken.None);
        var transaction = db.PulloutTransactions.Single();
        transaction.IsRejected = true;
        await db.SaveChangesAsync(CancellationToken.None);

        using var retry = BuildUploadSheet("GCPL-C121-O-12950");
        await handler.Handle(new ImportPulloutFromExcelCommand(retry, 1), CancellationToken.None);

        Assert.Equal(2, db.PulloutTransactions.Count());
    }

    [Fact]
    public async Task NoIndentValueOnSheet_CreatesPulloutWithoutError()
    {
        var (db, handler) = CreateHandler();

        using var upload = BuildUploadSheet(indentText: null);
        await handler.Handle(new ImportPulloutFromExcelCommand(upload, 1), CancellationToken.None);

        Assert.Single(db.PulloutTransactions);
        Assert.Null(db.PulloutTransactions.Single().ClientIndentNumber);
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
        public DateTimeOffset UtcNow => new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => new(2026, 10, 31);
    }

    /// <summary>Forwards only <see cref="SubmitPulloutCommand"/> to a real handler - enough for
    /// <see cref="ImportPulloutFromExcelHandler"/>, which never sends anything else.</summary>
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
