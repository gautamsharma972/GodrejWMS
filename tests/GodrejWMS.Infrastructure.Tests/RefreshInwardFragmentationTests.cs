using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
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

public class RefreshInwardDeactivatedMaterialTests
{
    [Fact]
    public async Task Allocation_TreatsALocationHoldingOnlyADeactivatedMaterialAsFree()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "P", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "P-01-01", MaxPallets = 1 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        Material Make(long number) => new()
        {
            MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 54, SeasonId = SeasonIds.Rainy
        };
        var old = Make(40000001);
        var incoming = Make(40000002);
        db.Materials.AddRange(old, incoming);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch { MaterialId = old.Id, PalletPositionId = position.Id, MfgMonth = 202610, QuantityBoxes = 54 });
        await db.SaveChangesAsync();

        var allocator = new PalletAllocationService(db, new FragClock());
        var whileActive = await allocator.AllocateAsync(incoming.Id, 10, 202610, 1);
        Assert.Empty(whileActive.Lines); // the other material's stock still fills the only location

        old.IsActive = false;
        await db.SaveChangesAsync();

        var afterDeactivation = await allocator.AllocateAsync(incoming.Id, 10, 202610, 1);
        Assert.Equal("P-01-01", Assert.Single(afterDeactivation.Lines).LocationCode);
    }

    [Fact]
    public async Task Refresh_AndReallocate_DoNotAllocateALocationToAMaterialDeactivatedAfterUpload()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "P", Columns = 1, Levels = 4, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var level = 1; level <= 4; level++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = level, LocationCode = $"P-01-{level:00}", MaxPallets = 1 });
        }
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40065278, Description = "AER PLUG", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 54, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var allocator = new PalletAllocationService(db, new FragClock());
        var submitted = await new SubmitInwardHandler(db, allocator, new FragClock(), new FragUser()).Handle(
            new SubmitInwardCommand([new SubmitInwardLine(material.MaterialNumber, 20, "OCT|2026")], WarehouseId: 1), default);
        Assert.Equal(20, Assert.Single(submitted.Lines).AllocatedQuantityBoxes);

        material.IsActive = false;
        await db.SaveChangesAsync();

        await new RefreshInwardPutawayHandler(db, allocator).Handle(new RefreshInwardPutawayCommand(submitted.Id), default);

        var line = Assert.Single(db.InwardTransactionLines);
        Assert.Equal(0, line.AllocatedQuantityBoxes);
        Assert.Equal(AllocationStatus.Failed, line.Status);
        Assert.Equal("Material code not found in Material Master.", line.Remarks);
        Assert.Empty(db.InwardPutaways);

        await new ReallocateInwardHandler(db, allocator).Handle(new ReallocateInwardCommand(submitted.Id), default);
        Assert.Empty(db.InwardPutaways);
    }
}

file sealed class AllowAll : GodrejWMS.Application.Common.Permissions.IPermissionService
{
    public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(true);
}

