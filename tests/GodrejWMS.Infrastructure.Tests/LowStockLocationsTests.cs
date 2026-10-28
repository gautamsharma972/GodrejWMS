using GodrejWMS.Application.Features.StockMaster.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class LowStockLocationsTests
{
    [Fact]
    public async Task IncludesOnlyPositiveStockAtOrBelowTenPercentOfMaterialPalletCapacity()
    {
        await using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Name = "A", Columns = 3, Levels = 1 };
        var low = new PalletPosition { Rack = rack, LocationCode = "A-01-01", Column = 1, Level = 1 };
        var boundary = new PalletPosition { Rack = rack, LocationCode = "A-02-01", Column = 2, Level = 1 };
        var above = new PalletPosition { Rack = rack, LocationCode = "A-03-01", Column = 3, Level = 1 };
        var material = new Material { MaterialNumber = 40019311, Description = "Test", DesignType = "X", PalletCapacityBoxes = 20, SeasonId = SeasonIds.Rainy };
        db.AddRange(rack, low, boundary, above, material);
        db.StockBatches.AddRange(
            new StockBatch { Material = material, PalletPosition = low, MfgMonth = 202610, QuantityBoxes = 1 },
            new StockBatch { Material = material, PalletPosition = boundary, MfgMonth = 202610, QuantityBoxes = 2 },
            new StockBatch { Material = material, PalletPosition = above, MfgMonth = 202610, QuantityBoxes = 3 });
        await db.SaveChangesAsync();

        var rows = await new GetLowStockLocationsHandler(db).Handle(new(), default);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(row.QuantityBoxes <= 2));
        Assert.Contains(rows, row => row.LocationCode == "A-02-01" && row.ThresholdBoxes == 2);
    }
}
