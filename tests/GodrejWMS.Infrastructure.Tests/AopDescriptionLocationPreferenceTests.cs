using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Commands;
using GodrejWMS.Application.Features.Inward.Commands;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Infrastructure.Persistence;
using GodrejWMS.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

/// <summary>
/// AOP Description location preferences: the create/update/deactivate commands and their
/// validation (active AOP, valid range, same rack, no overlap), and the put-away lookup that now
/// resolves the preferred range through the material's AOP Description instead of its Brand.
/// </summary>
public class AopDescriptionLocationPreferenceTests
{
    // ───────────────────────── helpers ─────────────────────────

    private sealed record World(AppDbContext Db, Rack RackA, Rack RackB, AopDescription Aop, AopDescription OtherAop)
    {
        public PalletPosition A(int column, int level) => RackA.PalletPositions.Single(p => p.Column == column && p.Level == level);
        public PalletPosition B(int column, int level) => RackB.PalletPositions.Single(p => p.Column == column && p.Level == level);
    }

    private static Rack BuildRack(string code, int columns, int levels)
    {
        var rack = new Rack { Code = code, Columns = columns, Levels = levels, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        for (var column = 1; column <= columns; column++)
        {
            for (var level = 1; level <= levels; level++)
            {
                rack.PalletPositions.Add(new PalletPosition
                {
                    Column = column, Level = level, LocationCode = $"{code}-{column:00}-{level:00}", MaxPallets = 1
                });
            }
        }
        return rack;
    }

    private static World SeedWorld(string rackBCode = "B", int columns = 4, int levels = 3)
    {
        var db = TestDbContextFactory.Create();
        var rackA = BuildRack("A", columns, levels);
        var rackB = BuildRack(rackBCode, columns, levels);
        db.Racks.AddRange(rackA, rackB);
        var aop = new AopDescription { Code = "FAST CARD", Description = "Fast card", IsActive = true };
        var other = new AopDescription { Code = "HIT CHALK", Description = "Hit chalk", IsActive = true };
        db.AopDescriptions.AddRange(aop, other);
        db.SaveChanges();
        return new World(db, rackA, rackB, aop, other);
    }

    private static Material NewMaterial(long number, AopDescription? aop, int capacityBoxes = 40) => new()
    {
        MaterialNumber = number,
        Description = $"MATERIAL {number}",
        DesignType = "XOLDH",
        PackSize = 24,
        GrossWeightKg = 0.5m,
        LengthMm = 100,
        WidthMm = 100,
        HeightMm = 100,
        PalletCapacityBoxes = capacityBoxes,
        SeasonId = SeasonIds.Rainy,
        AopDescription = aop
    };

    private static CreateAopDescriptionLocationPreferenceHandler CreateHandler(AppDbContext db) =>
        new(db, new FixedClock(), new FixedUser());

    private static UpdateAopDescriptionLocationPreferenceHandler UpdateHandler(AppDbContext db) =>
        new(db, new FixedClock(), new FixedUser());

    private static Task<int> Create(World w, AopDescription aop, PalletPosition from, PalletPosition to) =>
        CreateHandler(w.Db).Handle(new CreateAopDescriptionLocationPreferenceCommand(aop.Id, from.Id, to.Id), CancellationToken.None);

    private static AopDescriptionLocationPreference AddMapping(World w, AopDescription aop, PalletPosition from, PalletPosition to, bool active = true)
    {
        var mapping = new AopDescriptionLocationPreference
        {
            AopDescriptionId = aop.Id, FromPalletPositionId = from.Id, ToPalletPositionId = to.Id, IsActive = active
        };
        w.Db.AopDescriptionLocationPreferences.Add(mapping);
        w.Db.SaveChanges();
        return mapping;
    }

    // ───────────────────────── create / update / deactivate ─────────────────────────

    [Fact]
    public async Task Create_SavesActiveMapping_ForActiveAopDescription()
    {
        var w = SeedWorld();

        var id = await Create(w, w.Aop, w.B(1, 1), w.B(2, 3));

        var saved = w.Db.AopDescriptionLocationPreferences.Single(x => x.Id == id);
        Assert.Equal(w.Aop.Id, saved.AopDescriptionId);
        Assert.Null(saved.BrandId);
        Assert.True(saved.IsActive);
        Assert.Equal("test-user", saved.CreatedByUserId);
    }

    [Fact]
    public async Task Create_Rejects_UnknownAopDescription()
    {
        var w = SeedWorld();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler(w.Db).Handle(new CreateAopDescriptionLocationPreferenceCommand(9999, w.B(1, 1).Id, w.B(1, 2).Id), CancellationToken.None));

        Assert.Contains("active AOP Description", ex.Message);
    }

