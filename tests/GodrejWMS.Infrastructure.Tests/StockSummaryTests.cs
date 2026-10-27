using GodrejWMS.Application.Features.StockMaster.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class StockSummaryTests
{
    [Fact]
    public async Task DashboardSummary_SeparatesTodayInwardOutwardAndPendingConfirmations()
    {
        await using var db = TestDbContextFactory.Create();
        var today = new DateTimeOffset(DateTime.Today);

        db.InwardTransactions.AddRange(
            Inward("GRN-CONFIRMED", today.AddHours(1), 10, confirmed: true),
            Inward("GRN-PENDING", today.AddHours(2), 4, confirmed: false));
        db.PulloutTransactions.AddRange(
            Pullout("PICK-CONFIRMED", today.AddHours(3), 7, confirmed: true),
            Pullout("PICK-PENDING", today.AddHours(4), 3, confirmed: false),
            Pullout("PICK-REJECTED", today.AddHours(5), 9, confirmed: false, rejected: true));
        await db.SaveChangesAsync();

        var result = await new GetStockSummaryHandler(db)
            .Handle(new GetStockSummaryQuery(), CancellationToken.None);

        Assert.Equal(2, result.TodayInwardTransactions);
        Assert.Equal(14m, result.TodayInwardBoxes);
        Assert.Equal(10m, result.TodayConfirmedBoxes);
        Assert.Equal(2, result.TodayPulloutTransactions);
        Assert.Equal(10m, result.TodayPickedBoxes);
        Assert.Equal(7m, result.TodayPulloutBoxes);
        Assert.Equal(1, result.PendingPutawayTransactions);
        Assert.Equal(1, result.PendingPulloutTransactions);
    }

    private static InwardTransaction Inward(string reference, DateTimeOffset createdAt, decimal boxes, bool confirmed)
    {
        var transaction = new InwardTransaction { ReferenceNumber = reference, CreatedAt = createdAt };
        var line = new InwardTransactionLine
        {
            MaterialId = 1,
            MfgMonth = 202610,
            RequestedQuantityBoxes = boxes,
            AllocatedQuantityBoxes = boxes,
            Status = AllocationStatus.Fulfilled,
            InwardTransaction = transaction
        };
        line.Putaways.Add(new InwardPutaway
        {
            PalletPositionId = confirmed ? 1 : 2,
            QuantityBoxes = boxes,
            IsConfirmed = confirmed,
            ConfirmedAt = confirmed ? createdAt : null,
            InwardTransactionLine = line
        });
        transaction.Lines.Add(line);
        return transaction;
    }

    private static PulloutTransaction Pullout(
        string reference,
        DateTimeOffset createdAt,
        decimal boxes,
        bool confirmed,
        bool rejected = false)
    {
        var transaction = new PulloutTransaction
        {
            ReferenceNumber = reference,
            CreatedAt = createdAt,
            IsConfirmed = confirmed,
            IsRejected = rejected,
            ConfirmedAt = confirmed ? createdAt : null
        };
        transaction.Lines.Add(new PulloutTransactionLine
        {
            MaterialId = 1,
            RequestedQuantityBoxes = boxes,
            PickedQuantityBoxes = boxes,
            Status = AllocationStatus.Fulfilled,
            PulloutTransaction = transaction
        });
        return transaction;
    }
}
