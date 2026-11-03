using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>A Design Type flagged "ground floor only" keeps its materials on level-1 locations: the allocator never
/// suggests an upper level, a full ground floor ends in a clear shortfall message, and a manual move upstairs needs
/// the supervisor override permission.</summary>
public class GroundFloorOnlyTests
{
    private sealed record World(AppDbContext Db, Material Restricted, Material Free, Material Other, PalletPosition[] Ground, PalletPosition[] Upper);

    // Rack G: columns 1..2 x levels 1..3. Ground = G-01-01, G-02-01; upper = levels 2-3.
    private static async Task<World> SeedAsync(bool flagDesign = true)
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "G", Columns = 2, Levels = 3, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var column = 1; column <= 2; column++)
        {
            for (var level = 1; level <= 3; level++)
            {
                rack.PalletPositions.Add(new PalletPosition { Column = column, Level = level, LocationCode = $"G-{column:00}-{level:00}", MaxPallets = 1 });
            }
        }

        db.Racks.Add(rack);
        db.DesignTypes.Add(new DesignType { Code = "GRND", IsActive = true, RequiresGroundFloor = flagDesign });
        db.DesignTypes.Add(new DesignType { Code = "FREE", IsActive = true });
        var restricted = NewMaterial(1, "GRND");
        var free = NewMaterial(2, "FREE");
        var other = NewMaterial(3, "FREE");
        db.Materials.AddRange(restricted, free, other);
        await db.SaveChangesAsync();
        var positions = rack.PalletPositions.OrderBy(p => p.Column).ThenBy(p => p.Level).ToArray();
        return new World(db, restricted, free, other,
            positions.Where(p => p.Level == 1).ToArray(), positions.Where(p => p.Level > 1).ToArray());
    }

    private static Material NewMaterial(long number, string design) => new()
    {
        MaterialNumber = number, Description = $"M{number}", DesignType = design, PackSize = 24, GrossWeightKg = 0.5m,
        LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
    };

    private static PalletAllocationService Allocator(AppDbContext db) => new(db, new GroundClock());

    [Fact]
    public async Task RestrictedDesign_IsOnlyPlacedOnGroundLocations()
    {
        var w = await SeedAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 80, 202610, 1);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.All(result.Lines, l => Assert.EndsWith("-01", l.LocationCode));
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public async Task RestrictedDesign_NeverSpillsUpstairs_WhenGroundIsFull_AndExplainsWhy()
    {
        var w = await SeedAsync();
        // both ground locations hold another material
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[0].Id, MfgMonth = 202610, QuantityBoxes = 5 });
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[1].Id, MfgMonth = 202610, QuantityBoxes = 5 });
        await w.Db.SaveChangesAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 40, 202610, 1);

        Assert.Equal(AllocationStatus.Failed, result.Status);
        Assert.Empty(result.Lines);
        Assert.Contains("Ground-floor locations are not empty", result.Remarks);
        Assert.Contains("GRND", result.Remarks);
    }

    [Fact]
    public async Task RestrictedDesign_PlacesWhatFitsOnGround_AndFlagsTheRest()
    {
        var w = await SeedAsync();
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[1].Id, MfgMonth = 202610, QuantityBoxes = 5 });
        await w.Db.SaveChangesAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 100, 202610, 1);

        Assert.Equal(AllocationStatus.Partial, result.Status);
        Assert.Equal(40, result.AllocatedQuantityBoxes);
        Assert.Equal(w.Ground[0].LocationCode, Assert.Single(result.Lines).LocationCode);
        Assert.Contains("60", result.Remarks);
    }

    [Fact]
    public async Task RestrictedDesign_MayTopUpAGroundLocationHoldingTheSameMaterial()
    {
        var w = await SeedAsync();
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Restricted.Id, PalletPositionId = w.Ground[0].Id, MfgMonth = 202610, QuantityBoxes = 10 });
        await w.Db.SaveChangesAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 30, 202610, 1);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(w.Ground[0].LocationCode, Assert.Single(result.Lines).LocationCode);
    }

    [Fact]
    public async Task RestrictedDesign_ClubsWithSameMaterialPkmOnGround_ThenUsesTheFreeGroundLocations()
    {
        var w = await SeedAsync();
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Restricted.Id, PalletPositionId = w.Ground[0].Id, MfgMonth = 202609, QuantityBoxes = 10 });
        await w.Db.SaveChangesAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 70, 202609, 1);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(30, result.Lines.Single(l => l.LocationCode == w.Ground[0].LocationCode).QuantityBoxes); // topped up to 40
        Assert.Equal(40, result.Lines.Single(l => l.LocationCode == w.Ground[1].LocationCode).QuantityBoxes);
        Assert.All(result.Lines, l => Assert.EndsWith("-01", l.LocationCode));
    }

    [Fact]
    public async Task DepletedBatchRow_DoesNotMakeAGroundLocationNonEmpty()
    {
        var w = await SeedAsync();
        // stock was moved out earlier, leaving 0-box rows behind on both ground locations
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[0].Id, MfgMonth = 202609, QuantityBoxes = 0 });
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[1].Id, MfgMonth = 202609, QuantityBoxes = 0 });
        await w.Db.SaveChangesAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 80, 202610, 1);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(2, result.Lines.Count);
    }

    [Fact]
    public async Task ShortfallMessage_SaysWhyTheGroundLocationsCouldNotBeUsed()
    {
        var w = await SeedAsync();
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[0].Id, MfgMonth = 202610, QuantityBoxes = 5 });
        w.Db.InwardTransactions.Add(new InwardTransaction
        {
            ReferenceNumber = "GRN-OTHER",
            Lines =
            {
                new InwardTransactionLine
                {
                    MaterialId = w.Other.Id, MfgMonth = 202610, RequestedQuantityBoxes = 5, AllocatedQuantityBoxes = 5,
                    Putaways = { new InwardPutaway { PalletPositionId = w.Ground[1].Id, QuantityBoxes = 5 } }
                }
            }
        });
        await w.Db.SaveChangesAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 40, 202610, 1);

        Assert.Equal(AllocationStatus.Failed, result.Status);
        Assert.Contains("Of 2 ground-floor location(s): 2 hold another material or reservation", result.Remarks);
    }

    [Fact]
    public async Task NoPkmUpload_OfARestrictedDesign_UsesEmptyGroundLocations_AndIgnoresPartlyFilledOnes()
    {
        var w = await SeedAsync();
        // ground[0] holds 10 boxes of the same material (a pullout left it partly full); ground[1] is empty.
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Restricted.Id, PalletPositionId = w.Ground[0].Id, MfgMonth = 202609, QuantityBoxes = 10 });
        await w.Db.SaveChangesAsync();

        // A no-PKM inward has nothing to match a top-up against, so only genuinely empty locations qualify.
        var result = await Allocator(w.Db).AllocateAsync(w.Restricted.Id, 10, mfgMonth: null, 1);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(w.Ground[1].LocationCode, Assert.Single(result.Lines).LocationCode);
    }

    [Fact]
    public async Task UnflaggedDesign_IsNotRestricted_AndCanUseUpperLevels()
    {
        var w = await SeedAsync();
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[0].Id, MfgMonth = 202610, QuantityBoxes = 5 });
        w.Db.StockBatches.Add(new StockBatch { MaterialId = w.Other.Id, PalletPositionId = w.Ground[1].Id, MfgMonth = 202610, QuantityBoxes = 5 });
        await w.Db.SaveChangesAsync();

        var result = await Allocator(w.Db).AllocateAsync(w.Free.Id, 40, 202610, 1);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.DoesNotContain(result.Lines, l => l.LocationCode.EndsWith("-01"));
    }

    [Fact]
    public async Task ManualMoveUpstairs_IsBlocked_ButAllowedWithTheOverridePermission()
    {
        var w = await SeedAsync();
        var transaction = new InwardTransaction { ReferenceNumber = "GRN-GROUND" };
        var line = new InwardTransactionLine { MaterialId = w.Restricted.Id, MfgMonth = 202610, RequestedQuantityBoxes = 10, AllocatedQuantityBoxes = 10 };
        var putaway = new InwardPutaway { PalletPositionId = w.Ground[0].Id, QuantityBoxes = 10, AllocationReason = "seed" };
        line.Putaways.Add(putaway);
        transaction.Lines.Add(line);
        w.Db.InwardTransactions.Add(transaction);
        await w.Db.SaveChangesAsync();
        var upper = w.Upper.First();

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ChangeInwardPutawayLocationHandler(w.Db, new GroundPermissions(false))
                .Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, upper.Id, "test"), default));
        Assert.Contains("not a ground-floor location", blocked.Message);

        await new ChangeInwardPutawayLocationHandler(w.Db, new GroundPermissions(true))
            .Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, upper.Id, "supervisor override"), default);
        Assert.Equal(upper.Id, w.Db.InwardPutaways.Single().PalletPositionId);

        // moving between ground locations never needs the permission
        var back = await new ChangeInwardPutawayLocationHandler(w.Db, new GroundPermissions(false))
            .Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, w.Ground[1].Id, "back to ground"), default);
        Assert.NotNull(back);
    }
}

file sealed class GroundPermissions(bool grantOverride) : IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(grantOverride && permissionKey == PermissionKeys.Inward.OverrideGroundFloor);
}

file sealed class GroundClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