    [Fact]
    public async Task Create_Rejects_InactiveAopDescription()
    {
        var w = SeedWorld();
        w.Aop.IsActive = false;
        w.Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(w, w.Aop, w.B(1, 1), w.B(1, 2)));

        Assert.Contains("active AOP Description", ex.Message);
        Assert.Empty(w.Db.AopDescriptionLocationPreferences);
    }

    [Fact]
    public async Task Create_Rejects_RangeWhereToIsBeforeFrom()
    {
        var w = SeedWorld();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(w, w.Aop, w.B(3, 1), w.B(2, 3)));

        Assert.Contains("at or after", ex.Message);
    }

    [Fact]
    public async Task Create_Rejects_EndpointsOnDifferentRacks()
    {
        var w = SeedWorld();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(w, w.Aop, w.A(1, 1), w.B(2, 2)));

        Assert.Contains("same rack", ex.Message);
    }

    [Fact]
    public async Task Create_Rejects_InactiveLocationEndpoint()
    {
        var w = SeedWorld();
        w.B(2, 2).IsActive = false;
        w.Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(w, w.Aop, w.B(1, 1), w.B(2, 2)));

        Assert.Contains("active pallet positions", ex.Message);
    }

    [Fact]
    public async Task Create_Rejects_RangeOverlappingActiveMapping_OfSameAopDescription()
    {
        var w = SeedWorld();
        AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 2));

        // Columns 2-3 x levels 2-3 shares position (2,2) with the existing columns 1-2 x levels 1-2.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(w, w.Aop, w.B(2, 2), w.B(3, 3)));

        Assert.Contains("overlaps", ex.Message);
        Assert.Single(w.Db.AopDescriptionLocationPreferences);
    }

    [Fact]
    public async Task Create_Allows_AdjacentRange_ThatDoesNotOverlap()
    {
        var w = SeedWorld();
        AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 3));

        await Create(w, w.Aop, w.B(3, 1), w.B(4, 3));        // next columns, same levels
        await Create(w, w.Aop, w.A(1, 1), w.A(2, 3));        // same columns, different rack

        Assert.Equal(3, w.Db.AopDescriptionLocationPreferences.Count());
    }

    [Fact]
    public async Task Create_Allows_SameRange_ForDifferentAopDescription()
    {
        var w = SeedWorld();
        AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 3));

        await Create(w, w.OtherAop, w.B(1, 1), w.B(2, 3));

        Assert.Equal(2, w.Db.AopDescriptionLocationPreferences.Count());
    }

    [Fact]
    public async Task Create_Rejects_ExactDuplicate_EvenWhenExistingMappingIsInactive()
    {
        var w = SeedWorld();
        AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 3), active: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(w, w.Aop, w.B(1, 1), w.B(2, 3)));

        Assert.Contains("already exists", ex.Message);
    }

    [Fact]
    public async Task Update_ChangesRange_AndDoesNotConflictWithItsOwnOldRange()
    {
        var w = SeedWorld();
        var mapping = AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 2));

        // New range still overlaps the mapping's own previous range - that must not count as a conflict.
        await UpdateHandler(w.Db).Handle(
            new UpdateAopDescriptionLocationPreferenceCommand(mapping.Id, w.Aop.Id, w.B(2, 1).Id, w.B(3, 3).Id, IsActive: true),
            CancellationToken.None);

        var saved = w.Db.AopDescriptionLocationPreferences.Single();
        Assert.Equal(w.B(2, 1).Id, saved.FromPalletPositionId);
        Assert.Equal(w.B(3, 3).Id, saved.ToPalletPositionId);
        Assert.Equal("test-user", saved.UpdatedByUserId);
    }

    [Fact]
    public async Task Update_CanSwitchMappingToAnotherAopDescription()
    {
        var w = SeedWorld();
        var mapping = AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 2));

        await UpdateHandler(w.Db).Handle(
            new UpdateAopDescriptionLocationPreferenceCommand(mapping.Id, w.OtherAop.Id, w.B(1, 1).Id, w.B(2, 2).Id, IsActive: true),
            CancellationToken.None);

        Assert.Equal(w.OtherAop.Id, w.Db.AopDescriptionLocationPreferences.Single().AopDescriptionId);
    }

    [Fact]
    public async Task Update_KeepsMappingEditable_WhenItsAopDescriptionWasDeactivatedLater()
    {
        var w = SeedWorld();
        var mapping = AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 2));
        w.Aop.IsActive = false;
        w.Db.SaveChanges();

        // Same AOP Description (now inactive) can still be saved - e.g. to deactivate the mapping.
        await UpdateHandler(w.Db).Handle(
            new UpdateAopDescriptionLocationPreferenceCommand(mapping.Id, w.Aop.Id, w.B(1, 1).Id, w.B(2, 2).Id, IsActive: false),
            CancellationToken.None);

        Assert.False(w.Db.AopDescriptionLocationPreferences.Single().IsActive);
        // ...but picking an inactive AOP Description for a different mapping is still refused.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(w, w.Aop, w.B(3, 1), w.B(4, 1)));
    }

    [Fact]
    public async Task Deactivate_FreesTheRange_ForANewOverlappingMapping_AndReactivationIsThenBlocked()
    {
        var w = SeedWorld();
        var original = AddMapping(w, w.Aop, w.B(1, 1), w.B(2, 2));

        await UpdateHandler(w.Db).Handle(
            new UpdateAopDescriptionLocationPreferenceCommand(original.Id, w.Aop.Id, w.B(1, 1).Id, w.B(2, 2).Id, IsActive: false),
            CancellationToken.None);
        Assert.False(w.Db.AopDescriptionLocationPreferences.Single().IsActive);

        await Create(w, w.Aop, w.B(2, 2), w.B(3, 3));        // overlaps the now-inactive range: allowed

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => UpdateHandler(w.Db).Handle(
            new UpdateAopDescriptionLocationPreferenceCommand(original.Id, w.Aop.Id, w.B(1, 1).Id, w.B(2, 2).Id, IsActive: true),
            CancellationToken.None));
        Assert.Contains("overlaps", ex.Message);
    }

    // ───────────────────────── put-away ─────────────────────────

    [Fact]
    public async Task Allocate_UsesRangeOfTheMaterialsAopDescription_NotAnotherAopsRange()
    {
        var w = SeedWorld();
        var material = NewMaterial(40000101, w.Aop);
        w.Db.Materials.Add(material);
        w.Db.SaveChanges();
        AddMapping(w, w.Aop, w.B(3, 1), w.B(3, 3));
        AddMapping(w, w.OtherAop, w.B(1, 1), w.B(1, 3));     // another AOP's range must not attract this material

        var result = await new PalletAllocationService(w.Db, new FixedClock()).AllocateAsync(material.Id, 10, 202610, warehouseId: 1);

        var line = Assert.Single(result.Lines);
        Assert.Equal("B-03-01", line.LocationCode);
        Assert.StartsWith("AOP-preferred range", line.Reason);
    }

    [Fact]
    public async Task Allocate_IgnoresDeactivatedMapping()
    {
        var w = SeedWorld();
        var material = NewMaterial(40000102, w.Aop);
        w.Db.Materials.Add(material);
        w.Db.SaveChanges();
        AddMapping(w, w.Aop, w.B(3, 1), w.B(3, 3), active: false);

        var result = await new PalletAllocationService(w.Db, new FixedClock()).AllocateAsync(material.Id, 10, 202610, warehouseId: 1);

        var line = Assert.Single(result.Lines);
        Assert.DoesNotContain("AOP-preferred", line.Reason ?? "");
        Assert.Equal("A-01-01", line.LocationCode);          // standard logic: first position in the first rack
    }

    [Fact]
    public async Task Allocate_FallsBackToStandardLogic_AndLogsIt_WhenAopHasNoMapping()
    {
        var w = SeedWorld();
        var material = NewMaterial(40000103, w.Aop);
        w.Db.Materials.Add(material);
        w.Db.SaveChanges();
        AddMapping(w, w.OtherAop, w.B(1, 1), w.B(1, 3));     // a mapping exists, but for a different AOP
        var log = new CapturingLogger<PalletAllocationService>();

        var result = await new PalletAllocationService(w.Db, new FixedClock(), log).AllocateAsync(material.Id, 10, 202610, warehouseId: 1);

        var line = Assert.Single(result.Lines);
        Assert.Equal("A-01-01", line.LocationCode);
        Assert.DoesNotContain("AOP-preferred", line.Reason ?? "");
        var entry = Assert.Single(log.Entries, e => e.Message.Contains("No active AOP location preference"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("40000103", entry.Message);
    }

    [Fact]
    public async Task Allocate_FallsBackToStandardLogic_AndWarns_WhenMaterialHasNoAopDescription()
    {
        var w = SeedWorld();
        var material = NewMaterial(40000104, aop: null);
        w.Db.Materials.Add(material);
        w.Db.SaveChanges();
        AddMapping(w, w.Aop, w.B(3, 1), w.B(3, 3));
        var log = new CapturingLogger<PalletAllocationService>();

        var result = await new PalletAllocationService(w.Db, new FixedClock(), log).AllocateAsync(material.Id, 10, 202610, warehouseId: 1);

        Assert.Equal(GodrejWMS.Domain.Enums.AllocationStatus.Fulfilled, result.Status);
        Assert.Equal("A-01-01", Assert.Single(result.Lines).LocationCode);
        var entry = Assert.Single(log.Entries, e => e.Message.Contains("has no AOP Description"));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("40000104", entry.Message);
    }

    [Fact]
    public async Task Allocate_SpillsOutsideTheRange_OnlyWhenTheRangeIsFull()
    {
        var w = SeedWorld();
        var material = NewMaterial(40000105, w.Aop, capacityBoxes: 40);
        w.Db.Materials.Add(material);
        w.Db.SaveChanges();
        AddMapping(w, w.Aop, w.B(1, 1), w.B(1, 1));          // a one-position range holding 40 boxes

        var result = await new PalletAllocationService(w.Db, new FixedClock()).AllocateAsync(material.Id, 50, 202610, warehouseId: 1);

        Assert.Equal(2, result.Lines.Count);
        var preferred = result.Lines.Single(l => l.LocationCode == "B-01-01");
        Assert.Equal(40, preferred.QuantityBoxes);
        Assert.StartsWith("AOP-preferred range", preferred.Reason);
        var spill = result.Lines.Single(l => l.LocationCode != "B-01-01");
        Assert.Equal(10, spill.QuantityBoxes);
        Assert.DoesNotContain("AOP-preferred", spill.Reason ?? "");
    }

    [Fact]
    public async Task SubmitInward_PlacesMaterialsWithDifferentAopDescriptions_IntoTheirOwnRanges_OnOneDocument()
    {
        var db = TestDbContextFactory.Create();
        var rackA = BuildRack("A", columns: 4, levels: 2);
        var rackB = BuildRack("B", columns: 4, levels: 2);
        var rackC = BuildRack("C", columns: 4, levels: 2);
        db.Racks.AddRange(rackA, rackB, rackC);
        var aop1 = new AopDescription { Code = "FAST CARD", IsActive = true };
        var aop2 = new AopDescription { Code = "HIT CHALK", IsActive = true };
        db.AopDescriptions.AddRange(aop1, aop2);
        var m1 = NewMaterial(40000201, aop1);
        var m2 = NewMaterial(40000202, aop2);
        var mNoAop = NewMaterial(40000203, aop: null);
        mNoAop.DesignType = "ZNOAOP";                         // unique design code, so standard logic has no rack-proximity pull
        db.Materials.AddRange(m1, m2, mNoAop);
        db.SaveChanges();

        PalletPosition Pos(Rack r, int column, int level) => r.PalletPositions.Single(p => p.Column == column && p.Level == level);
        db.AopDescriptionLocationPreferences.AddRange(
            new AopDescriptionLocationPreference { AopDescriptionId = aop1.Id, FromPalletPositionId = Pos(rackB, 2, 1).Id, ToPalletPositionId = Pos(rackB, 3, 2).Id, IsActive = true },
            new AopDescriptionLocationPreference { AopDescriptionId = aop2.Id, FromPalletPositionId = Pos(rackC, 1, 1).Id, ToPalletPositionId = Pos(rackC, 1, 2).Id, IsActive = true });
        db.SaveChanges();

        var handler = new SubmitInwardHandler(db, new PalletAllocationService(db, new FixedClock()), new FixedClock(), new FixedUser());
        var result = await handler.Handle(
            new SubmitInwardCommand(
            [
                new SubmitInwardLine(m1.MaterialNumber, 30, "MAR|2026"),
                new SubmitInwardLine(m2.MaterialNumber, 30, "MAR|2026"),
                new SubmitInwardLine(mNoAop.MaterialNumber, 30, "MAR|2026")
            ], WarehouseId: 1),
            CancellationToken.None);

        string LocationOf(Material m) => Assert.Single(result.Lines.Single(l => l.MaterialNumber == m.MaterialNumber).AllocatedTo).LocationCode;
        Assert.Equal("B-02-01", LocationOf(m1));              // first position of AOP #1's range, not rack A
        Assert.Equal("C-01-01", LocationOf(m2));              // first position of AOP #2's range
        Assert.StartsWith("A-", LocationOf(mNoAop));          // no AOP: standard logic (first rack)
        Assert.StartsWith("AOP-preferred range", result.Lines.Single(l => l.MaterialNumber == m1.MaterialNumber).AllocatedTo.Single().Reason);
        Assert.StartsWith("AOP-preferred range", result.Lines.Single(l => l.MaterialNumber == m2.MaterialNumber).AllocatedTo.Single().Reason);
    }

    // ───────────────────────── test doubles ─────────────────────────

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

        public DateOnly Today => new(2026, 10, 31);
    }

    private sealed class FixedUser : ICurrentUserService
    {
        public string? UserId => "test-user";

        public string? UserName => "test@godrejwms.local";

        public bool IsInRole(string role) => true;

        public IReadOnlyList<string> Roles => ["Superadmin"];
    }
}
