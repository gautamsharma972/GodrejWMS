using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PulloutPickLocationChangeTests
{
    [Fact]
    public async Task PendingPick_CanSplitAcrossLocationsAndPkm_AndConfirmationUsesAllChosenSources()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 2, Levels = 1 };
        var original = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
        var alternate = new PalletPosition { Column = 2, Level = 1, LocationCode = "A-02-01", MaxPallets = 1 };
        rack.PalletPositions.Add(original);
        rack.PalletPositions.Add(alternate);
        var material = new Material
        {
            MaterialNumber = 40019311,
            Description = "Test material",
            DesignType = "TEST",
            SeasonId = SeasonIds.Rainy
        };
        db.Racks.Add(rack);
        db.Materials.Add(material);
        await db.SaveChangesAsync();
        db.StockBatches.AddRange(
            new StockBatch { MaterialId = material.Id, PalletPositionId = original.Id, MfgMonth = 202608, QuantityBoxes = 40 },
            new StockBatch { MaterialId = material.Id, PalletPositionId = alternate.Id, MfgMonth = 202609, QuantityBoxes = 10 });
        await db.SaveChangesAsync();

        var submit = new SubmitPulloutHandler(db, new PulloutAllocationService(db), new ChangeClock(), new ChangeUser());
        var requestLines = new[] { new SubmitPulloutLine(material.MaterialNumber, 30) };
        var preview = await submit.Handle(new(requestLines), default);
        var originalPick = Assert.Single(Assert.Single(preview.Lines).Picks);
        Assert.Equal(original.LocationCode, originalPick.LocationCode);

        var options = await new GetPulloutPickChangeOptionsHandler(db).Handle(
            new(preview.ReferenceNumber, material.MaterialNumber, originalPick.LocationCode, originalPick.MfgMonthLabel),
            default);
        var alternateOption = Assert.Single(options, option => option.PalletPositionId == alternate.Id);
        Assert.Equal("SEP|2026", alternateOption.MfgMonthLabel);
        Assert.Equal(10, alternateOption.AvailableBoxes);
        var originalOption = Assert.Single(options, option => option.PalletPositionId == original.Id);

        var incomplete = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ChangePulloutPickLocationHandler(db).Handle(
                new(preview.ReferenceNumber, material.MaterialNumber, originalPick.LocationCode,
                    originalPick.MfgMonthLabel,
                    [new(alternate.Id, alternateOption.MfgMonthLabel, 10)],
                    "Original pallet is partly blocked"),
                default));
        Assert.Contains("exactly 30", incomplete.Message);
        Assert.Single(db.PulloutPicks);

        var changed = await new ChangePulloutPickLocationHandler(db).Handle(
            new(preview.ReferenceNumber, material.MaterialNumber, originalPick.LocationCode,
                originalPick.MfgMonthLabel,
                [
                    new(original.Id, originalOption.MfgMonthLabel, 20),
                    new(alternate.Id, alternateOption.MfgMonthLabel, 10)
                ],
                "Original pallet is partly blocked"),
            default);
        var changedPicks = Assert.Single(changed.Lines).Picks;
        Assert.Equal(2, changedPicks.Count);
        Assert.Equal(30, changedPicks.Sum(pick => pick.QuantityBoxes));
        Assert.Contains(changedPicks, pick => pick.LocationCode == original.LocationCode
            && pick.MfgMonthLabel == "AUG|2026" && pick.QuantityBoxes == 20);
        Assert.Contains(changedPicks, pick => pick.LocationCode == alternate.LocationCode
            && pick.MfgMonthLabel == "SEP|2026" && pick.QuantityBoxes == 10);
        Assert.All(changedPicks, pick => Assert.Equal("Original pallet is partly blocked", pick.OverrideReason));
        Assert.All(db.PulloutPicks, pick => Assert.Equal("Original pallet is partly blocked", pick.OverrideReason));

        await submit.Handle(new(requestLines, changed), default);

        Assert.Equal(20, db.StockBatches.Single(b => b.PalletPositionId == original.Id).QuantityBoxes);
        Assert.DoesNotContain(db.StockBatches, b => b.PalletPositionId == alternate.Id);
        Assert.True(db.PulloutTransactions.Single().IsConfirmed);
    }
}

file sealed class ChangeClock : GodrejWMS.Application.Common.Interfaces.IDateTimeProvider
{
    public DateTimeOffset UtcNow => new(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
    public DateOnly Today => new(2026, 10, 2);
}

file sealed class ChangeUser : GodrejWMS.Application.Common.Interfaces.ICurrentUserService
{
    public string? UserId => "operator";
    public string? UserName => "operator@godrejwms.local";
    public bool IsInRole(string role) => true;
}
