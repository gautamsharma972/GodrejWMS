using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using MediatR;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>End-to-end: paste text in -> a real pullout out, including the same Indent-based
/// duplicate guard the .xlsx upload path uses (<see cref="ImportPulloutDuplicateIndentTests"/>
/// covers that guard's own rules in depth; this confirms the paste entry point wires into it
/// correctly, not re-testing every rule).</summary>
public class ImportPulloutFromPasteHandlerTests
{
    private static (AppDbContext Db, ImportPulloutFromPasteHandler Handler) CreateHandler()
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
        var handler = new ImportPulloutFromPasteHandler(new ClosedXmlExcelService(), mediator, db);
        return (db, handler);
    }

    private static string BuildPaste(string? indent) =>
        string.Join('\n',
        [
            "Transport\tKAPOOR\t\tGATE NO:\t\tPlan Time:\t12:10\t",
            $"\t\t\t\t\tIndent\t{indent ?? ""}\t",
            "Code\tDescription\tDel Qty\tPriority",
            "40063045\tTest Material\t10\t"
        ]);

    [Fact]
    public async Task PastedSheet_CreatesPullout_WithHeaderFieldsAndClientIndentNumberStored()
    {
        var (db, handler) = CreateHandler();

        var result = await handler.Handle(new ImportPulloutFromPasteCommand(BuildPaste("GCPL-C121-O-12950"), 1), CancellationToken.None);

        Assert.NotEmpty(result.ReferenceNumber);
        var transaction = db.PulloutTransactions.Single();
        Assert.Equal("KAPOOR", transaction.TransportName);
        Assert.Equal("GCPL-C121-O-12950", transaction.ClientIndentNumber);
    }

    [Fact]
    public async Task PastedSheet_WithIndentAlreadyUsed_IsRejectedAsDuplicate()
    {
        var (db, handler) = CreateHandler();
        await handler.Handle(new ImportPulloutFromPasteCommand(BuildPaste("GCPL-C121-O-12950"), 1), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new ImportPulloutFromPasteCommand(BuildPaste("GCPL-C121-O-12950"), 1), CancellationToken.None));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(db.PulloutTransactions);
    }

    [Fact]
    public async Task EmptyPaste_Throws_WithGuidanceMessage_AndCreatesNothing()
    {
        var (db, handler) = CreateHandler();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new ImportPulloutFromPasteCommand("just some header text, no data rows", 1), CancellationToken.None));

        Assert.Contains("No data rows", exception.Message);
        Assert.Empty(db.PulloutTransactions);
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
