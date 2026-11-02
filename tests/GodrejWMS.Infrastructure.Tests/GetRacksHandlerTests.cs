using GodrejWMS.Application.Features.Racks.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers <see cref="GetRacksHandler"/> after it was rewritten from one correlated
/// subquery per pallet position into two flat queries (rack+positions, then a single GROUP BY over
/// StockBatches) for performance - this pins the occupancy/capacity math stays identical across
/// that rewrite.</summary>
public class GetRacksHandlerTests
{
    [Fact]
    public async Task Handle_ComputesOccupancyAndCapacity_AcrossMixedPositions()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", WarehouseId = 1, Columns = 2, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 2 });
        rack.PalletPositions.Add(new PalletPosition { Column = 2, Level = 1, LocationCode = "A-02-01", MaxPallets = 2 });
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 40070000, Description = "Test Material", DesignType = "X",
            PackSize = 24, GrossWeightKg = 0.5m, LengthMm = 100, WidthMm = 100, HeightMm = 100,
            PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        await db.SaveChangesAsync();

        var occupiedPosition = rack.PalletPositions.Single(p => p.LocationCode == "A-01-01");
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = occupiedPosition.Id, MfgMonth = 202501, QuantityBoxes = 15 });
        await db.SaveChangesAsync();

        var handler = new GetRacksHandler(db);
        var racks = await handler.Handle(new GetRacksQuery(), CancellationToken.None);

        var dto = Assert.Single(racks);
        Assert.Equal(2, dto.TotalPositions);
        Assert.Equal(1, dto.OccupiedPositions);
        Assert.Equal(50.0m, dto.OccupancyPercent);
        // Only the occupied position contributes capacity (MaxPallets 2 x PalletCapacityBoxes 40);
        // the empty position has no material context, so it contributes nothing.
        Assert.Equal(80, dto.TotalCapacityBoxes);
    }

    [Fact]
    public async Task Handle_FiltersByWarehouseId()
    {
        var db = TestDbContextFactory.Create();

        var rackA = new Rack { Code = "A", WarehouseId = 1, Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rackA.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 });
        var rackB = new Rack { Code = "B", WarehouseId = 2, Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rackB.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "B-01-01", MaxPallets = 1 });
        db.Racks.AddRange(rackA, rackB);
        await db.SaveChangesAsync();

        var handler = new GetRacksHandler(db);
        var racks = await handler.Handle(new GetRacksQuery(WarehouseId: 1), CancellationToken.None);

        var dto = Assert.Single(racks);
        Assert.Equal("A", dto.Code);
    }
}
