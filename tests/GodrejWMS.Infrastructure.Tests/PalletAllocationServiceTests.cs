using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PalletAllocationServiceTests
{
    private static (AppDbContext Db, Material Material, Rack Rack) SeedWarehouse(int capacityBoxes = 40)
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack
        {
            Code = "A",
            Columns = 2,
            Levels = 1,
            ShelfLengthMm = 2000,
            ShelfWidthMm = 2500,
            ShelfHeightMm = 3600
        };
        rack.PalletPositions.Add(new PalletPosition
        {
            Column = 1,
            Level = 1,
            LocationCode = "A-01-01",
            CapacityBoxes = capacityBoxes
        });
        rack.PalletPositions.Add(new PalletPosition
        {
            Column = 2,
            Level = 1,
            LocationCode = "A-02-01",
            CapacityBoxes = capacityBoxes
        });
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40000015,
            Description = "A.PUR AEROSOL A.THR MRP 110 PS 24",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = capacityBoxes,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);

        db.SaveChanges();
        return (db, material, rack);
    }

    private static (AppDbContext Db, Material Material, Rack RackA, Rack RackB) SeedTwoRackWarehouse(int capacityBoxes = 40, int levels = 3)
    {
        var db = TestDbContextFactory.Create();

        var rackA = BuildSingleColumnRack("A", levels, capacityBoxes);
        var rackB = BuildSingleColumnRack("B", levels, capacityBoxes);
        db.Racks.AddRange(rackA, rackB);

        var material = new Material
        {
            MaterialNumber = 40000020,
            Description = "TEST MATERIAL FOR CONSOLIDATION",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = capacityBoxes,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);

        db.SaveChanges();
        return (db, material, rackA, rackB);
    }

    private static Rack BuildSingleColumnRack(string code, int levels, int capacityBoxes)
    {
        var rack = new Rack
        {
            Code = code,
            Columns = 1,
            Levels = levels,
            ShelfLengthMm = 2000,
            ShelfWidthMm = 2500,
            ShelfHeightMm = 3600
        };

        for (var level = 1; level <= levels; level++)
        {
            rack.PalletPositions.Add(new PalletPosition
            {
                Column = 1,
                Level = level,
                LocationCode = $"{code}-01-{level:00}",
                CapacityBoxes = capacityBoxes
            });
        }

        return rack;
    }

    private static void AddStock(AppDbContext db, Material material, PalletPosition position, int mfgMonth, decimal quantityBoxes)
    {
        db.StockBatches.Add(new StockBatch
        {
            MaterialId = material.Id,
            PalletPositionId = position.Id,
            MfgMonth = mfgMonth,
            QuantityBoxes = quantityBoxes
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task AllocateAsync_TopsUpExistingBatch_BeforeUsingNextPosition()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        var mfgMonth = 201003;
        var position = db.PalletPositions.First(p => p.LocationCode == "A-01-01");

        db.StockBatches.Add(new StockBatch
        {
            MaterialId = material.Id,
            PalletPositionId = position.Id,
            MfgMonth = mfgMonth,
            QuantityBoxes = 35
        });
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 10, mfgMonth);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(10, result.AllocatedQuantityBoxes);
        Assert.Equal(2, result.Lines.Count);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-01" && l.QuantityBoxes == 5);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 5);
        Assert.Equal(35, db.StockBatches.Single(b => b.PalletPositionId == position.Id).QuantityBoxes);
    }

    [Fact]
    public async Task AllocateAsync_ReturnsPartial_WhenWarehouseCapacityIsExceeded()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 100, 201003);

        Assert.Equal(AllocationStatus.Partial, result.Status);
        Assert.Equal(80, result.AllocatedQuantityBoxes);
        Assert.NotNull(result.Remarks);
        Assert.Empty(db.StockBatches);
    }

    [Fact]
    public async Task AllocateAsync_SkipsInactiveExistingBatch_WhenToppingUp()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        var mfgMonth = 201003;
        var inactivePosition = db.PalletPositions.First(p => p.LocationCode == "A-01-01");
        inactivePosition.IsActive = false;

        db.StockBatches.Add(new StockBatch
        {
            MaterialId = material.Id,
            PalletPositionId = inactivePosition.Id,
            MfgMonth = mfgMonth,
            QuantityBoxes = 35
        });
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 5, mfgMonth);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.DoesNotContain(result.Lines, l => l.LocationCode == "A-01-01");
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 5);
        Assert.Equal(35, db.StockBatches.Single(b => b.PalletPositionId == inactivePosition.Id).QuantityBoxes);
    }

    [Fact]
    public async Task AllocateAsync_UsesSkuPalletCapacity_WhenLocationAllowsMoreBoxes()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 80);
        material.PalletCapacityBoxes = 20;
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 50, 201003);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-01" && l.QuantityBoxes == 40);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 10);
    }

    [Fact]
    public async Task AllocateAsync_TreatsPendingPutawaysAsReservedCapacity()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        var reservedPosition = db.PalletPositions.First(p => p.LocationCode == "A-01-01");

        db.InwardTransactions.Add(new InwardTransaction
        {
            ReferenceNumber = "GRN-PENDING",
            Lines =
            {
                new InwardTransactionLine
                {
                    MaterialId = material.Id,
                    MfgMonth = 201003,
                    RequestedQuantityBoxes = 40,
                    AllocatedQuantityBoxes = 40,
                    Status = AllocationStatus.Fulfilled,
                    Putaways =
                    {
                        new InwardPutaway
                        {
                            PalletPositionId = reservedPosition.Id,
                            QuantityBoxes = 40,
                            IsConfirmed = false
                        }
                    }
                }
            }
        });
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 80, 201003);

        Assert.Equal(AllocationStatus.Partial, result.Status);
        Assert.Equal(40, result.AllocatedQuantityBoxes);
        Assert.DoesNotContain(result.Lines, l => l.LocationCode == "A-01-01");
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 40);
    }

    [Fact]
    public async Task ConfirmInwardPutawayAsync_CreatesStockFromReservedLocations()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        var position = db.PalletPositions.First(p => p.LocationCode == "A-01-01");
        db.InwardTransactions.Add(new InwardTransaction
        {
            ReferenceNumber = "GRN-CONFIRM",
            Lines =
            {
                new InwardTransactionLine
                {
                    MaterialId = material.Id,
                    MfgMonth = 201003,
                    RequestedQuantityBoxes = 25,
                    AllocatedQuantityBoxes = 25,
                    Status = AllocationStatus.Fulfilled,
                    Putaways =
                    {
                        new InwardPutaway
                        {
                            PalletPositionId = position.Id,
                            QuantityBoxes = 25,
                            IsConfirmed = false
                        }
                    }
                }
            }
        });
        await db.SaveChangesAsync();

        var sut = new ConfirmInwardPutawayHandler(db, new TestClock(), new TestCurrentUser());

        var result = await sut.Handle(new ConfirmInwardPutawayCommand("GRN-CONFIRM"), CancellationToken.None);

        Assert.True(result.IsConfirmed);
        Assert.Equal(25, db.StockBatches.Single().QuantityBoxes);
        Assert.True(db.InwardPutaways.Single().IsConfirmed);
    }

    [Fact]
    public async Task ChangeInwardPutawayAsync_RejectsLocationWithDifferentSubtype()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        var goodPosition = db.PalletPositions.First(p => p.LocationCode == "A-01-01");
        var damagePosition = db.PalletPositions.First(p => p.LocationCode == "A-02-01");
        damagePosition.LocationSubtypeId = LocationSubtypeIds.Damage;

        var transaction = new InwardTransaction
        {
            ReferenceNumber = "GRN-SUBTYPE",
            Lines =
            {
                new InwardTransactionLine
                {
                    MaterialId = material.Id,
                    MfgMonth = 201003,
                    RequestedQuantityBoxes = 10,
                    AllocatedQuantityBoxes = 10,
                    Status = AllocationStatus.Fulfilled,
                    Putaways =
                    {
                        new InwardPutaway
                        {
                            PalletPositionId = goodPosition.Id,
                            QuantityBoxes = 10,
                            IsConfirmed = false
                        }
                    }
                }
            }
        };
        db.InwardTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var putawayId = transaction.Lines.Single().Putaways.Single().Id;
        var sut = new ChangeInwardPutawayLocationHandler(db);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.Handle(new ChangeInwardPutawayLocationCommand(putawayId, damagePosition.Id), CancellationToken.None));

        Assert.Contains("different location subtype", exception.Message);
        Assert.Equal(goodPosition.Id, db.InwardPutaways.Single(p => p.Id == putawayId).PalletPositionId);
    }

    [Fact]
    public async Task SubmitInwardAsync_PersistsAllocationReasonForAuditDetail()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        var allocator = new PalletAllocationService(db, new TestClock());
        var sut = new SubmitInwardHandler(db, allocator, new TestClock(), new TestCurrentUser());

        var result = await sut.Handle(
            new SubmitInwardCommand([new SubmitInwardLine(material.MaterialNumber, 10, "MAR|2010")]),
            CancellationToken.None);

        var putaway = db.InwardPutaways.Single();
        Assert.False(string.IsNullOrWhiteSpace(putaway.AllocationReason));
        Assert.Equal(putaway.AllocationReason, result.Lines.Single().AllocatedTo.Single().Reason);
    }

    [Fact]
    public async Task AllocateAsync_ConsolidatesInAnchorRack_BeforeSpillingToNextRack()
    {
        var (db, material, rackA, _) = SeedTwoRackWarehouse(capacityBoxes: 40, levels: 3);

        var levelOneA = rackA.PalletPositions.Single(p => p.Level == 1);
        AddStock(db, material, levelOneA, mfgMonth: 201001, quantityBoxes: 30);

        var sut = new PalletAllocationService(db, new TestClock());

        // Rack A's total spare capacity for this material, across all three levels, is 10 (spare
        // on A-01-01) + 40 (empty A-01-02) + 40 (empty A-01-03) = 90. Requesting 100 more (a
        // different Mfg Month) should exhaust every level of Rack A first, then spill the
        // remaining 10 into Rack B.
        var result = await sut.AllocateAsync(material.Id, 100, mfgMonth: 201002);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(100, result.AllocatedQuantityBoxes);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-01" && l.QuantityBoxes == 10);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-02" && l.QuantityBoxes == 40);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-03" && l.QuantityBoxes == 40);
        Assert.Contains(result.Lines, l => l.LocationCode == "B-01-01" && l.QuantityBoxes == 10);
    }

    [Fact]
    public async Task AllocateAsync_TreatsPositionHoldingDifferentMfgMonthBatch_AsValidConsolidationTarget()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);

        var position = db.PalletPositions.First(p => p.LocationCode == "A-01-01");
        AddStock(db, material, position, mfgMonth: 201001, quantityBoxes: 30);

        var sut = new PalletAllocationService(db, new TestClock());

        // Same material, a *different* Mfg Month - not a Tier-1 exact-batch top-up, but the
        // position already holding this material (different batch) should still be a valid
        // Tier-2 consolidation target, not require an empty position.
        var result = await sut.AllocateAsync(material.Id, 15, mfgMonth: 201002);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-01" && l.QuantityBoxes == 10 && l.Reason == "Same-rack consolidation");
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 5);
    }

    [Fact]
    public async Task AllocateAsync_ExcludesPositionHoldingDifferentMaterial_FromConsolidationTier()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);

        var otherMaterial = new Material
        {
            MaterialNumber = 40000021,
            Description = "OTHER MATERIAL",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 0.5m,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(otherMaterial);
        await db.SaveChangesAsync();

        var positionWithOtherMaterial = db.PalletPositions.First(p => p.LocationCode == "A-01-01");
        var positionWithOurMaterial = db.PalletPositions.First(p => p.LocationCode == "A-02-01");
        AddStock(db, otherMaterial, positionWithOtherMaterial, mfgMonth: 201001, quantityBoxes: 5);
        AddStock(db, material, positionWithOurMaterial, mfgMonth: 201001, quantityBoxes: 5);

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 10, mfgMonth: 201002);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.DoesNotContain(result.Lines, l => l.LocationCode == "A-01-01");
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 10);
    }

    [Fact]
    public async Task AllocateAsync_SkipsConsolidationTier_ForBrandNewMaterialWithNoFootprint()
    {
        var (db, material, _, _) = SeedTwoRackWarehouse(capacityBoxes: 40, levels: 3);
        material.MovementTypeId = SkuMovementTypeIds.SlowMoving;
        material.SeasonId = SeasonIds.Summer; // does not match the Rainy fallback active season
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        // Request more than a single Level 1 position can hold (40) so the engine is forced to
        // reach Level 2 and 3 within Rack A. This material has no existing footprint anywhere, so
        // Tier 2 (same-rack consolidation) must be skipped entirely - the fill instead comes from
        // Tier 3's plain empty-position scoring.
        var result = await sut.AllocateAsync(material.Id, 90, mfgMonth: 201001);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-01" && l.QuantityBoxes == 40);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-02" && l.QuantityBoxes == 40);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-03" && l.QuantityBoxes == 10);
    }

    [Fact]
    public async Task AllocateAsync_ConsolidationTierReachesEveryLevel_WhenLowerLevelsAreFull()
    {
        var (db, material, rackA, rackB) = SeedTwoRackWarehouse(capacityBoxes: 10, levels: 3);

        // Fill Level 1 and Level 2 of both racks to capacity for this material, leaving only
        // Level 3 positions with any spare capacity anywhere in the warehouse.
        AddStock(db, material, rackA.PalletPositions.Single(p => p.Level == 1), mfgMonth: 201001, quantityBoxes: 10);
        AddStock(db, material, rackA.PalletPositions.Single(p => p.Level == 2), mfgMonth: 201002, quantityBoxes: 10);
        AddStock(db, material, rackB.PalletPositions.Single(p => p.Level == 1), mfgMonth: 201003, quantityBoxes: 10);
        AddStock(db, material, rackB.PalletPositions.Single(p => p.Level == 2), mfgMonth: 201004, quantityBoxes: 10);

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 5, mfgMonth: 201005);

        // Every rack is full at Level 1 and 2, but Level 3 has room - Tier 2 (same-rack
        // consolidation) is unrestricted by level, so it reaches Level 3 directly rather than
        // reporting Partial/Failed.
        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Contains(result.Lines, l => l.LocationCode.EndsWith("-03"));
    }

    [Fact]
    public async Task AllocateAsync_RanksRackByExistingConcentration_NotAlphabeticalCode()
    {
        var (db, material, rackA, rackB) = SeedTwoRackWarehouse(capacityBoxes: 40, levels: 3);

        AddStock(db, material, rackA.PalletPositions.Single(p => p.Level == 1), mfgMonth: 201001, quantityBoxes: 5);
        AddStock(db, material, rackB.PalletPositions.Single(p => p.Level == 1), mfgMonth: 201002, quantityBoxes: 30);

        var sut = new PalletAllocationService(db, new TestClock());

        // Rack B holds far more of this material (30) than Rack A (5), so despite "A" sorting
        // first alphabetically, Rack B should be visited first and fully absorb this request.
        var result = await sut.AllocateAsync(material.Id, 8, mfgMonth: 201003);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Single(result.Lines);
        Assert.Contains(result.Lines, l => l.LocationCode == "B-01-01" && l.QuantityBoxes == 8);
    }

    [Fact]
    public async Task AllocateAsync_PrefersMaterialsPreferredZone_AsSoftTiebreak()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        material.MovementTypeId = SkuMovementTypeIds.SlowMoving;
        material.SeasonId = SeasonIds.Summer; // does not match the Rainy fallback active season
        material.PreferredZoneTypeId = ZoneTypeIds.Seasonal;
        var fastPosition = db.PalletPositions.Single(p => p.LocationCode == "A-01-01");
        fastPosition.ZoneTypeId = ZoneTypeIds.Fast;
        var seasonalPosition = db.PalletPositions.Single(p => p.LocationCode == "A-02-01");
        seasonalPosition.ZoneTypeId = ZoneTypeIds.Seasonal;
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 5, mfgMonth: 201001);

        // For this off-season, slow-moving material, plain zone scoring ranks Fast (90) ahead of
        // Seasonal (95). The material's soft PreferredZoneTypeId nudges Seasonal to 83, flipping
        // the choice even though neither zone's hard eligibility rule applies to this SKU.
        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 5);
        Assert.DoesNotContain(result.Lines, l => l.LocationCode == "A-01-01");
    }

    [Fact]
    public async Task AllocateAsync_FillsRackInColumnThenLevelOrder_ForWeightNeutralMaterial()
    {
        // Reproduces the spec's own worked example: 380 boxes, pallet size 40, into a fresh
        // 2-column x 5-level rack, with no zone/design/weight signal to compete with the base
        // Rack -> Column -> Level fill sequence.
        var db = TestDbContextFactory.Create();
        var rack = BuildRack("A", columns: 2, levels: 5, capacityBoxes: 40);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40000030,
            Description = "WEIGHT-NEUTRAL TEST MATERIAL",
            DesignType = "XOLDH",
            PackSize = 1,
            GrossWeightKg = 0,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 380, mfgMonth: 201001);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Equal(380, result.AllocatedQuantityBoxes);
        for (var column = 1; column <= 2; column++)
        {
            for (var level = 1; level <= 5; level++)
            {
                var expected = (column - 1) * 200 + (level - 1) * 40 < 380
                    ? Math.Min(40, 380 - ((column - 1) * 200 + (level - 1) * 40))
                    : 0;
                var locationCode = $"A-{column:00}-{level:00}";
                if (expected > 0)
                {
                    Assert.Contains(result.Lines, l => l.LocationCode == locationCode && l.QuantityBoxes == expected);
                }
                else
                {
                    Assert.DoesNotContain(result.Lines, l => l.LocationCode == locationCode);
                }
            }
        }
    }

    [Fact]
    public async Task AllocateAsync_PrefersLowerLevelAcrossColumns_ForHeavyMaterial()
    {
        var db = TestDbContextFactory.Create();
        var rack = BuildRack("A", columns: 2, levels: 2, capacityBoxes: 10);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40000031,
            Description = "HEAVY TEST MATERIAL",
            DesignType = "XOLDH",
            PackSize = 24,
            GrossWeightKg = 5, // BoxWeightKg = 120, a strong level-preference signal
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 10,
            SeasonId = SeasonIds.Rainy
        };
        material.RecalculateDerivedFields(); // populates BoxWeightKg = GrossWeightKg * PackSize = 120
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        // Plain column-major order would fill A-01-01 then A-01-02 (both column 1) before ever
        // touching column 2. Weight instead groups by level across both columns first.
        var result = await sut.AllocateAsync(material.Id, 15, mfgMonth: 201001);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-01-01" && l.QuantityBoxes == 10);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 5);
        Assert.DoesNotContain(result.Lines, l => l.LocationCode == "A-01-02");
    }

    [Fact]
    public async Task AllocateAsync_PrefersRackWithSameDesignCode_ForBrandNewMaterial()
    {
        // Rack B's Level 1 is occupied by a different SKU sharing the incoming material's Design
        // Code, leaving Level 2 as the nearest eligible empty slot for the new material.
        var (db, existingMaterial, rackA, rackB) = SeedTwoRackWarehouse(capacityBoxes: 40, levels: 2);
        existingMaterial.DesignType = "D100";
        AddStock(db, existingMaterial, rackB.PalletPositions.Single(p => p.Level == 1), mfgMonth: 201001, quantityBoxes: 5);

        var newMaterial = new Material
        {
            MaterialNumber = 40000032,
            Description = "SAME DESIGN CODE, DIFFERENT SKU",
            DesignType = "D100",
            PackSize = 24,
            GrossWeightKg = 0,
            LengthMm = 100,
            WidthMm = 100,
            HeightMm = 100,
            PalletCapacityBoxes = 40,
            SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(newMaterial);
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        // Rack A sorts first alphabetically and has no footprint of anything, but Rack B already
        // holds a different SKU with the same Design Code - proximity should win, landing on Rack
        // B's empty Level 2 rather than mixing into Level 1's occupied (different-SKU) position.
        var result = await sut.AllocateAsync(newMaterial.Id, 8, mfgMonth: 201002);

        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Single(result.Lines);
        Assert.Contains(result.Lines, l => l.LocationCode == "B-01-02" && l.QuantityBoxes == 8);
    }

    [Fact]
    public async Task AllocateAsync_NeverUsesAnyOtherZone_WhenMaterialRequiresItsPreferredZone()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        material.MovementTypeId = SkuMovementTypeIds.FastMoving; // would normally make Fast the best-scoring zone by far
        material.PreferredZoneTypeId = ZoneTypeIds.Seasonal;
        material.RequirePreferredZone = true;
        var fastPosition = db.PalletPositions.Single(p => p.LocationCode == "A-01-01");
        fastPosition.ZoneTypeId = ZoneTypeIds.Fast;
        var seasonalPosition = db.PalletPositions.Single(p => p.LocationCode == "A-02-01");
        seasonalPosition.ZoneTypeId = ZoneTypeIds.Seasonal;
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 5, mfgMonth: 201001);

        // Fast would ordinarily win decisively (score 20 vs Seasonal's fallback 95) - the hard
        // constraint must exclude it from candidacy entirely regardless of scoring.
        Assert.Equal(AllocationStatus.Fulfilled, result.Status);
        Assert.Contains(result.Lines, l => l.LocationCode == "A-02-01" && l.QuantityBoxes == 5);
        Assert.DoesNotContain(result.Lines, l => l.LocationCode == "A-01-01");
    }

    [Fact]
    public async Task AllocateAsync_ReturnsFailed_WhenNoCapacityExistsInTheRequiredZone()
    {
        var (db, material, _) = SeedWarehouse(capacityBoxes: 40);
        material.PreferredZoneTypeId = ZoneTypeIds.Seasonal;
        material.RequirePreferredZone = true;
        // Neither seeded position is Seasonal (both default to Reserve).
        await db.SaveChangesAsync();

        var sut = new PalletAllocationService(db, new TestClock());

        var result = await sut.AllocateAsync(material.Id, 5, mfgMonth: 201001);

        Assert.Equal(AllocationStatus.Failed, result.Status);
        Assert.Empty(result.Lines);
        Assert.Contains("required zone", result.Remarks);
    }

    private static Rack BuildRack(string code, int columns, int levels, int capacityBoxes)
    {
        var rack = new Rack
        {
            Code = code,
            Columns = columns,
            Levels = levels,
            ShelfLengthMm = 2000,
            ShelfWidthMm = 2500,
            ShelfHeightMm = 3600
        };

        for (var column = 1; column <= columns; column++)
        {
            for (var level = 1; level <= levels; level++)
            {
                rack.PalletPositions.Add(new PalletPosition
                {
                    Column = column,
                    Level = level,
                    LocationCode = $"{code}-{column:00}-{level:00}",
                    CapacityBoxes = capacityBoxes
                });
            }
        }

        return rack;
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
