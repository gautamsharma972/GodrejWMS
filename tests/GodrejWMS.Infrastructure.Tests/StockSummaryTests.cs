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

    [Fact]
    public async Task Capacity_CountsEmptyLocationsAtDefaultPalletSize_AndUsedLocationsAtTheirMaterial()
    {
        await using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 3, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var used = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
        var empty1 = new PalletPosition { Column = 1, Level = 2, LocationCode = "A-01-02", MaxPallets = 1 };
        var empty2 = new PalletPosition { Column = 1, Level = 3, LocationCode = "A-01-03", MaxPallets = 2 };
        rack.PalletPositions.Add(used);
        rack.PalletPositions.Add(empty1);
        rack.PalletPositions.Add(empty2);
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 1, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 60, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = used.Id, MfgMonth = 202610, QuantityBoxes = 50 });
        await db.SaveChangesAsync();

        var result = await new GetStockSummaryHandler(db).Handle(new GetStockSummaryQuery(), CancellationToken.None);

        // used: 1 pallet x 60 boxes; empty: (1 + 2 pallets) x default 40
        Assert.Equal(2, result.EmptyLocationCount);
        Assert.Equal(120, result.EmptyCapacityBoxes);
        Assert.Equal(180, result.TotalCapacityBoxes);
        Assert.Equal(130m, result.FreeCapacityBoxes); // 180 - 50 stock - 0 pending
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
