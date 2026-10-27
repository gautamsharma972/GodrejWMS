using GodrejWMS.Application.Features.Pullout.Commands;
using GodrejWMS.Application.Features.Pullout.Dtos;
using GodrejWMS.Application.Features.Pullout.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Services;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class PulloutPickLocationChangeTests
{
    [Fact]
    public async Task DuplicateRowsForSameSource_AreTreatedAsOneLogicalPick()
    {
        using var db = TestDbContextFactory.Create();
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-03", MaxPallets = 1 };
        rack.PalletPositions.Add(position);
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

        db.StockBatches.Add(new StockBatch
        {
            MaterialId = material.Id,
            PalletPositionId = position.Id,
            MfgMonth = 202610,
            QuantityBoxes = 20
        });
        var line = new PulloutTransactionLine
        {
            MaterialId = material.Id,
            RequestedQuantityBoxes = 5,
            PickedQuantityBoxes = 5,
            Status = AllocationStatus.Fulfilled,
            PlantName = "A-bad"
        };
        line.Picks.Add(new PulloutPick { PalletPositionId = position.Id, MfgMonth = 202610, QuantityBoxes = 4 });
        line.Picks.Add(new PulloutPick { PalletPositionId = position.Id, MfgMonth = 202610, QuantityBoxes = 1 });
        var previewLine = new PulloutLineResultDto(material.MaterialNumber, material.Description, 5, 5,
            AllocationStatus.Fulfilled, null,
            [new(position.LocationCode, "OCT|2026", 4), new(position.LocationCode, "OCT|2026", 1)], "A-bad");
        var transaction = new PulloutTransaction
        {
            ReferenceNumber = "PICK-DUPLICATE",
            IsConfirmed = false,
            VehicleNumber = "MH-01-AB-1234",
            PreviewJson = System.Text.Json.JsonSerializer.Serialize(
                new PulloutResultDto("PICK-DUPLICATE", DateTimeOffset.UtcNow, [previewLine]))
        };
        transaction.Lines.Add(line);
        db.PulloutTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var options = await new GetPulloutPickChangeOptionsHandler(db).Handle(
            new(transaction.ReferenceNumber, material.MaterialNumber, position.LocationCode, "OCT|2026"), default);
        var current = Assert.Single(options);
        Assert.True(current.IsCurrent);

        var updated = await new ChangePulloutPickLocationHandler(db).Handle(
            new(transaction.ReferenceNumber, material.MaterialNumber, position.LocationCode, "OCT|2026",
                [new(position.Id, "OCT|2026", 3)], "Reduce actual picked quantity"), default);

        var updatedLine = Assert.Single(updated.Lines);
        var updatedPick = Assert.Single(updatedLine.Picks);
        Assert.Equal(3, updatedPick.QuantityBoxes);
        Assert.Equal(AllocationStatus.Partial, updatedLine.Status);
        Assert.Equal(3, Assert.Single(db.PulloutPicks).QuantityBoxes);

        var detail = await new GetPulloutDetailHandler(db).Handle(new(transaction.Id), default);
        Assert.Equal(3, Assert.Single(Assert.Single(detail.Lines).Picks).QuantityBoxes);
    }

    [Fact]
    public async Task History_IncludesDestinationAndVehicleNumber()
    {
        using var db = TestDbContextFactory.Create();
        var material = new Material
        {
            MaterialNumber = 40019311,
            Description = "Test material",
            DesignType = "TEST",
            SeasonId = SeasonIds.Rainy
        };
        var transaction = new PulloutTransaction
        {
            ReferenceNumber = "PICK-HISTORY-META",
            VehicleNumber = "MH-01-AB-1234",
            IsConfirmed = false
        };
        transaction.Lines.Add(new PulloutTransactionLine
        {
            Material = material,
            RequestedQuantityBoxes = 5,
            PickedQuantityBoxes = 0,
            Status = AllocationStatus.Failed,
            PlantName = "A-bad"
        });
        db.PulloutTransactions.Add(transaction);
        await db.SaveChangesAsync();

        var history = await new GetPulloutHistoryHandler(db).Handle(new(Search: "MH-01-AB-1234"), default);

        var row = Assert.Single(history.Items);
        Assert.Equal("A-bad", row.Destination);
        Assert.Equal("MH-01-AB-1234", row.VehicleNumber);
    }

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
        var preview = await submit.Handle(new(requestLines, WarehouseId: 1), default);
        var originalPick = Assert.Single(Assert.Single(preview.Lines).Picks);
        Assert.Equal(original.LocationCode, originalPick.LocationCode);

        var options = await new GetPulloutPickChangeOptionsHandler(db).Handle(
            new(preview.ReferenceNumber, material.MaterialNumber, originalPick.LocationCode, originalPick.MfgMonthLabel),
            default);
        var alternateOption = Assert.Single(options, option => option.PalletPositionId == alternate.Id);
        Assert.Equal("SEP|2026", alternateOption.MfgMonthLabel);
        Assert.Equal(10, alternateOption.AvailableBoxes);
        var originalOption = Assert.Single(options, option => option.PalletPositionId == original.Id);

        // Real-time picking means the replacement total no longer has to exactly match the
        // original pick - only fit within what the line overall requested. Picking less than the
        // original 30 (only 10 available right now) now succeeds, and the line is reclassified
        // Partial with a shortfall remark instead of throwing.
        var partial = await new ChangePulloutPickLocationHandler(db).Handle(
            new(preview.ReferenceNumber, material.MaterialNumber, originalPick.LocationCode,
                originalPick.MfgMonthLabel,
                [new(alternate.Id, alternateOption.MfgMonthLabel, 10)],
                "Original pallet is partly blocked"),
            default);
        var partialLine = Assert.Single(partial.Lines);
        Assert.Equal(AllocationStatus.Partial, partialLine.Status);
        Assert.Equal(10, partialLine.PickedQuantityBoxes);
        Assert.Contains("20", partialLine.Remarks);
        Assert.Equal(10, db.PulloutTransactions.Single().Lines.Single().PickedQuantityBoxes);
        Assert.Equal(AllocationStatus.Partial, db.PulloutTransactions.Single().Lines.Single().Status);

        // Selecting more than the line ever requested is still refused.
        var excessive = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ChangePulloutPickLocationHandler(db).Handle(
                new(preview.ReferenceNumber, material.MaterialNumber, alternate.LocationCode,
                    alternateOption.MfgMonthLabel,
                    [new(alternate.Id, alternateOption.MfgMonthLabel, 31)],
                    "Testing the cap"),
                default));
        Assert.Contains("30 requested", excessive.Message);

        // The pick now lives at the alternate location (from the partial change above) - restore
        // full coverage by redistributing across both locations.
        var changed = await new ChangePulloutPickLocationHandler(db).Handle(
            new(preview.ReferenceNumber, material.MaterialNumber, alternate.LocationCode,
                alternateOption.MfgMonthLabel,
                [
                    new(original.Id, originalOption.MfgMonthLabel, 20),
                    new(alternate.Id, alternateOption.MfgMonthLabel, 10)
                ],
                "Original pallet is partly blocked"),
            default);
        var changedLine = Assert.Single(changed.Lines);
        Assert.Equal(AllocationStatus.Fulfilled, changedLine.Status);
        Assert.Null(changedLine.Remarks);
        var changedPicks = changedLine.Picks;
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
    public IReadOnlyList<string> Roles => ["Superadmin"];
}
