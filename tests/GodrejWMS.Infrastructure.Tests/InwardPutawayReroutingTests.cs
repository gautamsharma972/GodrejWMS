using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using GodrejWMS.Application.Features.Inward.Dtos;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>
/// Covers the cross-SKU-mixing guard that both the manual reroute command and its candidate-list
/// query must enforce - a pallet position may only ever hold one material at a time, the same rule
/// <see cref="GodrejWMS.Infrastructure.Services.PalletAllocationService"/> already applies when it
/// picks a location automatically.
/// </summary>
public class InwardPutawayReroutingTests
{
    private static (AppDbContext Db, Material MaterialA, Material MaterialB, PalletPosition Current, PalletPosition Target, InwardPutaway Putaway) SeedScenario()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var current = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01" };
        var target = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-02-01" };
        rack.PalletPositions.Add(current);
        rack.PalletPositions.Add(target);
        db.Racks.Add(rack);

        var materialA = new Material
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
        var materialB = new Material
        {
            MaterialNumber = 2,
            Description = "Material B",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.AddRange(materialA, materialB);
        db.SaveChanges();

        var transaction = new InwardTransaction { ReferenceNumber = "GRN-TEST-1" };
        var line = new InwardTransactionLine
        {
            InwardTransaction = transaction,
            MaterialId = materialA.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = 5,
            AllocatedQuantityBoxes = 5,
            Status = AllocationStatus.Fulfilled
        };
        var putaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = current.Id,
            QuantityBoxes = 5,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(putaway);
        transaction.Lines.Add(line);
        db.InwardTransactions.Add(transaction);
        db.SaveChanges();

        return (db, materialA, materialB, current, target, putaway);
    }

    [Fact]
    public async Task ChangeLocation_Throws_WhenTargetHoldsConfirmedStockOfDifferentMaterial()
    {
        var (db, _, materialB, _, target, putaway) = SeedScenario();
        db.StockBatches.Add(new StockBatch { MaterialId = materialB.Id, PalletPositionId = target.Id, MfgMonth = 201001, QuantityBoxes = 5 });
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayLocationHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeLocation_Throws_WhenTargetHasPendingReservationOfDifferentMaterial()
    {
        var (db, _, materialB, _, target, putaway) = SeedScenario();
        var otherTransaction = new InwardTransaction { ReferenceNumber = "GRN-TEST-2" };
        var otherLine = new InwardTransactionLine
        {
            InwardTransaction = otherTransaction,
            MaterialId = materialB.Id,
            MfgMonth = 201001,
            RequestedQuantityBoxes = 5,
            AllocatedQuantityBoxes = 5,
            Status = AllocationStatus.Fulfilled
        };
        otherLine.Putaways.Add(new InwardPutaway { InwardTransactionLine = otherLine, PalletPositionId = target.Id, QuantityBoxes = 5 });
        otherTransaction.Lines.Add(otherLine);
        db.InwardTransactions.Add(otherTransaction);
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayLocationHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ChangeLocation_Throws_WhenWarehouseBlocksPkmMixingAndTargetHasDifferentPkm()
    {
        var (db, materialA, _, _, target, putaway) = SeedScenario();
        db.Warehouses.Add(new Warehouse
        {
            Id = 1,
            Code = "GCPL",
            Name = "GCPL",
            AllowPkmMixing = false
        });
        db.StockBatches.Add(new StockBatch
        {
            MaterialId = materialA.Id,
            PalletPositionId = target.Id,
            MfgMonth = 201002,
            QuantityBoxes = 5
        });
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayLocationHandler(db);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id, "Target changed"), CancellationToken.None));

