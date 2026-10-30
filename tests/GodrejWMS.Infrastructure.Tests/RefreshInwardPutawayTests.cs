using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class RefreshInwardPutawayTests
{
    [Fact]
    public async Task Refresh_ReplacesPendingReservation_UsingLatestBrandPreference()
    {
        var (db, transaction, line, oldPosition, preferredPosition) = SeedPendingGrn();

        var handler = new RefreshInwardPutawayHandler(db, new PalletAllocationService(db, new RefreshTestClock()));
        await handler.Handle(new RefreshInwardPutawayCommand(transaction.Id), CancellationToken.None);

        var putaway = Assert.Single(db.InwardPutaways.Where(x => x.InwardTransactionLineId == line.Id));
        Assert.Equal(preferredPosition.Id, putaway.PalletPositionId);
        Assert.Equal(5, putaway.QuantityBoxes);
        Assert.False(putaway.IsConfirmed);
        Assert.StartsWith("Brand-preferred range", putaway.AllocationReason);
        Assert.DoesNotContain(db.InwardPutaways, x => x.PalletPositionId == oldPosition.Id);

        var refreshedLine = db.InwardTransactionLines.Single(x => x.Id == line.Id);
        Assert.Equal(5, refreshedLine.AllocatedQuantityBoxes);
        Assert.Equal(AllocationStatus.Fulfilled, refreshedLine.Status);
    }

    [Fact]
    public async Task Refresh_PreservesConfirmedQuantity_AndMovesOnlyPendingQuantity()
    {
        var (db, transaction, line, oldPosition, preferredPosition) = SeedPendingGrn(requested: 10, pending: 6);
        line.Putaways.Add(new InwardPutaway
        {
            PalletPositionId = oldPosition.Id,
            QuantityBoxes = 4,
            AllocationReason = "Confirmed seed",
            IsConfirmed = true
        });
        line.AllocatedQuantityBoxes = 10;
        await db.SaveChangesAsync();

        var handler = new RefreshInwardPutawayHandler(db, new PalletAllocationService(db, new RefreshTestClock()));
        await handler.Handle(new RefreshInwardPutawayCommand(transaction.Id), CancellationToken.None);

        var putaways = db.InwardPutaways.Where(x => x.InwardTransactionLineId == line.Id).ToList();
        Assert.Contains(putaways, x => x.PalletPositionId == oldPosition.Id && x.IsConfirmed && x.QuantityBoxes == 4);
        Assert.Contains(putaways, x => x.PalletPositionId == preferredPosition.Id && !x.IsConfirmed && x.QuantityBoxes == 6);
        Assert.Equal(10, putaways.Sum(x => x.QuantityBoxes));

        var refreshedLine = db.InwardTransactionLines.Single(x => x.Id == line.Id);
        Assert.Equal(10, refreshedLine.AllocatedQuantityBoxes);
        Assert.Equal(AllocationStatus.Fulfilled, refreshedLine.Status);
    }

    private static (AppDbContext Db, InwardTransaction Transaction, InwardTransactionLine Line,
        PalletPosition OldPosition, PalletPosition PreferredPosition) SeedPendingGrn(
        decimal requested = 5,
        decimal pending = 5)
    {
        var db = TestDbContextFactory.Create();
        var rackA = BuildRack("A", "A-01-01");
        var rackB = BuildRack("B", "B-01-01");
        db.Racks.AddRange(rackA, rackB);

        var brand = new Brand { Code = "HIT", Description = "Hit", IsActive = true };
        var material = new Material
        {
            MaterialNumber = 40019311,
            Description = "Refresh allocation material",
            DesignType = "GGAF1",
            PackSize = 20,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 20,
            SeasonId = SeasonIds.Rainy,
            Brand = brand
        };
        db.Materials.Add(material);
        db.SaveChanges();

        var oldPosition = rackA.PalletPositions.Single();
        var preferredPosition = rackB.PalletPositions.Single();
        db.BrandLocationPreferences.Add(new BrandLocationPreference
        {
            BrandId = brand.Id,
            FromPalletPositionId = preferredPosition.Id,
            ToPalletPositionId = preferredPosition.Id,
            IsActive = true
        });

        var transaction = new InwardTransaction { ReferenceNumber = "GRN-REFRESH-1" };
        var line = new InwardTransactionLine
        {
            Material = material,
            MfgMonth = 202610,
            RequestedQuantityBoxes = requested,
            AllocatedQuantityBoxes = pending,
            Status = pending >= requested ? AllocationStatus.Fulfilled : AllocationStatus.Partial
        };
        line.Putaways.Add(new InwardPutaway
        {
            PalletPositionId = oldPosition.Id,
            QuantityBoxes = pending,
            AllocationReason = "Original reservation"
        });
        transaction.Lines.Add(line);
        db.InwardTransactions.Add(transaction);
        db.SaveChanges();

        return (db, transaction, line, oldPosition, preferredPosition);
    }

    private static Rack BuildRack(string code, string locationCode)
    {
        var rack = new Rack
        {
            Code = code,
            Columns = 1,
            Levels = 1,
            ShelfLengthMm = 2000,
            ShelfWidthMm = 2500,
            ShelfHeightMm = 3600
        };
        rack.PalletPositions.Add(new PalletPosition
        {
            Column = 1,
            Level = 1,
            LocationCode = locationCode,
            MaxPallets = 1
        });
        return rack;
    }
}

file sealed class RefreshTestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
