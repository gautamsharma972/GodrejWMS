using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class RetryInwardUnsavedLinesTests
{
    private static async Task<(AppDbContext Db, PalletAllocationService Allocator)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 4, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var level = 1; level <= 4; level++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = level, LocationCode = $"A-01-{level:00}", MaxPallets = 1 });
        }
        db.Racks.Add(rack);
        db.Materials.Add(NewMaterial(1));
        await db.SaveChangesAsync();
        return (db, new PalletAllocationService(db, new RetryClock()));
    }

    private static Material NewMaterial(long number) => new()
    {
        MaterialNumber = number, Description = $"M{number}", DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
        LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
    };

    private static Task<GodrejWMS.Application.Features.Inward.Dtos.InwardResultDto> SubmitAsync(AppDbContext db, PalletAllocationService allocator) =>
        new SubmitInwardHandler(db, TestServices.Placement(db, allocator), TestServices.Uow(db), new RetryClock(), new RetryUser()).Handle(
            new SubmitInwardCommand(
                [new SubmitInwardLine(1, 10, "OCT|2026"), new SubmitInwardLine(40064374, 50, "OCT|2026", "PA DISCOVER")],
                WarehouseId: 1),
            default);

    [Fact]
    public async Task Retry_AllocatesTheLine_OnceTheMaterialExists_AndClearsItFromTheList()
    {
        var (db, allocator) = await SeedAsync();
        var grn = await SubmitAsync(db, allocator);
        var unsaved = Assert.Single(db.InwardUnsavedLines);
        Assert.Equal(202610, unsaved.MfgMonth);

        db.Materials.Add(NewMaterial(40064374));
        await db.SaveChangesAsync();

        var result = await new RetryInwardUnsavedLinesHandler(db, TestServices.Placement(db, allocator), TestServices.Uow(db))
            .Handle(new RetryInwardUnsavedLinesCommand(grn.Id), default);

        Assert.Equal(1, result.PlacedLines);
        Assert.Equal(50, result.PlacedBoxes);
        Assert.Empty(db.InwardUnsavedLines);
        var line = db.InwardTransactionLines.Include(l => l.Material).Single(l => l.Material.MaterialNumber == 40064374);
        Assert.Equal(50, line.RequestedQuantityBoxes);
        Assert.Equal(50, line.AllocatedQuantityBoxes);
        Assert.Equal(202610, line.MfgMonth);
        // With a PKM the retried line is posted straight into inventory.
        Assert.Equal(50, db.InwardPutaways.Where(p => p.InwardTransactionLineId == line.Id && p.IsConfirmed).Sum(p => p.QuantityBoxes));
        Assert.Equal(50, db.StockBatches.Where(b => b.Material.MaterialNumber == 40064374).Sum(b => b.QuantityBoxes));
    }

    [Fact]
    public async Task Retry_LeavesLinesWhoseMaterialIsStillMissing()
    {
        var (db, allocator) = await SeedAsync();
        var grn = await SubmitAsync(db, allocator);

        var result = await new RetryInwardUnsavedLinesHandler(db, TestServices.Placement(db, allocator), TestServices.Uow(db))
            .Handle(new RetryInwardUnsavedLinesCommand(grn.Id), default);

        Assert.Equal(0, result.PlacedLines);
        Assert.Equal(1, result.StillMissingLines);
        Assert.Single(db.InwardUnsavedLines);
    }

    [Fact]
    public async Task Retry_NeedsAMfgMonth_ForLinesSavedBeforeItWasRecorded_ButAcceptsAFallback()
    {
        var (db, allocator) = await SeedAsync();
        var grn = await SubmitAsync(db, allocator);
        db.InwardUnsavedLines.Single().MfgMonth = null; // as saved by an older build
        db.Materials.Add(NewMaterial(40064374));
        await db.SaveChangesAsync();
        var handler = new RetryInwardUnsavedLinesHandler(db, TestServices.Placement(db, allocator), TestServices.Uow(db));

        var without = await handler.Handle(new RetryInwardUnsavedLinesCommand(grn.Id), default);
        Assert.Equal(1, without.NeedsPkmLines);
        Assert.Single(db.InwardUnsavedLines);

        var with = await handler.Handle(new RetryInwardUnsavedLinesCommand(grn.Id, 202609), default);
        Assert.Equal(1, with.PlacedLines);
        Assert.Equal(202609, db.InwardTransactionLines.Include(l => l.Material).Single(l => l.Material.MaterialNumber == 40064374).MfgMonth);
    }
}

file sealed class RetryUser : ICurrentUserService
{
    public string? UserId => "test-user";
    public string? UserName => "test@godrejwms.local";
    public bool IsInRole(string role) => true;
    public IReadOnlyList<string> Roles => ["Superadmin"];
}

file sealed class RetryClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
