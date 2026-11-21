using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.CycleCount;
using GodrejWMS.Application.Features.CycleCount.Commands;
using GodrejWMS.Application.Features.CycleCount.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Counting cards, corrections and the submit that reconciles stock through the ledger.</summary>
public class CycleCountCountingTests
{
    private sealed class Access : IWarehouseAccessService
    {
        public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseOption>>([new WarehouseOption(1, "GDR1", "Godrej 1")]);
        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(warehouseId == 1);
    }

    private sealed class Notes : IRealtimeNotificationService
    {
        public IDisposable Subscribe(string? userId, IReadOnlyCollection<string> roles, Func<RealtimeNotification, Task> handler) => new Noop();
        public Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private sealed record Ctx(AppDbContext Db, Material M1, Material M2, Material Inactive, int BatchId, List<PalletPosition> Pos);

    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

    private static async Task<Ctx> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 3, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 3; c++) rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        db.Racks.Add(rack);
        Material Make(long n, bool active = true) => new()
        {
            MaterialNumber = n, Description = "M" + n, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy, IsActive = active
        };
        var m1 = Make(40000001);
        var m2 = Make(40000002);
        var off = Make(40000003, false);
        db.Materials.AddRange(m1, m2, off);
        await db.SaveChangesAsync();
        var pos = rack.PalletPositions.OrderBy(p => p.Column).ToList();
        db.StockBatches.Add(new StockBatch { MaterialId = m1.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 10 });
        db.StockBatches.Add(new StockBatch { MaterialId = m1.Id, PalletPositionId = pos[1].Id, MfgMonth = 202610, QuantityBoxes = 20 });
        db.StockBatches.Add(new StockBatch { MaterialId = m2.Id, PalletPositionId = pos[1].Id, MfgMonth = 202609, QuantityBoxes = 5 });
        await db.SaveChangesAsync();
        var batch = await new ScheduleCycleCountHandler(db, new Access(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new ScheduleCycleCountCommand(1, Start, End, ["A"], null, true), default);
        await Start_(db, batch.Id);
        return new Ctx(db, m1, m2, off, batch.Id, pos);
    }

    private static Task<CycleCountBatchDto> Start_(AppDbContext db, int batchId) =>
        new StartCycleCountHandler(db, new Access(), TestServices.Uow(db), new SvcClock(), new SvcUser(), new Notes()).Handle(new StartCycleCountCommand(batchId), default);

    private static Task<CycleCountCardsDto> Cards(AppDbContext db, int batchId, string? from = null, string? to = null) =>
        new GetCycleCountCardsHandler(db, new Access()).Handle(new GetCycleCountCardsQuery(batchId, "A", from, to), default);

    private static Task<CycleCountCardDto> Verify(AppDbContext db, int cardId) =>
        new VerifyCycleCountCardHandler(db, new Access(), new SvcClock(), new SvcUser()).Handle(new VerifyCycleCountCardCommand(cardId), default);

    private static Task<CycleCountCardDto> Correct(AppDbContext db, int cardId, long? material, string? pkm, decimal qty) =>
        new CorrectCycleCountCardHandler(db, new Access(), new SvcClock(), new SvcUser()).Handle(new CorrectCycleCountCardCommand(cardId, material, pkm, qty), default);

    private static Task<CycleCountSubmitResultDto> Submit(AppDbContext db, int batchId) =>
        new SubmitCycleCountRackHandler(db, new Access(), TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new SubmitCycleCountRackCommand(batchId, "A"), default);

    private static async Task VerifyAllAsync(AppDbContext db, int batchId)
    {
        foreach (var card in (await Cards(db, batchId)).Cards.Where(c => !c.IsCompleted)) await Verify(db, card.Id);
    }