        Assert.Contains("does not allow PKM mixing", error.Message);
    }

    [Fact]
    public async Task ChangeLocation_Succeeds_AndRecordsOverrideReason_WhenTargetIsFree()
    {
        var (db, _, _, _, target, putaway) = SeedScenario();
        var handler = new ChangeInwardPutawayLocationHandler(db);

        await handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id, "Damaged pallet in original slot"), CancellationToken.None);

        var moved = db.InwardPutaways.Single(p => p.Id == putaway.Id);
        Assert.Equal(target.Id, moved.PalletPositionId);
        Assert.Equal("Damaged pallet in original slot", moved.OverrideReason);
        Assert.Equal(1u, moved.RowVersion);
    }

    [Fact]
    public async Task ChangeAllocation_UpdatesQuantityAtCurrentLocation_AndRecalculatesLine()
    {
        var (db, _, _, current, _, putaway) = SeedScenario();
        var handler = new ChangeInwardPutawayLocationHandler(db);

        await handler.Handle(
            new ChangeInwardPutawayLocationCommand(putaway.Id, current.Id, "Only three boxes received", 3),
            CancellationToken.None);

        var updated = db.InwardPutaways.Single(p => p.Id == putaway.Id);
        var line = db.InwardTransactionLines.Single(l => l.Id == putaway.InwardTransactionLineId);
        Assert.Equal(3, updated.QuantityBoxes);
        Assert.Equal(current.Id, updated.PalletPositionId);
        Assert.Equal(3, line.AllocatedQuantityBoxes);
        Assert.Equal(AllocationStatus.Partial, line.Status);
        Assert.Contains("2 box(es) remain unallocated", line.Remarks);
    }

    [Fact]
    public async Task ChangeAllocation_RejectsQuantityAboveRequestedLineTotal()
    {
        var (db, _, _, current, _, putaway) = SeedScenario();
        var handler = new ChangeInwardPutawayLocationHandler(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ChangeInwardPutawayLocationCommand(putaway.Id, current.Id, "Increase", 6),
            CancellationToken.None));

        Assert.Contains("cannot exceed 5", error.Message);
        Assert.Equal(5, db.InwardPutaways.Single(p => p.Id == putaway.Id).QuantityBoxes);
    }

    [Fact]
    public async Task ChangeAllocation_RejectsConfirmedPutaway()
    {
        var (db, _, _, current, _, putaway) = SeedScenario();
        putaway.IsConfirmed = true;
        await db.SaveChangesAsync();
        var handler = new ChangeInwardPutawayLocationHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ChangeInwardPutawayLocationCommand(putaway.Id, current.Id, "Reduce", 3),
            CancellationToken.None));
    }

    [Fact]
    public async Task ChangeAllocations_SplitsPutawayAcrossMultipleLocations()
    {
        var (db, _, _, current, target, putaway) = SeedScenario();
        var handler = new ChangeInwardPutawayAllocationsHandler(db);

        await handler.Handle(new ChangeInwardPutawayAllocationsCommand(
            putaway.Id,
            [new(current.Id, 2), new(target.Id, 3)],
            "Split receipt across two slots"), CancellationToken.None);

        var allocations = db.InwardPutaways.Where(p => p.InwardTransactionLineId == putaway.InwardTransactionLineId).ToList();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(5, allocations.Sum(p => p.QuantityBoxes));
        Assert.Contains(allocations, p => p.PalletPositionId == current.Id && p.QuantityBoxes == 2);
        Assert.Contains(allocations, p => p.PalletPositionId == target.Id && p.QuantityBoxes == 3);
    }

    [Fact]
    public async Task ChangeAllocations_RejectsCombinedQuantityAboveInwardQuantity()
    {
        var (db, _, _, current, target, putaway) = SeedScenario();
        var handler = new ChangeInwardPutawayAllocationsHandler(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ChangeInwardPutawayAllocationsCommand(putaway.Id,
                [new(current.Id, 3), new(target.Id, 3)], "Too many"), CancellationToken.None));

        Assert.Single(db.InwardPutaways.Where(p => p.InwardTransactionLineId == putaway.InwardTransactionLineId));
        Assert.Equal(5, putaway.QuantityBoxes);
    }

    [Fact]
    public async Task ChangeAllocations_FromHistory_CanSplitAnExistingAllocationAcrossMoreLocations()
    {
        var (db, _, _, current, target, putaway) = SeedScenario();
        var third = new PalletPosition { RackId = current.RackId, Column = 3, Level = 1, LocationCode = "A-03-01" };
        db.PalletPositions.Add(third);
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayAllocationsHandler(db);
        await handler.Handle(new ChangeInwardPutawayAllocationsCommand(
            putaway.Id,
            [new(current.Id, 2), new(target.Id, 3)],
            "Initial split"), CancellationToken.None);

        var historyAllocation = db.InwardPutaways.Single(p => p.PalletPositionId == current.Id);
        await handler.Handle(new ChangeInwardPutawayAllocationsCommand(
            historyAllocation.Id,
            [new(current.Id, 1), new(third.Id, 1)],
            "Split history allocation"), CancellationToken.None);

        var allocations = db.InwardPutaways
            .Where(p => p.InwardTransactionLineId == putaway.InwardTransactionLineId)
            .ToList();
        Assert.Equal(3, allocations.Count);
        Assert.Equal(5, allocations.Sum(p => p.QuantityBoxes));
        Assert.Contains(allocations, p => p.PalletPositionId == current.Id && p.QuantityBoxes == 1);
        Assert.Contains(allocations, p => p.PalletPositionId == target.Id && p.QuantityBoxes == 3);
        Assert.Contains(allocations, p => p.PalletPositionId == third.Id && p.QuantityBoxes == 1);
    }

    [Fact]
    public async Task ChangeAllocations_FromHistory_RejectsAggregateAboveInwardQuantity()
    {
        var (db, _, _, current, target, putaway) = SeedScenario();
        var third = new PalletPosition { RackId = current.RackId, Column = 3, Level = 1, LocationCode = "A-03-01" };
        db.PalletPositions.Add(third);
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayAllocationsHandler(db);
        await handler.Handle(new ChangeInwardPutawayAllocationsCommand(
            putaway.Id,
            [new(current.Id, 2), new(target.Id, 3)],
            "Initial split"), CancellationToken.None);

        var historyAllocation = db.InwardPutaways.Single(p => p.PalletPositionId == current.Id);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ChangeInwardPutawayAllocationsCommand(
                historyAllocation.Id,
                [new(current.Id, 2), new(third.Id, 1)],
                "Too many from history"), CancellationToken.None));

        Assert.Contains("exceed the material's inward quantity", error.Message);
        var allocations = db.InwardPutaways
            .Where(p => p.InwardTransactionLineId == putaway.InwardTransactionLineId)
            .ToList();
        Assert.Equal(2, allocations.Count);
        Assert.Equal(5, allocations.Sum(p => p.QuantityBoxes));
    }

    [Fact]
    public async Task ChangeLocation_Throws_WhenTargetIsOutsideRequiredZone()
    {
        var (db, materialA, _, _, target, putaway) = SeedScenario();
        materialA.PreferredZoneTypeId = ZoneTypeIds.Fast;
        materialA.RequirePreferredZone = true;
        // target keeps its default ZoneTypeId (Reserve), which is outside the required zone.
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayLocationHandler(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new ChangeInwardPutawayLocationCommand(putaway.Id, target.Id, "Test reason"), CancellationToken.None));
        Assert.Contains("required zone", ex.Message);
    }

    [Fact]
    public async Task GetChangeOptions_ExcludesPositionsOutsideRequiredZone()
    {
        var (db, materialA, _, _, target, putaway) = SeedScenario();
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        materialA.PreferredZoneTypeId = ZoneTypeIds.Fast;
        materialA.RequirePreferredZone = true;
        await db.SaveChangesAsync();

        var handler = new GetInwardPalletChangeOptionsHandler(db);

        var options = await handler.Handle(new GetInwardPalletChangeOptionsQuery(putaway.Id), CancellationToken.None);

        Assert.DoesNotContain(options, o => o.Id == target.Id);
    }

    [Fact]
    public async Task GetChangeOptions_IncludesPositionsInsideRequiredZone()
    {
        var (db, materialA, _, _, target, putaway) = SeedScenario();
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        materialA.PreferredZoneTypeId = ZoneTypeIds.Reserve; // matches target's (default) zone
        materialA.RequirePreferredZone = true;
        await db.SaveChangesAsync();

        var handler = new GetInwardPalletChangeOptionsHandler(db);

        var options = await handler.Handle(new GetInwardPalletChangeOptionsQuery(putaway.Id), CancellationToken.None);

        Assert.Contains(options, o => o.Id == target.Id);
    }

    [Fact]
    public async Task GetChangeOptions_ExcludesPositionsHeldByDifferentMaterial()
    {
        var (db, _, materialB, _, target, putaway) = SeedScenario();
        db.LocationSubtypes.Add(new LocationSubtype { Id = LocationSubtypeIds.Good, Code = "GOOD", DisplayName = "Good" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        db.StockBatches.Add(new StockBatch { MaterialId = materialB.Id, PalletPositionId = target.Id, MfgMonth = 201001, QuantityBoxes = 5 });
        await db.SaveChangesAsync();

        var handler = new GetInwardPalletChangeOptionsHandler(db);

        var options = await handler.Handle(new GetInwardPalletChangeOptionsQuery(putaway.Id), CancellationToken.None);

        Assert.DoesNotContain(options, o => o.Id == target.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validator_Rejects_MissingOrBlankReason(string? reason)
    {
        var validator = new ChangeInwardPutawayLocationValidator();

        var result = await validator.ValidateAsync(new ChangeInwardPutawayLocationCommand(1, 2, reason));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangeInwardPutawayLocationCommand.Reason));
    }

    [Fact]
    public async Task Validator_Accepts_NonBlankReason()
    {
        var validator = new ChangeInwardPutawayLocationValidator();

        var result = await validator.ValidateAsync(new ChangeInwardPutawayLocationCommand(1, 2, "Damaged pallet"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ChangeRack_MovesAllSelectedReservations_InOneRequest()
    {
        var (db, _, _, _, _, firstPutaway) = SeedScenario();
        var destinationRack = new Rack { Code = "B", Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        destinationRack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "B-01-01" });
        destinationRack.PalletPositions.Add(new PalletPosition { Column = 2, Level = 1, LocationCode = "B-02-01" });
        db.Racks.Add(destinationRack);

        var line = firstPutaway.InwardTransactionLine;
        var secondPutaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = firstPutaway.PalletPositionId,
            QuantityBoxes = 5,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(secondPutaway);
        await db.SaveChangesAsync();

        var handler = new ChangeInwardPutawayRackHandler(db);
        await handler.Handle(
            new ChangeInwardPutawayRackCommand([firstPutaway.Id, secondPutaway.Id], "B", "Use Rack B"),
            CancellationToken.None);

        var destinationPositionIds = destinationRack.PalletPositions.Select(p => p.Id).ToHashSet();
        var moved = db.InwardPutaways.Where(p => p.Id == firstPutaway.Id || p.Id == secondPutaway.Id).ToList();
        Assert.All(moved, p => Assert.Contains(p.PalletPositionId, destinationPositionIds));
        Assert.All(moved, p => Assert.Equal("Use Rack B", p.OverrideReason));
    }

    [Fact]
    public async Task ChangeRack_MakesNoChanges_WhenRackCannotFitCompleteSelection()
    {
        var (db, material, _, _, _, firstPutaway) = SeedScenario();
        firstPutaway.QuantityBoxes = 50;
        var destinationRack = new Rack { Code = "B", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        destinationRack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "B-01-01", MaxPallets = 2 });
        db.Racks.Add(destinationRack);

        var line = firstPutaway.InwardTransactionLine;
        var secondPutaway = new InwardPutaway
        {
            InwardTransactionLine = line,
            PalletPositionId = firstPutaway.PalletPositionId,
            QuantityBoxes = 50,
            AllocationReason = "test seed"
        };
        line.Putaways.Add(secondPutaway);
        await db.SaveChangesAsync();
        var originalPositionId = firstPutaway.PalletPositionId;

        var handler = new ChangeInwardPutawayRackHandler(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ChangeInwardPutawayRackCommand([firstPutaway.Id, secondPutaway.Id], "B", "Use Rack B"),
            CancellationToken.None));

        Assert.Equal(originalPositionId, firstPutaway.PalletPositionId);
        Assert.Equal(originalPositionId, secondPutaway.PalletPositionId);
        Assert.Null(firstPutaway.OverrideReason);
        Assert.Null(secondPutaway.OverrideReason);
        Assert.Equal(40, material.PalletCapacityBoxes);
    }

    [Fact]
    public async Task ExcelReassignment_MovesPendingPutaway_WhenWorkbookIsValid()
    {
        var (db, material, _, _, target, putaway) = SeedScenario();
        var service = new ClosedXmlExcelService();
        using var workbook = BuildReassignmentWorkbook(service, material, putaway, target.LocationCode);
        var handler = new ImportInwardPutawayLocationsHandler(db, service);

        var result = await handler.Handle(
            new ImportInwardPutawayLocationsCommand(
                putaway.InwardTransactionLine.InwardTransactionId, workbook, "Original slot unavailable"),
            CancellationToken.None);

        Assert.Equal(1, result.UpdatedLocations);
        var moved = db.InwardPutaways.Single(p => p.Id == putaway.Id);
        Assert.Equal(target.Id, moved.PalletPositionId);
        Assert.Equal("Original slot unavailable", moved.OverrideReason);
        Assert.Equal("Location reassigned from GRN Excel upload", moved.AllocationReason);
    }

    [Fact]
    public async Task ExcelReassignment_ChangesPendingQuantity_AndRecalculatesLine()
    {
        var (db, material, _, current, _, putaway) = SeedScenario();
        var service = new ClosedXmlExcelService();
        using var workbook = BuildReassignmentWorkbook(service, material, putaway, current.LocationCode, quantityBoxes: 3);
        var handler = new ImportInwardPutawayLocationsHandler(db, service);

        var result = await handler.Handle(
            new ImportInwardPutawayLocationsCommand(
                putaway.InwardTransactionLine.InwardTransactionId, workbook, "Only three boxes received"),
            CancellationToken.None);

        Assert.Equal(0, result.UpdatedLocations);
        Assert.Equal(1, result.UpdatedQuantities);
        Assert.Equal(3, db.InwardPutaways.Single(p => p.Id == putaway.Id).QuantityBoxes);
        var line = db.InwardTransactionLines.Single(l => l.Id == putaway.InwardTransactionLineId);
        Assert.Equal(3, line.AllocatedQuantityBoxes);
        Assert.Equal(AllocationStatus.Partial, line.Status);
    }

    [Fact]
    public async Task ExcelReassignment_RejectsSkuMismatch_WithoutChangingLocation()
    {
        var (db, material, _, current, target, putaway) = SeedScenario();
        var service = new ClosedXmlExcelService();
        using var workbook = BuildReassignmentWorkbook(service, material, putaway, target.LocationCode, materialNumber: 999);
        var handler = new ImportInwardPutawayLocationsHandler(db, service);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ImportInwardPutawayLocationsCommand(
                putaway.InwardTransactionLine.InwardTransactionId, workbook, "Excel update"),
            CancellationToken.None));

        Assert.Contains("SKU mismatch", error.Message);
        Assert.Equal(current.Id, db.InwardPutaways.Single(p => p.Id == putaway.Id).PalletPositionId);
    }

    [Fact]
    public async Task ExcelReassignment_RejectsPkmMismatch_WithoutChangingLocation()
    {
        var (db, material, _, current, target, putaway) = SeedScenario();
        var service = new ClosedXmlExcelService();
        using var workbook = BuildReassignmentWorkbook(service, material, putaway, target.LocationCode, pkm: "FEB|2010");
        var handler = new ImportInwardPutawayLocationsHandler(db, service);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ImportInwardPutawayLocationsCommand(
                putaway.InwardTransactionLine.InwardTransactionId, workbook, "Excel update"),
            CancellationToken.None));

        Assert.Contains("PKM mismatch", error.Message);
        Assert.Equal(current.Id, db.InwardPutaways.Single(p => p.Id == putaway.Id).PalletPositionId);
    }

    [Fact]
    public async Task ExcelReassignment_RejectsInsufficientCapacity_WithoutChangingLocation()
    {
        var (db, material, _, current, target, putaway) = SeedScenario();
        db.StockBatches.Add(new StockBatch
        {
            MaterialId = material.Id,
            PalletPositionId = target.Id,
            MfgMonth = 200912,
            QuantityBoxes = 79
        });
        await db.SaveChangesAsync();
        var service = new ClosedXmlExcelService();
        using var workbook = BuildReassignmentWorkbook(service, material, putaway, target.LocationCode);
        var handler = new ImportInwardPutawayLocationsHandler(db, service);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ImportInwardPutawayLocationsCommand(
                putaway.InwardTransactionLine.InwardTransactionId, workbook, "Excel update"),
            CancellationToken.None));

        Assert.Contains("free box", error.Message);
        Assert.Equal(current.Id, db.InwardPutaways.Single(p => p.Id == putaway.Id).PalletPositionId);
    }

    private static MemoryStream BuildReassignmentWorkbook(
        ClosedXmlExcelService service,
        Material material,
        InwardPutaway putaway,
        string targetLocation,
        long? materialNumber = null,
        string pkm = "JAN|2010",
        decimal? quantityBoxes = null)
    {
        var line = putaway.InwardTransactionLine;
        var rows = new[]
        {
            new InwardDownloadRow(
                line.InwardTransaction.ReferenceNumber,
                materialNumber ?? material.MaterialNumber,
                material.Description,
                pkm,
                line.RequestedQuantityBoxes,
                line.AllocatedQuantityBoxes,
                line.Status.ToString(),
                targetLocation,
                quantityBoxes ?? putaway.QuantityBoxes,
                "Reserved",
                putaway.Id)
        };
        return new MemoryStream(service.WriteInwardDownload(rows));
    }
}
