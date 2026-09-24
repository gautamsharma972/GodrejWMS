using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class RejectPulloutTests
{
    private static async Task<(AppDbContext Db, SubmitPulloutHandler Submit, string Reference)> SeedPendingPulloutAsync()
    {
        var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = new Material { MaterialNumber = 123, Description = "Test", DesignType = "X", SeasonId = SeasonIds.Rainy };
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        db.StockBatches.Add(new StockBatch { MaterialId = material.Id, PalletPositionId = position.Id, MfgMonth = 202601, QuantityBoxes = 10 });
        await db.SaveChangesAsync();

        var submit = new SubmitPulloutHandler(db, new PulloutAllocationService(db), new TestClock(), new TestCurrentUser());
        var preview = await submit.Handle(new([new SubmitPulloutLine(123, 4)]), CancellationToken.None);
        return (db, submit, preview.ReferenceNumber);
    }

    private static RejectPulloutHandler Reject(AppDbContext db) => new(db, new TestClock(), new TestCurrentUser());

    [Fact]
    public async Task Reject_MarksPendingPulloutRejected_RecordsWhoAndWhy_AndLeavesInventoryUntouched()
    {
        var (db, _, reference) = await SeedPendingPulloutAsync();

        await Reject(db).Handle(new RejectPulloutCommand(reference, "  Raised in error  "), CancellationToken.None);

        var transaction = db.PulloutTransactions.Single();
        Assert.True(transaction.IsRejected);
        Assert.False(transaction.IsConfirmed);
        Assert.Equal("Raised in error", transaction.RejectionReason);
        Assert.Equal("test@godrejwms.local", transaction.RejectedByUserName);
        Assert.NotNull(transaction.RejectedAt);
        Assert.Equal(10, db.StockBatches.Single().QuantityBoxes);
    }

    [Fact]
    public async Task Reject_Throws_WhenPulloutAlreadyConfirmed()
    {
        var (db, submit, reference) = await SeedPendingPulloutAsync();
        var saved = GetSavedPreview(db, reference);
        await submit.Handle(new([new SubmitPulloutLine(123, 4)], saved), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Reject(db).Handle(new RejectPulloutCommand(reference, "Too late"), CancellationToken.None));

        Assert.Contains("already been confirmed", exception.Message);
        Assert.False(db.PulloutTransactions.Single().IsRejected);
    }

    [Fact]
    public async Task Reject_Throws_WhenPulloutAlreadyRejected()
    {
        var (db, _, reference) = await SeedPendingPulloutAsync();
        await Reject(db).Handle(new RejectPulloutCommand(reference, "First"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Reject(db).Handle(new RejectPulloutCommand(reference, "Second"), CancellationToken.None));

        Assert.Contains("already been rejected", exception.Message);
    }

    [Fact]
    public async Task RefreshAndConfirm_AreBlocked_AfterRejection()
    {
        var (db, submit, reference) = await SeedPendingPulloutAsync();
        var saved = GetSavedPreview(db, reference);
        await Reject(db).Handle(new RejectPulloutCommand(reference, "Cancelled"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            submit.Handle(new([new SubmitPulloutLine(123, 4)], RefreshReference: reference), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            submit.Handle(new([new SubmitPulloutLine(123, 4)], saved), CancellationToken.None));

        Assert.Equal(10, db.StockBatches.Single().QuantityBoxes);
        Assert.False(db.PulloutTransactions.Single().IsConfirmed);
    }

    [Fact]
    public async Task History_ShowsRejected_AndNotConfirmedFilterExcludesIt()
    {
        var (db, _, reference) = await SeedPendingPulloutAsync();
        await Reject(db).Handle(new RejectPulloutCommand(reference, "Cancelled"), CancellationToken.None);
        db.ChangeTracker.Clear();
        var history = new GetPulloutHistoryHandler(db);

        var all = await history.Handle(new(), CancellationToken.None);
        var pending = await history.Handle(new(IsConfirmed: false), CancellationToken.None);
        var rejected = await history.Handle(new(IsRejected: true), CancellationToken.None);

        Assert.True(Assert.Single(all.Items).IsRejected);
        Assert.Empty(pending.Items);
        Assert.Single(rejected.Items);

        var detail = await new GetPulloutDetailHandler(db).Handle(new(all.Items[0].Id), CancellationToken.None);
        Assert.True(detail.IsRejected);
        Assert.Equal("Cancelled", detail.RejectionReason);
        Assert.Equal("test@godrejwms.local", detail.RejectedByUserName);
    }

    private static GodrejWMS.Application.Features.Pullout.Dtos.PulloutResultDto GetSavedPreview(AppDbContext db, string reference)
    {
        var json = db.PulloutTransactions.Single(t => t.ReferenceNumber == reference).PreviewJson!;
        return System.Text.Json.JsonSerializer.Deserialize<GodrejWMS.Application.Features.Pullout.Dtos.PulloutResultDto>(json)!;
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
