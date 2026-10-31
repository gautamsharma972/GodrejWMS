using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Application.Features.Inward.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Covers the Pickers' "Inward without PKM" path: <see cref="SubmitInwardWithoutPkmCommand"/>
/// creates a GRN flagged <see cref="InwardTransaction.IsPkmless"/> with every line's MfgMonth left
/// null, allocates straight to empty pallet positions, and distinct GRN references across multiple
/// submissions (same reference-sequence mechanics as <see cref="SubmitInwardCommand"/>, reused via
/// <c>InwardSubmissionLogic</c>). <see cref="GetInwardHistoryWithoutPkmHandler"/> only ever surfaces
/// these still-pending, PKM-less GRNs - confirmed and PKM'd ones must never show up here.</summary>
public class SubmitInwardWithoutPkmHandlerTests
{
    private static (AppDbContext Db, Material Material) SeedWarehouse()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 2, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 10 });
        rack.PalletPositions.Add(new PalletPosition { Column = 1, Level = 2, LocationCode = "A-01-02", MaxPallets = 10 });
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40050000, Description = "No PKM Test Material", DesignType = "X",
            PackSize = 24, GrossWeightKg = 0.5m, LengthMm = 100, WidthMm = 100, HeightMm = 100,
            PalletCapacityBoxes = 1000, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();
        return (db, material);
    }

    [Fact]
    public async Task Handle_CreatesPkmlessGrn_WithNullMfgMonthOnLines()
    {
        var (db, material) = SeedWarehouse();
        var handler = new SubmitInwardWithoutPkmHandler(db, new PalletAllocationService(db, new TestClock()), new TestClock(), new TestCurrentUser());

        var result = await handler.Handle(
            new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(material.MaterialNumber, 10)], WarehouseId: 1),
            CancellationToken.None);

        Assert.Equal(AllocationStatus.Fulfilled, result.Lines.Single().Status);
        var transaction = db.InwardTransactions.Single(t => t.Id == result.Id);
        Assert.True(transaction.IsPkmless);
        Assert.Null(transaction.Lines.Single().MfgMonth);
    }

    [Fact]
    public async Task Handle_AcrossMultipleCalls_GeneratesDistinctReferences()
    {
        var (db, material) = SeedWarehouse();
        var handler = new SubmitInwardWithoutPkmHandler(db, new PalletAllocationService(db, new TestClock()), new TestClock(), new TestCurrentUser());

        var first = await handler.Handle(
            new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(material.MaterialNumber, 5)], WarehouseId: 1),
            CancellationToken.None);
        var second = await handler.Handle(
            new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(material.MaterialNumber, 5)], WarehouseId: 1),
            CancellationToken.None);

        Assert.NotEqual(first.ReferenceNumber, second.ReferenceNumber);
    }

    [Fact]
    public async Task HistoryQuery_OnlyReturnsPendingPkmlessGrns()
    {
        var (db, material) = SeedWarehouse();
        var submitHandler = new SubmitInwardWithoutPkmHandler(db, new PalletAllocationService(db, new TestClock()), new TestClock(), new TestCurrentUser());
        var pkmless = await submitHandler.Handle(
            new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(material.MaterialNumber, 5)], WarehouseId: 1),
            CancellationToken.None);

        // A normal, PKM'd GRN (the existing path) must never show up in the Pickers-only view.
        var pkmTransaction = new InwardTransaction { WarehouseId = 1, ReferenceNumber = "GRN-TEST-WITH-PKM", CreatedAt = DateTimeOffset.UtcNow, IsPkmless = false };
        db.InwardTransactions.Add(pkmTransaction);
        await db.SaveChangesAsync();

        var historyHandler = new GetInwardHistoryWithoutPkmHandler(db);
        var page = await historyHandler.Handle(new GetInwardHistoryWithoutPkmQuery(), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(pkmless.ReferenceNumber, item.ReferenceNumber);
        Assert.True(item.IsPkmless);
        Assert.False(item.IsConfirmed);
    }

    [Fact]
    public async Task HistoryQuery_ExcludesConfirmedPkmlessGrn()
    {
        var (db, material) = SeedWarehouse();
        var submitHandler = new SubmitInwardWithoutPkmHandler(db, new PalletAllocationService(db, new TestClock()), new TestClock(), new TestCurrentUser());
        var pkmless = await submitHandler.Handle(
            new SubmitInwardWithoutPkmCommand([new SubmitInwardWithoutPkmLine(material.MaterialNumber, 5)], WarehouseId: 1),
            CancellationToken.None);

        var transaction = db.InwardTransactions.Single(t => t.Id == pkmless.Id);
        foreach (var putaway in transaction.Lines.SelectMany(l => l.Putaways))
        {
            putaway.IsConfirmed = true;
        }
        await db.SaveChangesAsync();

        var historyHandler = new GetInwardHistoryWithoutPkmHandler(db);
        var page = await historyHandler.Handle(new GetInwardHistoryWithoutPkmQuery(), CancellationToken.None);

        Assert.Empty(page.Items);
    }
}

file sealed class TestCurrentUser : ICurrentUserService
{
    public string? UserId => "test-user";

    public string? UserName => "test@godrejwms.local";

    public bool IsInRole(string role) => true;

    public IReadOnlyList<string> Roles => ["Pickers"];
}

file sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow => new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