public class PickerPlaceInwardPutawayTests
{
    private static async Task<(AppDbContext Db, Material A, Material B, InwardTransaction Grn, InwardPutaway Putaway, List<PalletPosition> Positions)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "P", Columns = 3, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var c = 1; c <= 3; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"P-0{c}-01", MaxPallets = 1 });
        }
        db.Racks.Add(rack);
        Material Make(long number) => new()
        {
            MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 54, SeasonId = SeasonIds.Rainy
        };
        var a = Make(40000001);
        var b = Make(40000002);
        db.Materials.AddRange(a, b);
        await db.SaveChangesAsync();

        var positions = rack.PalletPositions.OrderBy(p => p.Column).ToList();
        var grn = new InwardTransaction { ReferenceNumber = "GRN-1" };
        var line = new InwardTransactionLine { MaterialId = a.Id, MfgMonth = 202610, RequestedQuantityBoxes = 30, AllocatedQuantityBoxes = 30 };
        var putaway = new InwardPutaway { PalletPositionId = positions[0].Id, QuantityBoxes = 30 };
        line.Putaways.Add(putaway);
        grn.Lines.Add(line);
        db.InwardTransactions.Add(grn);
        await db.SaveChangesAsync();
        return (db, a, b, grn, putaway, positions);
    }

    private static PickerPlaceInwardPutawayHandler Handler(AppDbContext db) => new(db, new AllowAll(), new FragUser(), new FragClock());

    [Fact]
    public async Task ChangesLocationQuantityAndPkm_MovingThePutawayToTheLineOfThatPkm()
    {
        var (db, _, _, grn, putaway, positions) = await SeedAsync();
        using var _ = db;

        var result = await Handler(db).Handle(
            new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "NOV|2026", positions[1].Id, null), default);

        Assert.True(result.Saved);
        var line = Assert.Single(db.InwardTransactionLines.Where(l => l.InwardTransactionId == grn.Id));
        Assert.Equal(202611, line.MfgMonth);
        Assert.Equal(30, line.AllocatedQuantityBoxes);
        Assert.Equal(30, line.RequestedQuantityBoxes); // the inward order's own quantity is never reduced by the picker
        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Fulfilled, line.Status);
        var saved = Assert.Single(db.InwardPutaways);
        Assert.Equal(positions[1].Id, saved.PalletPositionId);
        Assert.Equal(30, saved.QuantityBoxes);
        Assert.True(saved.IsPickerPlaced);
        Assert.False(saved.IsConfirmed);
    }

    [Fact]
    public async Task QuantityAboveTheSystemAllocation_IsAcceptedAtPutaway_ButBlocksTheSupervisorConfirm()
    {
        var (db, _, _, _, putaway, positions) = await SeedAsync();
        using var _ = db;

        Assert.True((await Handler(db).Handle(
            new PickerPlaceInwardPutawayCommand(putaway.Id, 31, "OCT|2026", positions[0].Id, null), default)).Saved);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ConfirmInwardPutawayHandler(db, new FragClock(), new FragUser(), new AllowAll())
                .Handle(new ConfirmInwardPutawayCommand("GRN-1"), default));
        Assert.Contains("more than the 30", ex.Message);
        Assert.False(db.InwardPutaways.Single().IsConfirmed);
    }

    [Fact]
    public async Task QuantityBelowTheSystemAllocation_IsAcceptedAtPutaway_ButBlocksTheSupervisorConfirm()
    {
        var (db, _, _, _, putaway, positions) = await SeedAsync();
        using var _ = db;

        Assert.True((await Handler(db).Handle(
            new PickerPlaceInwardPutawayCommand(putaway.Id, 28, "OCT|2026", positions[0].Id, null), default)).Saved);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ConfirmInwardPutawayHandler(db, new FragClock(), new FragUser(), new AllowAll())
                .Handle(new ConfirmInwardPutawayCommand("GRN-1"), default));
        Assert.Contains("less than the 30", ex.Message);
        Assert.Contains("28", ex.Message);
        Assert.False(db.InwardPutaways.Single().IsConfirmed);
    }

    [Fact]
    public async Task OrderOf60WithAllocationOf50_PickerMustPutAwayExactly50AcrossTheLocations()
    {
        var (db, a, _, grn, first, positions) = await SeedAsync();
        using var _ = db;
        // The order asked for 60, the system could only allocate 50: 30 + 20 on two locations.
        var line = grn.Lines.Single();
        line.RequestedQuantityBoxes = 60;
        line.AllocatedQuantityBoxes = 50;
        var second = new InwardPutaway { PalletPositionId = positions[1].Id, QuantityBoxes = 20 };
        line.Putaways.Add(second);
        await db.SaveChangesAsync();

        // The putaway screen never checks the total: the first location can be submitted on its own.
        Assert.True((await Handler(db).Handle(
            new PickerPlaceInwardPutawayCommand(first.Id, 25, "OCT|2026", positions[0].Id, null), default)).Saved);

        // A total that does not match is only reported when the supervisor confirms.
        Assert.True((await Handler(db).Handle(
            new PickerPlaceInwardPutawayCommand(second.Id, 20, "OCT|2026", positions[1].Id, null), default)).Saved);
        var confirm = new ConfirmInwardPutawayHandler(db, new FragClock(), new FragUser(), new AllowAll());
        var under = await Assert.ThrowsAsync<InvalidOperationException>(() => confirm.Handle(new ConfirmInwardPutawayCommand("GRN-1"), default));
        Assert.Contains("less than the 50", under.Message);

        Assert.True((await Handler(db).Handle(
            new PickerPlaceInwardPutawayCommand(second.Id, 25, "OCT|2026", positions[1].Id, null), default)).Saved);
        Assert.Equal(50, db.InwardPutaways.Sum(p => p.QuantityBoxes));
        Assert.Equal(60, db.InwardTransactionLines.Single().RequestedQuantityBoxes);
        await confirm.Handle(new ConfirmInwardPutawayCommand("GRN-1"), default);
        Assert.All(db.InwardPutaways, p => Assert.True(p.IsConfirmed));
    }

    [Fact]
    public async Task LocationHoldingStock_WarnsFirst_ThenSavesWhenAcknowledged_AndConfirmAllowsMixing()
    {
        var (db, _, b, _, putaway, positions) = await SeedAsync();
        using var _ = db;
        db.StockBatches.Add(new StockBatch { MaterialId = b.Id, PalletPositionId = positions[1].Id, MfgMonth = 202609, QuantityBoxes = 54 });
        await db.SaveChangesAsync();

        var command = new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", positions[1].Id, null);
        var warned = await Handler(db).Handle(command, default);
        Assert.False(warned.Saved);
        Assert.Contains("P-02-01", warned.Warning);
        Assert.Equal(positions[0].Id, db.InwardPutaways.Single().PalletPositionId); // nothing changed yet

        var saved = await Handler(db).Handle(command with { AcknowledgeWarning = true }, default);
        Assert.True(saved.Saved);

        // The location is full of another material, yet the picker's choice confirms into stock.
        var confirmed = await new ConfirmInwardPutawayHandler(db, new FragClock(), new FragUser(), new AllowAll())
            .Handle(new ConfirmInwardPutawayCommand("GRN-1"), default);
        Assert.NotNull(confirmed);
        Assert.Equal(2, db.StockBatches.Count(s => s.PalletPositionId == positions[1].Id));
    }

    [Fact]
    public async Task Git_ClosesThePutawayWithoutCreatingStock()
    {
        var (db, _, _, _, putaway, _) = await SeedAsync();
        using var _ = db;

        var result = await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, null, null, "git"), default);

        Assert.True(result.Saved);
        var saved = db.InwardPutaways.Single();
        Assert.Equal("GIT", saved.ExcludedLocation);
        Assert.True(saved.IsConfirmed);
        Assert.Empty(db.StockBatches);
    }

    [Fact]
    public async Task GitCanBeCorrectedToARealLocation_ItGoesBackToWaitingForConfirmation()
    {
        var (db, _, _, _, putaway, positions) = await SeedAsync();
        using var _ = db;
        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, null, null, "GIT"), default);

        var result = await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", positions[2].Id, null), default);

        Assert.True(result.Saved);
        var saved = db.InwardPutaways.Single();
        Assert.Null(saved.ExcludedLocation);
        Assert.False(saved.IsConfirmed);
        Assert.Null(saved.ConfirmedAt);
        Assert.Equal(positions[2].Id, saved.PalletPositionId);

        // and a real location can be set aside again
        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, null, null, "NO RACK"), default);
        Assert.Equal("NO RACK", db.InwardPutaways.Single().ExcludedLocation);
    }

    [Fact]
    public async Task GitCannotBeCorrectedOnceASupervisorConfirmsTheGrn()
    {
        var (db, _, _, _, putaway, positions) = await SeedAsync();
        using var _ = db;
        await Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, null, null, "GIT"), default);

        await new ConfirmInwardPutawayHandler(db, new FragClock(), new FragUser(), new AllowAll())
            .Handle(new ConfirmInwardPutawayCommand("GRN-1"), default);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Handler(db).Handle(new PickerPlaceInwardPutawayCommand(putaway.Id, 30, "OCT|2026", positions[2].Id, null), default));
        Assert.Contains("already been confirmed", ex.Message);
    }
}
