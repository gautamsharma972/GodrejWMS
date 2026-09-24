using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class InwardReportsTests
{
    private static (AppDbContext Db, PalletPosition Position, Material Material) SeedWarehouse()
    {
        var db = TestDbContextFactory.Create();

        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", CapacityBoxes = 40 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);

        var material = new Material
        {
            MaterialNumber = 1, Description = "Material A", DesignType = "XOLDH", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();

        return (db, position, material);
    }

    [Fact]
    public async Task RejectionReasons_GroupsRejectedGrns_ByReason()
    {
        var (db, _, _) = SeedWarehouse();

        var rejected1 = new InwardTransaction
        {
            ReferenceNumber = "GRN-R1", IsRejected = true, RejectionReason = "Vendor error",
            RejectedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
        };
        rejected1.Lines.Add(new InwardTransactionLine { MaterialId = 0, MfgMonth = 202601, RequestedQuantityBoxes = 40, Status = AllocationStatus.Fulfilled });
        var rejected2 = new InwardTransaction
        {
            ReferenceNumber = "GRN-R2", IsRejected = true, RejectionReason = "Vendor error",
            RejectedAt = new DateTimeOffset(2026, 9, 6, 0, 0, 0, TimeSpan.Zero)
        };
        rejected2.Lines.Add(new InwardTransactionLine { MaterialId = 0, MfgMonth = 202601, RequestedQuantityBoxes = 10, Status = AllocationStatus.Fulfilled });
        var rejected3 = new InwardTransaction
        {
            ReferenceNumber = "GRN-R3", IsRejected = true, RejectionReason = "Damaged in transit",
            RejectedAt = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero)
        };
        rejected3.Lines.Add(new InwardTransactionLine { MaterialId = 0, MfgMonth = 202601, RequestedQuantityBoxes = 5, Status = AllocationStatus.Fulfilled });
        var notRejected = new InwardTransaction { ReferenceNumber = "GRN-OK", IsRejected = false };

        db.InwardTransactions.AddRange(rejected1, rejected2, rejected3, notRejected);
        await db.SaveChangesAsync();

        var handler = new GetInwardRejectionReasonsHandler(db);
        var rows = await handler.Handle(new GetInwardRejectionReasonsQuery(), CancellationToken.None);

        Assert.Equal(2, rows.Count);
        var vendorRow = Assert.Single(rows, r => r.Reason == "Vendor error");
        Assert.Equal(2, vendorRow.GrnCount);
        Assert.Equal(50, vendorRow.TotalRequestedBoxes);
        var damagedRow = Assert.Single(rows, r => r.Reason == "Damaged in transit");
        Assert.Equal(1, damagedRow.GrnCount);
    }

    [Fact]
    public async Task PutawayTurnaround_ComputesHours_ForFullyConfirmedGrn_AndLeavesUnconfirmedAsPending()
    {
        var (db, position, material) = SeedWarehouse();
        var receivedAt = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var confirmedAt = new DateTimeOffset(2026, 9, 1, 14, 0, 0, TimeSpan.Zero); // 6 hours later

        var confirmedGrn = new InwardTransaction { ReferenceNumber = "GRN-DONE", CreatedAt = receivedAt };
        var confirmedLine = new InwardTransactionLine { MaterialId = material.Id, MfgMonth = 202601, RequestedQuantityBoxes = 10, AllocatedQuantityBoxes = 10, Status = AllocationStatus.Fulfilled };
        confirmedLine.Putaways.Add(new InwardPutaway { PalletPositionId = position.Id, QuantityBoxes = 10, IsConfirmed = true, ConfirmedAt = confirmedAt });
        confirmedGrn.Lines.Add(confirmedLine);

        var pendingGrn = new InwardTransaction { ReferenceNumber = "GRN-PENDING", CreatedAt = receivedAt };
        var pendingLine = new InwardTransactionLine { MaterialId = material.Id, MfgMonth = 202602, RequestedQuantityBoxes = 5, AllocatedQuantityBoxes = 5, Status = AllocationStatus.Fulfilled };
        pendingLine.Putaways.Add(new InwardPutaway { PalletPositionId = position.Id, QuantityBoxes = 5, IsConfirmed = false });
        pendingGrn.Lines.Add(pendingLine);

        db.InwardTransactions.AddRange(confirmedGrn, pendingGrn);
        await db.SaveChangesAsync();

        var handler = new GetInwardPutawayTurnaroundHandler(db);
        var summary = await handler.Handle(new GetInwardPutawayTurnaroundQuery(), CancellationToken.None);

        Assert.Equal(2, summary.GrnCount);
        Assert.Equal(1, summary.ConfirmedCount);
        Assert.Equal(6.0, summary.AverageHours);
        Assert.Equal(6.0, summary.MedianHours);

        var doneRow = Assert.Single(summary.Rows, r => r.ReferenceNumber == "GRN-DONE");
        Assert.Equal(6.0, doneRow.TurnaroundHours);
        var pendingRow = Assert.Single(summary.Rows, r => r.ReferenceNumber == "GRN-PENDING");
        Assert.Null(pendingRow.TurnaroundHours);
        Assert.Null(pendingRow.ConfirmedAt);
    }

    [Fact]
    public async Task Exceptions_ReturnsOnlyPartialOrFailedLines_WithShortfall()
    {
        var (db, _, material) = SeedWarehouse();

        var grn = new InwardTransaction { ReferenceNumber = "GRN-MIXED" };
        grn.Lines.Add(new InwardTransactionLine { MaterialId = material.Id, MfgMonth = 202601, RequestedQuantityBoxes = 40, AllocatedQuantityBoxes = 40, Status = AllocationStatus.Fulfilled });
        grn.Lines.Add(new InwardTransactionLine { MaterialId = material.Id, MfgMonth = 202602, RequestedQuantityBoxes = 30, AllocatedQuantityBoxes = 10, Status = AllocationStatus.Partial, Remarks = "Warehouse at capacity" });
        grn.Lines.Add(new InwardTransactionLine { MaterialId = material.Id, MfgMonth = 202603, RequestedQuantityBoxes = 20, AllocatedQuantityBoxes = 0, Status = AllocationStatus.Failed, Remarks = "No capacity" });
        db.InwardTransactions.Add(grn);
        await db.SaveChangesAsync();

        var handler = new GetInwardExceptionsHandler(db);
        var rows = await handler.Handle(new GetInwardExceptionsQuery(), CancellationToken.None);

        Assert.Equal(2, rows.Count); // Fulfilled line excluded
        var partialRow = Assert.Single(rows, r => r.Status == "Partial");
        Assert.Equal(20, partialRow.ShortfallBoxes);
        var failedRow = Assert.Single(rows, r => r.Status == "Failed");
        Assert.Equal(20, failedRow.ShortfallBoxes);
    }
}
