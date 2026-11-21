using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Common.Permissions;
using GodrejWMS.Application.Features.MaterialConversions.Commands;
using GodrejWMS.Application.Features.MaterialConversions.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>Converting stock of an old material code into a new code: FIFO proposal, approval, posting through the ledger, conflicts and guards.</summary>
public class MaterialConversionTests
{
    private sealed class Perms(params string[] keys) : IPermissionService
    {
        public Task<bool> HasPermissionAsync(string permissionKey, CancellationToken cancellationToken = default) => Task.FromResult(keys.Contains(permissionKey));
    }

    private sealed class AllWarehouses : IWarehouseAccessService
    {
        public Task<IReadOnlyList<WarehouseOption>> GetAccessibleWarehousesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WarehouseOption>>([]);
        public Task<bool> HasAccessAsync(int warehouseId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class User(string id) : ICurrentUserService
    {
        public string? UserId => id;
        public string? UserName => id + "@test";
        public bool IsInRole(string role) => true;
        public IReadOnlyList<string> Roles => [];
    }

    private static readonly Perms Requester = new(PermissionKeys.Menu.ViewMaterialConversion, PermissionKeys.Inventory.ConvertMaterial);
    private static readonly Perms Reviewer = new(PermissionKeys.Menu.ViewMaterialConversion, PermissionKeys.Inventory.ApproveMaterialConversion);
    private static readonly Perms Both = new(PermissionKeys.Menu.ViewMaterialConversion, PermissionKeys.Inventory.ConvertMaterial, PermissionKeys.Inventory.ApproveMaterialConversion);

    private sealed record Seed(AppDbContext Db, Material Old, Material New, List<PalletPosition> Positions);

    private static async Task<Seed> SeedAsync(bool newActive = true)
    {
        var db = TestDbContextFactory.Create();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "GDR1", Name = "Godrej 1" });
        var rack = new Rack { WarehouseId = 1, Code = "A", Columns = 4, Levels = 1, ShelfLengthMm = 1, ShelfWidthMm = 1, ShelfHeightMm = 1 };
        for (var c = 1; c <= 4; c++)
        {
            rack.PalletPositions.Add(new PalletPosition { Column = c, Level = 1, LocationCode = $"A-{c:00}-01", MaxPallets = 1 });
        }

        db.Racks.Add(rack);
        Material Make(long number, bool active) => new()
        {
            MaterialNumber = number, Description = "M" + number, DesignType = "X", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 50, SeasonId = SeasonIds.Rainy, IsActive = active
        };
        var old = Make(40000001, true);
        var neu = Make(40000002, newActive);
        db.Materials.AddRange(old, neu);
        await db.SaveChangesAsync();
        var positions = rack.PalletPositions.OrderBy(p => p.Column).ToList();
        // Old code: two cells of the older PKM (SEP) and one of the newer PKM (OCT).
        db.StockBatches.Add(new StockBatch { MaterialId = old.Id, PalletPositionId = positions[0].Id, MfgMonth = 202609, QuantityBoxes = 10 });
        db.StockBatches.Add(new StockBatch { MaterialId = old.Id, PalletPositionId = positions[1].Id, MfgMonth = 202609, QuantityBoxes = 20 });
        db.StockBatches.Add(new StockBatch { MaterialId = old.Id, PalletPositionId = positions[2].Id, MfgMonth = 202610, QuantityBoxes = 30 });
        await db.SaveChangesAsync();
        return new Seed(db, old, neu, positions);
    }

    private static GetMaterialConversionProposalHandler Proposal(AppDbContext db) =>
        new(db, new PulloutAllocationService(db), Requester, new AllWarehouses());

