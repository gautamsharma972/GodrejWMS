using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class ReleaseInactiveMaterialPutawayTests
{
    private static (Infrastructure.Persistence.AppDbContext Db, InwardTransaction Tx, Material Material, PalletPosition Position) Seed(decimal stock)
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 1, Description = "M", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();

        var tx = new InwardTransaction { ReferenceNumber = "GRN-INACT-1", PostedAt = DateTimeOffset.UtcNow };
        var line = new InwardTransactionLine
        {
            InwardTransaction = tx, MaterialId = material.Id, MfgMonth = 201001,
            RequestedQuantityBoxes = 20, AllocatedQuantityBoxes = 20, Status = AllocationStatus.Fulfilled
        };
        line.Putaways.Add(new InwardPutaway { InwardTransactionLine = line, PalletPositionId = position.Id, QuantityBoxes = 20, IsConfirmed = true, AllocationReason = "seed" });
        tx.Lines.Add(line);
        db.InwardTransactions.Add(tx);
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 201001, QuantityBoxes = stock });
        db.SaveChanges();
        return (db, tx, material, position);
    }

    private static ReleaseInactiveMaterialPutawayHandler Handler(Infrastructure.Persistence.AppDbContext db) =>
        new(db, TestServices.Posting(db), TestServices.Uow(db));

    [Fact]
    public async Task Release_TakesStockOutAndLeavesTheLineUnallocated_ForAnInactiveMaterial()
    {
        var (db, tx, material, _) = Seed(20);
        material.IsActive = false;
        await db.SaveChangesAsync();

        await Handler(db).Handle(new ReleaseInactiveMaterialPutawayCommand(tx.Id), CancellationToken.None);

        Assert.Equal(0, db.StockBatches.Single().QuantityBoxes);
        Assert.Empty(db.InwardPutaways);
        var line = db.InwardTransactionLines.Single();
        Assert.Equal(0, line.AllocatedQuantityBoxes);
        Assert.Equal(AllocationStatus.Failed, line.Status);
    }

    [Fact]
    public async Task Release_Throws_WhenNoMaterialIsInactive_AndLeavesStockAlone()
    {
        var (db, tx, _, _) = Seed(20);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new ReleaseInactiveMaterialPutawayCommand(tx.Id), CancellationToken.None));
        Assert.Equal(20, db.StockBatches.Single().QuantityBoxes);
    }

    [Fact]
    public async Task Release_TakesBackOnlyWhatIsStillInPlace_WhenSomeBoxesWereAlreadyPicked()
    {
        var (db, tx, material, _) = Seed(5); // 20 were put away, 15 since picked
        material.IsActive = false;
        await db.SaveChangesAsync();

        await Handler(db).Handle(new ReleaseInactiveMaterialPutawayCommand(tx.Id), CancellationToken.None);

        Assert.Equal(0, db.StockBatches.Single().QuantityBoxes);
        Assert.Equal(-5, Assert.Single(db.StockLedgerEntries.Where(e => e.EntryType == StockLedgerEntryTypes.Reversal)).QuantityBoxes);
        Assert.Empty(db.InwardPutaways);
        Assert.Equal(0, db.InwardTransactionLines.Single().AllocatedQuantityBoxes);
    }

    [Fact]
    public async Task Release_LeavesPutawaysThePickerAlreadySubmitted()
    {
        var (db, tx, material, _) = Seed(20);
        material.IsActive = false;
        db.InwardPutaways.Single().IsPickerPlaced = true;
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new ReleaseInactiveMaterialPutawayCommand(tx.Id), CancellationToken.None));
        Assert.Equal(20, db.StockBatches.Single().QuantityBoxes);
        Assert.Single(db.InwardPutaways);
    }
}
