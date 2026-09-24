using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class RejectInwardTests
{
    private static (AppDbContext Db, PalletPosition Position, InwardTransaction Transaction, InwardPutaway Putaway) SeedScenario()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 1,
            Description = "Material A",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();

        var transaction = new InwardTransaction { ReferenceNumber = "GRN-REJECT-1" };
        var line = new InwardTransactionLine
        {
            InwardTransaction = transaction,
            MaterialId = material.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = 20,
            AllocatedQuantityBoxes = 20,
            Status = AllocationStatus.Fulfilled
        };
        var putaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = position.Id,
            QuantityBoxes = 20,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(putaway);
        transaction.Lines.Add(line);
        db.InwardTransactions.Add(transaction);
        db.SaveChanges();

        return (db, position, transaction, putaway);
    }

    [Fact]
    public async Task Reject_MarksTransactionRejected_AndReleasesTheReservedLocation()
    {
        var (db, position, transaction, putaway) = SeedScenario();
        var handler = new RejectInwardHandler(db, new TestClock(), new TestCurrentUser());

        var result = await handler.Handle(new RejectInwardCommand(transaction.Id, "Vendor sent wrong material"), CancellationToken.None);

        Assert.True(result.IsRejected);

        var reloaded = db.InwardTransactions.Single(t => t.Id == transaction.Id);
        Assert.True(reloaded.IsRejected);
        Assert.Equal("Vendor sent wrong material", reloaded.RejectionReason);
        Assert.Equal("test@godrejwms.local", reloaded.RejectedByUserName);
        Assert.NotNull(reloaded.RejectedAt);

        // The reservation is gone, so the location's capacity is fully free again.
        Assert.False(db.InwardPutaways.Any(p => p.Id == putaway.Id));
        Assert.Empty(db.StockBatches.Where(b => b.PalletPositionId == position.Id));
    }

    [Fact]
    public async Task Reject_Throws_WhenAnyPutawayAlreadyConfirmed()
    {
        var (db, _, transaction, putaway) = SeedScenario();
        putaway.IsConfirmed = true;
        await db.SaveChangesAsync();

        var handler = new RejectInwardHandler(db, new TestClock(), new TestCurrentUser());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RejectInwardCommand(transaction.Id, "Too late"), CancellationToken.None));
        Assert.Contains("already been confirmed", ex.Message);

        // Nothing should have been touched.
        Assert.True(db.InwardPutaways.Single(p => p.Id == putaway.Id).IsConfirmed);
        Assert.False(db.InwardTransactions.Single(t => t.Id == transaction.Id).IsRejected);
    }

    [Fact]
    public async Task Reject_Throws_WhenAlreadyRejected()
    {
        var (db, _, transaction, _) = SeedScenario();
        var handler = new RejectInwardHandler(db, new TestClock(), new TestCurrentUser());
        await handler.Handle(new RejectInwardCommand(transaction.Id, "First rejection"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new RejectInwardCommand(transaction.Id, "Second attempt"), CancellationToken.None));
        Assert.Contains("already been rejected", ex.Message);
    }

    [Fact]
    public async Task Reject_SucceedsOnATransactionWithNoPutaways_AllocationTotallyFailed()
    {
        var db = TestDbContextFactory.Create();
        var material = new Material
        {
            MaterialNumber = 1, Description = "M", DesignType = "XOLDH", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var transaction = new InwardTransaction { ReferenceNumber = "GRN-REJECT-FAILED" };
        var line = new InwardTransactionLine
        {
            InwardTransaction = transaction,
            MaterialId = material.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = 20,
            AllocatedQuantityBoxes = 0,
            Status = AllocationStatus.Failed,
            Remarks = "No capacity"
        };
        transaction.Lines.Add(line);
        db.InwardTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var handler = new RejectInwardHandler(db, new TestClock(), new TestCurrentUser());
        var result = await handler.Handle(new RejectInwardCommand(transaction.Id, "No point keeping a failed GRN pending"), CancellationToken.None);

        Assert.True(result.IsRejected);
    }
}

file sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

file sealed class TestCurrentUser : ICurrentUserService
{
    public string? UserId => "test-user";

    public string? UserName => "test@godrejwms.local";

    public bool IsInRole(string role) => true;
}