    private static SubmitMaterialConversionHandler Submit(AppDbContext db, IPermissionService permissions, string user = "requester") =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new User(user), permissions, new AllWarehouses());

    private static ApproveMaterialConversionHandler Approve(AppDbContext db, string user = "reviewer") =>
        new(db, TestServices.Posting(db), TestServices.Uow(db), new SvcClock(), new User(user), Reviewer, new AllWarehouses());

    private static decimal Stock(AppDbContext db, int materialId, int positionId, int mfgMonth) =>
        db.StockBatches.AsNoTracking().Where(b => b.MaterialId == materialId && b.PalletPositionId == positionId && b.MfgMonth == mfgMonth).Sum(b => (decimal?)b.QuantityBoxes) ?? 0;

    [Fact]
    public async Task Proposal_ForSomeBoxes_TakesTheOldestPkmFirst_AndAllStockWhenAsked()
    {
        var s = await SeedAsync();
        using var _ = s.Db;

        var some = await Proposal(s.Db).Handle(new GetMaterialConversionProposalQuery(s.Old.Id, s.New.Id, 1, 25), default);
        Assert.Equal(25, some.ProposedBoxes);
        Assert.All(some.Lines, l => Assert.Equal(202609, l.MfgMonth)); // FIFO: the SEP stock goes first, the OCT stock is untouched
        Assert.Equal(60, some.AvailableBoxes);

        var all = await Proposal(s.Db).Handle(new GetMaterialConversionProposalQuery(s.Old.Id, s.New.Id, 1), default);
        Assert.Equal(60, all.ProposedBoxes);
        Assert.Equal(3, all.Lines.Count);
        Assert.Equal(202609, all.Lines[0].MfgMonth);
        Assert.Equal(202610, all.Lines[^1].MfgMonth);
    }

    [Fact]
    public async Task Proposal_RefusesTheSameCode_AnInactiveNewCode_AndNoStock()
    {
        var s = await SeedAsync(newActive: false);
        using var _ = s.Db;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Proposal(s.Db).Handle(new GetMaterialConversionProposalQuery(s.Old.Id, s.Old.Id, 1), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Proposal(s.Db).Handle(new GetMaterialConversionProposalQuery(s.Old.Id, s.New.Id, 1), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Proposal(s.Db).Handle(new GetMaterialConversionProposalQuery(s.New.Id, s.Old.Id, 1), default)); // no stock of the "old" code
    }

    [Fact]
    public async Task Request_ChangesNothing_UntilApproved_ThenMovesStockOldMinusNewPlusAtTheSameCellAndPkm()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var proposal = await Proposal(s.Db).Handle(new GetMaterialConversionProposalQuery(s.Old.Id, s.New.Id, 1, 25), default);
        var lines = proposal.Lines.Select(l => new ConversionLineInput(l.StockBatchId, l.QuantityBoxes)).ToList();

        var requested = await Submit(s.Db, Requester).Handle(new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, lines, "Re-coded"), default);
        Assert.Equal(GodrejWMS.Domain.Entities.MaterialConversionStatus.PendingApproval, requested.Status);
        Assert.Equal(60, s.Db.StockBatches.AsNoTracking().Where(b => b.MaterialId == s.Old.Id).Sum(b => b.QuantityBoxes));
        Assert.Empty(s.Db.StockBatches.AsNoTracking().Where(b => b.MaterialId == s.New.Id));

        var approved = await Approve(s.Db).Handle(new ApproveMaterialConversionCommand(requested.Id, "ok"), default);

        Assert.Equal(GodrejWMS.Domain.Entities.MaterialConversionStatus.Approved, approved.Status);
        Assert.Equal(35, s.Db.StockBatches.AsNoTracking().Where(b => b.MaterialId == s.Old.Id).Sum(b => b.QuantityBoxes));
        Assert.Equal(25, s.Db.StockBatches.AsNoTracking().Where(b => b.MaterialId == s.New.Id).Sum(b => b.QuantityBoxes));
        foreach (var line in proposal.Lines)
        {
            Assert.Equal(line.QuantityBoxes, Stock(s.Db, s.New.Id, line.PalletPositionId, line.MfgMonth)); // same cell and PKM
        }

        var ledger = s.Db.StockLedgerEntries.AsNoTracking().Where(e => e.EntryType == StockLedgerEntryTypes.MaterialConversion).ToList();
        Assert.Equal(25, ledger.Where(e => e.MaterialId == s.New.Id).Sum(e => e.QuantityBoxes));
        Assert.Equal(-25, ledger.Where(e => e.MaterialId == s.Old.Id).Sum(e => e.QuantityBoxes));
        Assert.All(ledger, e => Assert.Equal(requested.ConversionNumber, e.ReferenceNumber));
    }

    [Fact]
    public async Task SubmitterWhoMayApprove_HasTheirRequestAppliedAtOnce_AndItAddsToAnExistingNewCodeBatch()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        s.Db.StockBatches.Add(new StockBatch { MaterialId = s.New.Id, PalletPositionId = s.Positions[1].Id, MfgMonth = 202609, QuantityBoxes = 5 });
        await s.Db.SaveChangesAsync();
        var line = s.Db.StockBatches.AsNoTracking().Single(b => b.MaterialId == s.Old.Id && b.PalletPositionId == s.Positions[1].Id);

        var result = await Submit(s.Db, Both).Handle(
            new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [new ConversionLineInput(line.Id, 20)]), default);

        Assert.Equal(GodrejWMS.Domain.Entities.MaterialConversionStatus.Approved, result.Status);
        Assert.Equal(25, Stock(s.Db, s.New.Id, s.Positions[1].Id, 202609)); // 5 already there + 20
        Assert.Equal(0, Stock(s.Db, s.Old.Id, s.Positions[1].Id, 202609));
    }

    [Fact]
    public async Task StockChangedAfterTheRequest_MakesTheApprovalAConflict_AndNothingMoves()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var batch = s.Db.StockBatches.AsNoTracking().First(b => b.MaterialId == s.Old.Id);
        var requested = await Submit(s.Db, Requester).Handle(
            new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [new ConversionLineInput(batch.Id, 5)]), default);

        // Someone picks from that cell meanwhile.
        var position = await s.Db.PalletPositions.Include(p => p.Rack).SingleAsync(p => p.Id == batch.PalletPositionId);
        await TestServices.Posting(s.Db).ApplyAsync(new StockPostingContext(StockLedgerEntryTypes.PulloutDeduct),
            [new StockChange(s.Old.Id, batch.MfgMonth, GodrejWMS.Domain.Entities.LocationSubtypeIds.Good, position, -1)], default);
        await s.Db.SaveChangesAsync();

        var result = await Approve(s.Db).Handle(new ApproveMaterialConversionCommand(requested.Id), default);

        Assert.Equal(GodrejWMS.Domain.Entities.MaterialConversionStatus.Conflict, result.Status);
        Assert.Empty(s.Db.StockBatches.AsNoTracking().Where(b => b.MaterialId == s.New.Id));
    }

    [Fact]
    public async Task Reject_NeedsADifferentUser_Cancel_LeavesStockAlone_AndOnlyPendingRequestsCanBeActedOn()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var batch = s.Db.StockBatches.AsNoTracking().First(b => b.MaterialId == s.Old.Id);
        var input = new ConversionLineInput(batch.Id, 5);
        var first = await Submit(s.Db, Requester).Handle(new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [input]), default);

        // The requester cannot review their own request; another reviewer can.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RejectMaterialConversionHandler(s.Db, new SvcClock(), new User("requester"), Reviewer).Handle(new RejectMaterialConversionCommand(first.Id, "no"), default));
        var rejected = await new RejectMaterialConversionHandler(s.Db, new SvcClock(), new User("boss"), Reviewer)
            .Handle(new RejectMaterialConversionCommand(first.Id, "Wrong code"), default);
        Assert.Equal(GodrejWMS.Domain.Entities.MaterialConversionStatus.Rejected, rejected.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Approve(s.Db).Handle(new ApproveMaterialConversionCommand(first.Id), default));

        var second = await Submit(s.Db, Requester).Handle(new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [input]), default);
        var cancelled = await new CancelMaterialConversionHandler(s.Db, new SvcClock(), new User("requester"), Requester).Handle(new CancelMaterialConversionCommand(second.Id), default);
        Assert.Equal(GodrejWMS.Domain.Entities.MaterialConversionStatus.Cancelled, cancelled.Status);
        Assert.Equal(60, s.Db.StockBatches.AsNoTracking().Where(b => b.MaterialId == s.Old.Id).Sum(b => b.QuantityBoxes));
        Assert.Empty(s.Db.StockBatches.AsNoTracking().Where(b => b.MaterialId == s.New.Id));
    }

    [Fact]
    public async Task Submit_RefusesTooManyBoxes_APendingRequestOnTheSameCell_AndAUserWithoutPermission()
    {
        var s = await SeedAsync();
        using var _ = s.Db;
        var batch = s.Db.StockBatches.AsNoTracking().First(b => b.MaterialId == s.Old.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(s.Db, Requester).Handle(
            new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [new ConversionLineInput(batch.Id, batch.QuantityBoxes + 1)]), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Submit(s.Db, Reviewer).Handle(
            new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [new ConversionLineInput(batch.Id, 1)]), default));

        await Submit(s.Db, Requester).Handle(new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [new ConversionLineInput(batch.Id, 1)]), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(s.Db, Requester).Handle(
            new SubmitMaterialConversionCommand(s.Old.Id, s.New.Id, 1, [new ConversionLineInput(batch.Id, 1)]), default));
    }
}
