using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class RefreshInwardFragmentationTests
{
    /// <summary>A GRN with several lines of the same SKU (different PKMs, so they stay separate lines) must pack pallets the same way after "Refresh put-away"
    /// as it did when first submitted: full pallets, with at most one partial one. Refreshing line 1 used to see the
    /// not-yet-refreshed reservations of lines 2..n as occupied space, so it topped up around them and left the same
    /// pallet split across two lines (e.g. 42 + 12 on a 54-box pallet) instead of 54 in one go.</summary>
    [Fact]
    public async Task Refresh_OfMultiLineGrn_PacksPalletsLikeTheOriginalSubmit()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "P", Columns = 1, Levels = 12, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var level = 1; level <= 12; level++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = level, LocationCode = $"P-01-{level:00}", MaxPallets = 1 });
        }
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40065278, Description = "AER PLUG", DesignType = "FAE2RLB", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 54, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var allocator = new PalletAllocationService(db, new FragClock());
        var submitted = await new SubmitInwardHandler(db, allocator, new FragClock(), new FragUser()).Handle(
            new SubmitInwardCommand(
                [new SubmitInwardLine(material.MaterialNumber, 200, "OCT|2026"), new SubmitInwardLine(material.MaterialNumber, 200, "SEP|2026")],
                WarehouseId: 1),
            default);
        AssertPackedLikeOneStream(db);
        var before = Rows(db);

        await new RefreshInwardPutawayHandler(db, allocator).Handle(new RefreshInwardPutawayCommand(submitted.Id), default);

        AssertPackedLikeOneStream(db);
        Assert.Equal(before, Rows(db)); // same layout as the original submit, not a re-shuffled one
    }

    [Fact]
    public async Task Submit_AddsRepeatedMaterialAndPkmRowsIntoOneLine_SoAPalletIsNotSplitAcrossRows()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "P", Columns = 1, Levels = 12, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var level = 1; level <= 12; level++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = level, LocationCode = $"P-01-{level:00}", MaxPallets = 1 });
        }
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40065278, Description = "AER PLUG", DesignType = "FAE2RLB", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 54, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var result = await new SubmitInwardHandler(db, new PalletAllocationService(db, new FragClock()), new FragClock(), new FragUser()).Handle(
            new SubmitInwardCommand(
                [new SubmitInwardLine(material.MaterialNumber, 200, "OCT|2026"), new SubmitInwardLine(material.MaterialNumber, 200, "oct|2026")],
                WarehouseId: 1),
            default);

        var line = Assert.Single(result.Lines);
        Assert.Equal(400, line.RequestedQuantityBoxes);
        Assert.Equal(400, line.AllocatedQuantityBoxes);
        // 400 = 7 full 54-box pallets + 22: one row per location, never the same location twice
        Assert.Equal(8, db.InwardPutaways.Count());
        Assert.Equal(8, db.InwardPutaways.Select(p => p.PalletPositionId).Distinct().Count());
    }

    [Fact]
    public async Task Refresh_FoldsDuplicateMaterialPkmLinesOfAnOldGrn_IntoOneLine_KeepingConfirmedStock()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "E", Columns = 1, Levels = 6, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var level = 1; level <= 6; level++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = level, LocationCode = $"E-01-{level:00}", MaxPallets = 1 });
        }
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40063427, Description = "EXPERT CREME", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 240, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        var first = rack.PalletPositions.First();

        // An old GRN: the same material + PKM on two lines (7 and 193), each reserving part of one location, and 4 of the
        // first line's boxes already confirmed.
        var transaction = new InwardTransaction { ReferenceNumber = "GRN-OLD" };
        var line1 = new InwardTransactionLine { MaterialId = material.Id, MfgMonth = 202610, RequestedQuantityBoxes = 7, AllocatedQuantityBoxes = 7 };
        line1.Putaways.Add(new InwardPutaway { PalletPositionId = first.Id, QuantityBoxes = 4, IsConfirmed = true });
        line1.Putaways.Add(new InwardPutaway { PalletPositionId = first.Id, QuantityBoxes = 3 });
        var line2 = new InwardTransactionLine { MaterialId = material.Id, MfgMonth = 202610, RequestedQuantityBoxes = 193, AllocatedQuantityBoxes = 3 };
        line2.Putaways.Add(new InwardPutaway { PalletPositionId = first.Id, QuantityBoxes = 3 });
        transaction.Lines.Add(line1);
        transaction.Lines.Add(line2);
        db.InwardTransactions.Add(transaction);
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = first.Id, MfgMonth = 202610, QuantityBoxes = 4 });
        await db.SaveChangesAsync();

        await new RefreshInwardPutawayHandler(db, new PalletAllocationService(db, new FragClock())).Handle(new RefreshInwardPutawayCommand(transaction.Id), default);

        var line = Assert.Single(db.InwardTransactionLines);
        Assert.Equal(200, line.RequestedQuantityBoxes);
        Assert.Equal(200, line.AllocatedQuantityBoxes);
        var confirmed = Assert.Single(db.InwardPutaways.Where(p => p.IsConfirmed));
        Assert.Equal(4, confirmed.QuantityBoxes);
        Assert.Equal(200, db.InwardPutaways.Sum(p => p.QuantityBoxes));
        // every pending location appears once
        var pending = db.InwardPutaways.Where(p => !p.IsConfirmed).AsEnumerable().ToList();
        Assert.Equal(pending.Count, pending.Select(p => p.PalletPositionId).Distinct().Count());
        Assert.Equal(4, db.StockBatches.Single().QuantityBoxes); // confirmed stock untouched
    }

    private static List<string> Rows(AppDbContext db) =>
        db.InwardPutaways.AsEnumerable()
            .OrderBy(p => p.InwardTransactionLineId).ThenBy(p => p.Id)
            .Select(p => $"{db.PalletPositions.Find(p.PalletPositionId)!.LocationCode}={p.QuantityBoxes}")
            .ToList();

    private static void AssertPackedLikeOneStream(AppDbContext db)
    {
        var perLocation = db.InwardPutaways.AsEnumerable()
            .GroupBy(p => p.PalletPositionId)
            .Select(g => g.Sum(p => p.QuantityBoxes))
            .ToList();
        Assert.Equal(400, perLocation.Sum());
        Assert.All(perLocation, total => Assert.True(total <= 54));
        Assert.Equal(1, perLocation.Count(total => total < 54)); // 400 = 7 x 54 + 22
        Assert.Equal(8, perLocation.Count);
    }
}

file sealed class FragUser : ICurrentUserService
{
    public string? UserId => "test-user";
    public string? UserName => "test@godrejwms.local";
    public bool IsInRole(string role) => true;
    public IReadOnlyList<string> Roles => ["Superadmin"];
}

file sealed class FragClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