    private static decimal Stock(AppDbContext db, int materialId, int positionId, int pkm) =>
        db.StockBatches.AsNoTracking().Where(b => b.MaterialId == materialId && b.PalletPositionId == positionId && b.MfgMonth == pkm).Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task Start_SnapshotsEveryLocation_IncludingEmptyOnes_Once_AndTheRackOrderIsStable()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        var again = await Start_(s.Db, s.BatchId); // a second counter opening the batch creates nothing new
        Assert.Equal(CycleCountBatchStatus.InProgress, again.Status);
        var cards = await Cards(s.Db, s.BatchId);
        Assert.Equal(4, cards.TotalCards);                                  // A-01 m1, A-02 m1 + m2, A-03 empty
        Assert.Equal(["A-01-01", "A-02-01", "A-02-01", "A-03-01"], cards.Cards.Select(c => c.LocationCode));
        Assert.Contains(cards.Cards, c => c.LocationCode == "A-03-01" && c.SystemMaterialNumber is null && c.SystemQuantityBoxes == 0);
        Assert.Equal(0, cards.CompletionPercent);
    }

    [Fact]
    public async Task OkMovesTheCardToTheBottom_KeepsBothGroupsInLocationOrder_AndSurvivesAReload()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var first = (await Cards(s.Db, s.BatchId)).Cards;

        await Verify(s.Db, first[0].Id); // A-01-01
        await Verify(s.Db, first[2].Id); // the second A-02-01 card

        var after = await Cards(s.Db, s.BatchId);
        Assert.Equal([false, false, true, true], after.Cards.Select(c => c.IsCompleted));
        Assert.Equal(["A-02-01", "A-03-01", "A-01-01", "A-02-01"], after.Cards.Select(c => c.LocationCode));
        Assert.Equal(50m, after.CompletionPercent);
        Assert.Equal(after.Cards.Select(c => c.Id), (await Cards(s.Db, s.BatchId)).Cards.Select(c => c.Id)); // same order on reopen
        Assert.All(after.Cards.Where(c => c.IsCompleted), c => Assert.Equal("svc@godrejwms.local", c.CountedByUserName));
    }

    [Fact]
    public async Task Range_IsInclusive_InRackSequence_AndRefusesAnInvertedOrForeignRange()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        var range = await Cards(s.Db, s.BatchId, "A-02-01", "A-03-01");
        Assert.Equal(["A-02-01", "A-02-01", "A-03-01"], range.Cards.Select(c => c.LocationCode));
        Assert.Equal(["A-01-01"], (await Cards(s.Db, s.BatchId, "A-01-01", "A-01-01")).Cards.Select(c => c.LocationCode));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cards(s.Db, s.BatchId, "A-03-01", "A-01-01"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cards(s.Db, s.BatchId, "A-01-01", "B-09-09"));
        var locations = await new GetCycleCountLocationsHandler(s.Db, new Access()).Handle(new GetCycleCountLocationsQuery(s.BatchId, "A"), default);
        Assert.Equal(["A-01-01", "A-02-01", "A-03-01"], locations.Select(l => l.LocationCode));
    }

    [Fact]
    public async Task Correct_ValidatesMaterialPkmAndQuantity_AndKeepsInventoryUntouchedUntilSubmit()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var card = (await Cards(s.Db, s.BatchId)).Cards[0];

        Assert.False(new CorrectCycleCountCardValidator().Validate(new CorrectCycleCountCardCommand(card.Id, null, null, -1)).IsValid);
        Assert.False(new CorrectCycleCountCardValidator().Validate(new CorrectCycleCountCardCommand(card.Id, null, null, 1.5m)).IsValid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Correct(s.Db, card.Id, 99999999, null, 5));            // unknown material
        await Assert.ThrowsAsync<InvalidOperationException>(() => Correct(s.Db, card.Id, s.Inactive.MaterialNumber, "SEP|2026", 5)); // inactive
        await Assert.ThrowsAsync<InvalidOperationException>(() => Correct(s.Db, card.Id, null, "not a month", 5));

        var saved = await Correct(s.Db, card.Id, null, null, 8);
        Assert.Equal(CycleCountCardState.Corrected, saved.State);
        Assert.Equal(-2, saved.DifferenceBoxes);
        Assert.Equal(10, Stock(s.Db, s.M1.Id, s.Pos[0].Id, 202609)); // still the system figure

        var same = await Correct(s.Db, card.Id, null, null, 10);     // typing the system figure is the same as OK
        Assert.Equal(CycleCountCardState.Verified, same.State);
        Assert.Contains(s.Db.CycleCountHistory, h => h.Action == CycleCountActions.Reopened && h.CycleCountCardId == card.Id);
    }

    [Fact]
    public async Task Submit_PostsOnlyTheDifferences_ThroughTheLedger_AndLinksThemToTheBatch()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var cards = (await Cards(s.Db, s.BatchId)).Cards;
        var a1 = cards.Single(c => c.LocationCode == "A-01-01");
        var a2m1 = cards.Single(c => c.LocationCode == "A-02-01" && c.SystemMaterialNumber == 40000001);
        var a2m2 = cards.Single(c => c.LocationCode == "A-02-01" && c.SystemMaterialNumber == 40000002);
        var a3 = cards.Single(c => c.LocationCode == "A-03-01");

        await Verify(s.Db, a1.Id);                         // matches: no entry
        await Correct(s.Db, a2m1.Id, null, null, 17);      // 20 -> 17
        await Correct(s.Db, a2m2.Id, null, null, 0);       // zero: the row stays, quantity 0
        await Correct(s.Db, a3.Id, 40000002, "NOV|2026", 12); // empty location: stock found
        var batchNumber = (await Cards(s.Db, s.BatchId)).BatchNumber;
        var ledgerBefore = s.Db.StockLedgerEntries.Count();

        var result = await Submit(s.Db, s.BatchId);

        Assert.False(result.AlreadySubmitted);
        Assert.Equal(3, result.StockChanges);
        Assert.Equal(17, Stock(s.Db, s.M1.Id, s.Pos[1].Id, 202610));
        Assert.Equal(0, Stock(s.Db, s.M2.Id, s.Pos[1].Id, 202609));
        Assert.True(s.Db.StockBatches.Any(b => b.MaterialId == s.M2.Id && b.PalletPositionId == s.Pos[1].Id && b.MfgMonth == 202609)); // row kept
        Assert.Equal(12, Stock(s.Db, s.M2.Id, s.Pos[2].Id, 202611));
        Assert.Equal(10, Stock(s.Db, s.M1.Id, s.Pos[0].Id, 202609));
        var entries = s.Db.StockLedgerEntries.AsNoTracking().Where(e => e.EntryType == StockLedgerEntryTypes.CycleCount).ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal(ledgerBefore + 3, s.Db.StockLedgerEntries.Count());
        Assert.All(entries, e => Assert.Equal(batchNumber, e.ReferenceNumber));
        Assert.Contains(entries, e => e.QuantityBoxes == -3 && e.BalanceAfterBoxes == 17);
        Assert.Contains(entries, e => e.QuantityBoxes == -5 && e.BalanceAfterBoxes == 0);
        Assert.Contains(entries, e => e.QuantityBoxes == 12 && e.BalanceAfterBoxes == 12);

        var batch = s.Db.CycleCountBatches.Include(b => b.Racks).Single();
        Assert.Equal(CycleCountBatchStatus.PendingReview, batch.Status);
        Assert.Equal(CycleCountRackStatus.Submitted, batch.Racks.Single().Status);
        Assert.Equal(CycleCountReviewStatus.PendingReview, s.Db.CycleCountReviews.Single().Status);
        Assert.All(s.Db.CycleCountCards, c => Assert.NotNull(c.SubmittedAt));
        Assert.Contains(s.Db.CycleCountHistory, h => h.Action == CycleCountActions.Submitted);
    }

    [Fact]
    public async Task MaterialOrPkmChange_MovesTheStockBetweenCells_AndFoundStockAddsACard()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var cards = (await Cards(s.Db, s.BatchId)).Cards;
        var a1 = cards.Single(c => c.LocationCode == "A-01-01");

        await Correct(s.Db, a1.Id, 40000002, "OCT|2026", 10);   // the 10 boxes are really material 2, another PKM
        var found = await new AddCycleCountFoundStockHandler(s.Db, new Access(), new SvcClock(), new SvcUser())
            .Handle(new AddCycleCountFoundStockCommand(s.BatchId, s.Pos[0].Id, 40000001, "SEP|2026", 4), default);
        Assert.True(found.IsExtra);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AddCycleCountFoundStockHandler(s.Db, new Access(), new SvcClock(), new SvcUser())
            .Handle(new AddCycleCountFoundStockCommand(s.BatchId, s.Pos[0].Id, 40000002, "OCT|2026", 1), default)); // would count the same stock twice
        await VerifyAllAsync(s.Db, s.BatchId);
        await Submit(s.Db, s.BatchId);

        Assert.Equal(4, Stock(s.Db, s.M1.Id, s.Pos[0].Id, 202609));   // original cell now holds only what was found
        Assert.Equal(10, Stock(s.Db, s.M2.Id, s.Pos[0].Id, 202610));
        var ledger = s.Db.StockLedgerEntries.Where(e => e.EntryType == StockLedgerEntryTypes.CycleCount && e.PalletPositionId == s.Pos[0].Id).ToList();
        Assert.Equal(0, ledger.Sum(e => e.QuantityBoxes) - 10 - (4 - 10)); // -6 (m1) + 10 (m2)
    }

    [Fact]
    public async Task Submit_IsRefusedWhilePending_AndASecondSubmitPostsNothingMore()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var cards = (await Cards(s.Db, s.BatchId)).Cards;
        await Verify(s.Db, cards[0].Id);
        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(s.Db, s.BatchId));
        Assert.Contains("pending", blocked.Message);
        var summary = await new GetCycleCountSubmitSummaryHandler(s.Db, new Access()).Handle(new GetCycleCountSubmitSummaryQuery(s.BatchId, "A"), default);
        Assert.False(summary.CanSubmit);
        Assert.Equal(3, summary.PendingCards);

        var two = cards.Single(c => c.LocationCode == "A-02-01" && c.SystemMaterialNumber == 40000001);
        await VerifyAllAsync(s.Db, s.BatchId);
        await Correct(s.Db, two.Id, null, null, 15);
        await Submit(s.Db, s.BatchId);
        var ledger = s.Db.StockLedgerEntries.Count(e => e.EntryType == StockLedgerEntryTypes.CycleCount);

        var again = await Submit(s.Db, s.BatchId);
        Assert.True(again.AlreadySubmitted);
        Assert.Equal(ledger, s.Db.StockLedgerEntries.Count(e => e.EntryType == StockLedgerEntryTypes.CycleCount));
        Assert.Equal(1, s.Db.CycleCountReviews.Count());
        Assert.Equal(15, Stock(s.Db, s.M1.Id, s.Pos[1].Id, 202610));
    }

    [Fact]
    public async Task SubmittedRecords_CannotBeEdited()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var card = (await Cards(s.Db, s.BatchId)).Cards[0];
        await VerifyAllAsync(s.Db, s.BatchId);
        await Submit(s.Db, s.BatchId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Verify(s.Db, card.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Correct(s.Db, card.Id, null, null, 3));
    }

    [Fact]
    public async Task StockThatMovedAfterTheSnapshot_IsOverwrittenByTheCount_AndTheDriftIsRecorded()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var card = (await Cards(s.Db, s.BatchId)).Cards.Single(c => c.LocationCode == "A-01-01");
        await VerifyAllAsync(s.Db, s.BatchId);
        // someone picked 4 boxes after the snapshot
        var batch = s.Db.StockBatches.Single(b => b.PalletPositionId == s.Pos[0].Id);
        batch.QuantityBoxes = 6;
        await s.Db.SaveChangesAsync();

        await Submit(s.Db, s.BatchId);

        Assert.Equal(10, Stock(s.Db, s.M1.Id, s.Pos[0].Id, 202609));
        Assert.Contains(s.Db.CycleCountHistory, h => h.Action == CycleCountActions.SnapshotDrift && h.CycleCountCardId == card.Id);
    }

    [Fact]
    public async Task PartialClose_LetsTheCompletedPartBeSubmitted_AndLeavesTheRestUntouched()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var cards = (await Cards(s.Db, s.BatchId)).Cards;
        await Correct(s.Db, cards.Single(c => c.LocationCode == "A-01-01").Id, null, null, 7);
        await new PartiallyCloseCycleCountHandler(s.Db, new Access(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new PartiallyCloseCycleCountCommand(s.BatchId, "out of time"), default);

        var left = await Cards(s.Db, s.BatchId);
        Assert.Equal(1, left.TotalCards);                    // excluded cards are not part of the work
        await Submit(s.Db, s.BatchId);

        Assert.Equal(7, Stock(s.Db, s.M1.Id, s.Pos[0].Id, 202609));
        Assert.Equal(20, Stock(s.Db, s.M1.Id, s.Pos[1].Id, 202610));  // excluded: untouched
        Assert.Equal(5, Stock(s.Db, s.M2.Id, s.Pos[1].Id, 202609));
        var closed = await new CloseCycleCountHandler(s.Db, new Access(), new SvcClock(), new SvcUser(), new Notes()).Handle(new CloseCycleCountCommand(s.BatchId), default);
        Assert.Equal(CycleCountBatchStatus.Closed, closed.Status);
    }

    [Fact]
    public async Task LedgerStillMatchesInventory_AfterACycleCount()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        // give the opening stock ledger rows so ledger and inventory start equal
        foreach (var b in s.Db.StockBatches.AsNoTracking().ToList())
        {
            s.Db.StockLedgerEntries.Add(new StockLedgerEntry
            {
                PostingNumber = "OPEN", EntryType = StockLedgerEntryTypes.StockImport, WarehouseId = 1, MaterialId = b.MaterialId, MfgMonth = b.MfgMonth,
                StockSubtypeId = b.StockSubtypeId, PalletPositionId = b.PalletPositionId, QuantityBoxes = b.QuantityBoxes, BalanceAfterBoxes = b.QuantityBoxes
            });
        }

        await s.Db.SaveChangesAsync();
        var cards = (await Cards(s.Db, s.BatchId)).Cards;
        await Correct(s.Db, cards.Single(c => c.LocationCode == "A-01-01").Id, null, null, 3);
        await Correct(s.Db, cards.Single(c => c.LocationCode == "A-03-01").Id, 40000001, "SEP|2026", 9);
        await VerifyAllAsync(s.Db, s.BatchId);
        await Submit(s.Db, s.BatchId);

        foreach (var b in s.Db.StockBatches.AsNoTracking().ToList())
        {
            var ledger = s.Db.StockLedgerEntries.Where(e => e.MaterialId == b.MaterialId && e.MfgMonth == b.MfgMonth && e.PalletPositionId == b.PalletPositionId && e.StockSubtypeId == b.StockSubtypeId).Sum(e => e.QuantityBoxes);
            Assert.Equal(b.QuantityBoxes, ledger);
        }
    }

    private static async Task<Ctx> SubmittedAsync()
    {
        var s = await SeedAsync();
        var cards = (await Cards(s.Db, s.BatchId)).Cards;
        await Correct(s.Db, cards.Single(c => c.LocationCode == "A-01-01").Id, null, null, 8);                    // discrepancy
        await Correct(s.Db, cards.Single(c => c.LocationCode == "A-03-01").Id, 40000002, "NOV|2026", 12);         // new stock in an empty location
        await Correct(s.Db, cards.Single(c => c.LocationCode == "A-02-01" && c.SystemMaterialNumber == 40000002).Id, 40000001, "SEP|2026", 5); // material changed
        await VerifyAllAsync(s.Db, s.BatchId);
        await Submit(s.Db, s.BatchId);
        return s;
    }

    [Fact]
    public async Task Review_ShowsSystemAgainstPhysical_ClassifiesRows_AndMarkingReviewedNeverChangesStock()
    {
        var s = await SubmittedAsync();
        using var _ = s.Db;
        var stockBefore = s.Db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes);

        var list = await new GetCycleCountReviewsHandler(s.Db, new Access()).Handle(new GetCycleCountReviewsQuery(), default);
        var item = Assert.Single(list.Items);
        Assert.Equal("A", item.RackCode);
        Assert.Equal(4, item.TotalCards);
        Assert.Equal(3, item.CorrectedCards);
        Assert.Equal(1, item.VerifiedCards);
        Assert.Equal(CycleCountReviewStatus.PendingReview, item.ReviewStatus);

        var detail = await new GetCycleCountReviewDetailHandler(s.Db, new Access()).Handle(new GetCycleCountReviewDetailQuery(s.BatchId, "A"), default);
        Assert.Equal(CycleCountRowKind.Discrepancy, detail.Rows.Single(r => r.LocationCode == "A-01-01").Kind);
        Assert.Equal(-2, detail.Rows.Single(r => r.LocationCode == "A-01-01").DifferenceBoxes);
        Assert.Equal(CycleCountRowKind.Exceptional, detail.Rows.Single(r => r.LocationCode == "A-03-01").Kind);          // found in an empty location
        Assert.Equal(CycleCountRowKind.Exceptional, detail.Rows.Single(r => r.SystemQuantityBoxes == 5).Kind);           // material changed
        Assert.Equal(CycleCountRowKind.Matched, detail.Rows.Single(r => r.SystemQuantityBoxes == 20).Kind);
        // a count of zero is a quantity discrepancy, not a changed material
        Assert.Equal(CycleCountRowKind.Discrepancy, (await new GetCycleCountReviewDetailHandler(s.Db, new Access()).Handle(new GetCycleCountReviewDetailQuery(s.BatchId, "A", Location: "A-01"), default)).Rows.Single().Kind);
        Assert.NotEmpty(detail.History);
        var onlyDiff = await new GetCycleCountReviewDetailHandler(s.Db, new Access()).Handle(new GetCycleCountReviewDetailQuery(s.BatchId, "A", CountStatus: "discrepancy"), default);
        Assert.Equal(3, onlyDiff.Rows.Count);
        var byLocation = await new GetCycleCountReviewDetailHandler(s.Db, new Access()).Handle(new GetCycleCountReviewDetailQuery(s.BatchId, "A", Location: "A-02"), default);
        Assert.All(byLocation.Rows, r => Assert.Equal("A-02-01", r.LocationCode));

        var mark = new MarkCycleCountReviewedHandler(s.Db, new Access(), new SvcClock(), new SvcUser(), new Notes());
        Assert.True(await mark.Handle(new MarkCycleCountReviewedCommand(s.BatchId, "A", "looks right"), default));
        Assert.False(await mark.Handle(new MarkCycleCountReviewedCommand(s.BatchId, "A", "again"), default)); // already reviewed
        var review = s.Db.CycleCountReviews.Single();
        Assert.Equal(CycleCountReviewStatus.Reviewed, review.Status);
        Assert.Equal("looks right", review.ReviewRemarks);
        Assert.Equal(stockBefore, s.Db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));
    }

    [Fact]
    public async Task Review_IsRefusedBeforeSubmission()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MarkCycleCountReviewedHandler(s.Db, new Access(), new SvcClock(), new SvcUser(), new Notes()).Handle(new MarkCycleCountReviewedCommand(s.BatchId, "A"), default));
    }

    [Fact]
    public async Task Reports_ShowBatchProgress_LocationRows_OnlyDiscrepancies_AndPerUserFigures()
    {
        var s = await SubmittedAsync();
        using var _ = s.Db;
        var all = new CycleCountReportFilter();

        var summary = Assert.Single(await new GetCycleCountBatchSummaryReportHandler(s.Db).Handle(new GetCycleCountBatchSummaryReportQuery(all), default));
        Assert.Equal(4, summary.TotalCards);
        Assert.Equal(4, summary.CompletedCards);
        Assert.Equal(0, summary.PendingCards);
        Assert.Equal(3, summary.CorrectedCards);
        Assert.Equal(100m, summary.CompletionPercent);
        Assert.Equal(CycleCountBatchStatus.PendingReview, summary.Status);
        Assert.Equal("svc@godrejwms.local", summary.SubmittedBy);

        var location = await new GetCycleCountLocationReportHandler(s.Db).Handle(new GetCycleCountLocationReportQuery(all), default);
        Assert.Equal(4, location.Count);
        var discrepancy = await new GetCycleCountLocationReportHandler(s.Db).Handle(new GetCycleCountLocationReportQuery(all, OnlyDiscrepancies: true), default);
        Assert.Equal(3, discrepancy.Count);
        Assert.DoesNotContain(discrepancy, r => r.Kind == CycleCountRowKind.Matched);
        var byMaterial = await new GetCycleCountLocationReportHandler(s.Db).Handle(new GetCycleCountLocationReportQuery(all with { MaterialNumber = 40000002 }), default);
        Assert.All(byMaterial, r => Assert.True(r.MaterialNumber == 40000002 || r.CountedAs.StartsWith("40000002")));
        Assert.Empty(await new GetCycleCountLocationReportHandler(s.Db).Handle(new GetCycleCountLocationReportQuery(all with { BatchNumber = "CC-NOPE" }), default));

        var users = await new GetCycleCountUserReportHandler(s.Db).Handle(new GetCycleCountUserReportQuery(all), default);
        var row = Assert.Single(users);
        Assert.Equal(4, row.AssignedCards);
        Assert.Equal(4, row.CompletedCards);
        Assert.Equal(3, row.CorrectedCards);
        Assert.Equal(100m, row.CompletionPercent);
    }

    private static async Task<Ctx> SeedWithDamageAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 3, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 3; c++) rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        db.Racks.Add(rack);
        Material Make(long n) => new()
        {
            MaterialNumber = n, Description = "M" + n, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy
        };
        var m1 = Make(40000001);
        var m2 = Make(40000002);
        db.Materials.AddRange(m1, m2);
        await db.SaveChangesAsync();
        var pos = rack.PalletPositions.OrderBy(p => p.Column).ToList();
        db.StockBatches.Add(new StockBatch { MaterialId = m1.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, QuantityBoxes = 10 });
        db.StockBatches.Add(new StockBatch { MaterialId = m1.Id, PalletPositionId = pos[0].Id, MfgMonth = 202609, StockSubtypeId = LocationSubtypeIds.Damage, QuantityBoxes = 3 });
        await db.SaveChangesAsync();
        var batch = await new ScheduleCycleCountHandler(db, new Access(), new SvcClock(), new SvcUser(), new Notes())
            .Handle(new ScheduleCycleCountCommand(1, Start, End, ["A"], null, true), default);
        await Start_(db, batch.Id);
        return new Ctx(db, m1, m2, m2, batch.Id, pos);
    }

    [Fact]
    public async Task DamagedStock_IsOnOfferFromTheStart_ChosenViaTheStockTypeFilter_AndPostedToTheDamageSubtype()
    {
        var s = await SeedWithDamageAsync();
        using var _ = s.Db;
        var both = (await Cards(s.Db, s.BatchId)).Cards;                          // default: Good and Damage together
        Assert.Contains(both, c => c.StockType == "Damage" && c.SystemQuantityBoxes == 3);
        Assert.Contains(both, c => c.StockType == "Good" && c.SystemQuantityBoxes == 10);
        Assert.Single((await new GetCycleCountCardsHandler(s.Db, new Access()).Handle(new GetCycleCountCardsQuery(s.BatchId, "A", StockSubtypeId: 2), default)).Cards);
        Assert.DoesNotContain((await new GetCycleCountCardsHandler(s.Db, new Access()).Handle(new GetCycleCountCardsQuery(s.BatchId, "A", StockSubtypeId: 1), default)).Cards, c => c.StockType == "Damage");

        // the damaged boxes are really 2; 4 more damaged boxes are found as new stock in an empty location; everything else is OK
        await Correct(s.Db, both.Single(c => c.StockType == "Damage").Id, null, null, 2);
        await new AddCycleCountFoundStockHandler(s.Db, new Access(), new SvcClock(), new SvcUser())
            .Handle(new AddCycleCountFoundStockCommand(s.BatchId, s.Pos[2].Id, 40000002, "SEP|2026", 4, LocationSubtypeIds.Damage), default);
        await VerifyAllAsync(s.Db, s.BatchId);
        await Submit(s.Db, s.BatchId);

        decimal Qty(int material, int position, int pkm, int subtype) => s.Db.StockBatches.AsNoTracking()
            .Where(b => b.MaterialId == material && b.PalletPositionId == position && b.MfgMonth == pkm && b.StockSubtypeId == subtype).Sum(b => (decimal?)b.QuantityBoxes) ?? 0;
        Assert.Equal(2, Qty(s.M1.Id, s.Pos[0].Id, 202609, LocationSubtypeIds.Damage));
        Assert.Equal(10, Qty(s.M1.Id, s.Pos[0].Id, 202609, LocationSubtypeIds.Good));     // good stock untouched
        Assert.Equal(4, Qty(s.M2.Id, s.Pos[2].Id, 202609, LocationSubtypeIds.Damage));
    }

    [Fact]
    public async Task UncountedDamageCards_DoNotBlockTheSubmit_AndTheDamagedStockStaysAsItWas()
    {
        var s = await SeedWithDamageAsync();
        using var _ = s.Db;
        var good = (await new GetCycleCountCardsHandler(s.Db, new Access()).Handle(new GetCycleCountCardsQuery(s.BatchId, "A", StockSubtypeId: 1), default)).Cards;
        foreach (var card in good.Where(c => !c.IsCompleted)) await Verify(s.Db, card.Id);     // only the Good stock is counted

        var summary = await new GetCycleCountSubmitSummaryHandler(s.Db, new Access()).Handle(new GetCycleCountSubmitSummaryQuery(s.BatchId, "A"), default);
        Assert.True(summary.CanSubmit);
        Assert.Equal(1, summary.UncountedDamageCards);
        Assert.Equal(0, summary.PendingCards);
        await Submit(s.Db, s.BatchId);

        Assert.Equal(3, s.Db.StockBatches.AsNoTracking().Single(b => b.StockSubtypeId == LocationSubtypeIds.Damage).QuantityBoxes);
        Assert.True(s.Db.CycleCountCards.Single(c => c.SystemStockSubtypeId == LocationSubtypeIds.Damage).IsExcluded);
        Assert.Contains(s.Db.CycleCountHistory, h => h.Action == CycleCountActions.Submitted && h.NewValue!.Contains("damage card(s) were not counted"));
    }

    [Fact]
    public async Task AGoodCardStillPending_BlocksTheSubmit_EvenWithDamageCardsPresent()
    {
        var s = await SeedWithDamageAsync();
        using var _ = s.Db;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(s.Db, s.BatchId));
        Assert.Contains("pending", ex.Message);
    }

    [Fact]
    public async Task ReopeningABatchStartedEarlier_AddsTheDamageCardsItLacked()
    {
        var s = await SeedWithDamageAsync();
        using var _ = s.Db;
        // simulate a batch started before damage cards were offered by default
        s.Db.CycleCountCards.RemoveRange(s.Db.CycleCountCards.Where(c => c.SystemStockSubtypeId == LocationSubtypeIds.Damage));
        s.Db.CycleCountBatchRacks.Single().IncludesDamageStock = false;
        await s.Db.SaveChangesAsync();
        Assert.DoesNotContain((await Cards(s.Db, s.BatchId)).Cards, c => c.StockType == "Damage");

        await Start_(s.Db, s.BatchId);                                   // the counter opens the batch again

        Assert.Single((await Cards(s.Db, s.BatchId)).Cards, c => c.StockType == "Damage");
        var again = await Start_(s.Db, s.BatchId);                       // and doing it twice adds nothing more
        Assert.Single((await Cards(s.Db, s.BatchId)).Cards, c => c.StockType == "Damage");
        Assert.Equal(CycleCountBatchStatus.InProgress, again.Status);
    }

    [Fact]
    public async Task GoodStockOnly_LeavesDamageStockUntouched()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        s.Db.StockBatches.Add(new StockBatch { MaterialId = s.M1.Id, PalletPositionId = s.Pos[0].Id, MfgMonth = 202609, StockSubtypeId = LocationSubtypeIds.Damage, QuantityBoxes = 3 });
        await s.Db.SaveChangesAsync();
        var a1 = (await Cards(s.Db, s.BatchId)).Cards.Single(c => c.LocationCode == "A-01-01");
        await Correct(s.Db, a1.Id, null, null, 6);
        await VerifyAllAsync(s.Db, s.BatchId);
        await Submit(s.Db, s.BatchId);

        Assert.Equal(6, s.Db.StockBatches.AsNoTracking().Single(b => b.PalletPositionId == s.Pos[0].Id && b.StockSubtypeId == LocationSubtypeIds.Good).QuantityBoxes);
        Assert.Equal(3, s.Db.StockBatches.AsNoTracking().Single(b => b.PalletPositionId == s.Pos[0].Id && b.StockSubtypeId == LocationSubtypeIds.Damage).QuantityBoxes);
    }

    [Fact]
    public async Task LastRackReviewed_ClosesTheBatchByItself_AndLiftsTheFreeze_ButNotBeforeEveryRackIsDone()
    {
        var s = await SubmittedAsync();                                   // rack A submitted, review pending
        using var _ = s.Db;
        var batch = s.Db.CycleCountBatches.Include(b => b.Racks).Single();
        var other = new CycleCountBatchRack { CycleCountBatchId = batch.Id, RackCode = "B", Status = CycleCountRackStatus.Open, RowVersion = 1 };
        s.Db.CycleCountBatchRacks.Add(other);                             // a second rack still to be counted
        await s.Db.SaveChangesAsync();
        var mark = new MarkCycleCountReviewedHandler(s.Db, new Access(), new SvcClock(), new SvcUser(), new Notes());

        await mark.Handle(new MarkCycleCountReviewedCommand(s.BatchId, "A"), default);
        Assert.Equal(CycleCountBatchStatus.PendingReview, s.Db.CycleCountBatches.Single().Status);       // rack B is still open: not closed
        Assert.NotNull(await CycleCountFreeze.FindFreezingBatchAsync(s.Db, 1, TestServices.Now, default));

        other.Status = CycleCountRackStatus.Excluded;                      // nothing left to count
        s.Db.CycleCountReviews.Add(new CycleCountReview { CycleCountBatchId = batch.Id, RackCode = "B", Status = CycleCountReviewStatus.PendingReview });
        await s.Db.SaveChangesAsync();
        await mark.Handle(new MarkCycleCountReviewedCommand(s.BatchId, "B"), default);                      // last pending review

        var closed = s.Db.CycleCountBatches.Single();
        Assert.Equal(CycleCountBatchStatus.Closed, closed.Status);
        Assert.NotNull(closed.ActualEndAt);
        Assert.Contains(s.Db.CycleCountHistory, h => h.Action == CycleCountActions.AutoClosed);
        Assert.Null(await CycleCountFreeze.FindFreezingBatchAsync(s.Db, 1, TestServices.Now, default));    // Inward and Pullout are allowed again
    }

    [Fact]
    public async Task EndedBatches_CloseAutomatically_OnlyWhenTheyAskedForIt_WithoutApplyingUnsubmittedCounts()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var cards = (await Cards(s.Db, s.BatchId)).Cards;
        await Correct(s.Db, cards.Single(c => c.LocationCode == "A-01-01").Id, null, null, 3);             // counted, never submitted
        var stockBefore = s.Db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes);
        var past = TestServices.Now.AddDays(-1);
        var batch = s.Db.CycleCountBatches.Single();
        batch.ScheduleTo = past;                                          // the schedule is over, but the batch did not ask to release the freeze
        await s.Db.SaveChangesAsync();

        Assert.Empty(await CycleCountAutoClose.CloseEndedBatchesAsync(s.Db, TestServices.Now, default));
        Assert.NotEqual(CycleCountBatchStatus.Closed, s.Db.CycleCountBatches.Single().Status);

        batch.AutoReleaseFreezeAtScheduleEnd = true;
        batch.ScheduleTo = TestServices.Now.AddDays(1);                   // asked for it, but not over yet
        await s.Db.SaveChangesAsync();
        Assert.Empty(await CycleCountAutoClose.CloseEndedBatchesAsync(s.Db, TestServices.Now, default));

        batch.ScheduleTo = past;                                          // asked for it and over
        await s.Db.SaveChangesAsync();
        var closed = await CycleCountAutoClose.CloseEndedBatchesAsync(s.Db, TestServices.Now, default);

        Assert.Single(closed);
        var after = s.Db.CycleCountBatches.Include(b => b.Racks).Single();
        Assert.Equal(CycleCountBatchStatus.Closed, after.Status);
        Assert.All(after.Racks, r => Assert.Equal(CycleCountRackStatus.Excluded, r.Status));
        Assert.Equal(stockBefore, s.Db.StockBatches.AsNoTracking().Sum(b => b.QuantityBoxes));               // nothing was applied to stock
        Assert.DoesNotContain(s.Db.StockLedgerEntries, e => e.EntryType == StockLedgerEntryTypes.CycleCount);
        Assert.Contains(s.Db.CycleCountHistory, h => h.Action == CycleCountActions.AutoClosed && h.UserName == CycleCountAutoClose.SystemUserName);
        Assert.Null(await CycleCountFreeze.FindFreezingBatchAsync(s.Db, 1, TestServices.Now, default));
        Assert.Empty(await CycleCountAutoClose.CloseEndedBatchesAsync(s.Db, TestServices.Now, default));    // closing twice does nothing
    }

    [Fact]
    public async Task EndedBatch_KeepsRacksThatWereAlreadySubmitted()
    {
        var s = await SubmittedAsync();                                   // rack A submitted and posted
        using var _ = s.Db;
        var batch = s.Db.CycleCountBatches.Single();
        batch.AutoReleaseFreezeAtScheduleEnd = true;
        batch.ScheduleTo = TestServices.Now.AddDays(-1);
        await s.Db.SaveChangesAsync();

        await CycleCountAutoClose.CloseEndedBatchesAsync(s.Db, TestServices.Now, default);

        Assert.Equal(CycleCountBatchStatus.Closed, s.Db.CycleCountBatches.Single().Status);
        Assert.Equal(CycleCountRackStatus.Submitted, s.Db.CycleCountBatchRacks.Single().Status);
        Assert.Equal(CycleCountReviewStatus.PendingReview, s.Db.CycleCountReviews.Single().Status);          // still reviewable after the close
    }
}
