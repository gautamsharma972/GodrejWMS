using GodrejWMS.Application.Features.Reports.Queries;
using GodrejWMS.Domain.Entities;
using GodrejWMS.Domain.Enums;
using GodrejWMS.Infrastructure.Persistence;
using Xunit;

namespace GodrejWMS.Infrastructure.Tests;

public class ReservationLookupTests
{
    private static (AppDbContext Db, PalletPosition Position, Material Material) Seed()
    {
        var db = TestDbContextFactory.Create();
        db.LocationTypes.Add(new LocationType { Id = LocationTypeIds.Rack, Code = "Rack", DisplayName = "Rack" });
        db.ZoneTypes.Add(new ZoneType { Id = ZoneTypeIds.Reserve, Code = "Reserve", DisplayName = "Reserve" });
        var rack = new Rack { Code = "A", Columns = 1, Levels = 1, ShelfLengthMm = 2000, ShelfWidthMm = 2500, ShelfHeightMm = 3600 };
        var position = new PalletPosition { Column = 1, Level = 1, LocationCode = "A-01-01", MaxPallets = 1 };
        rack.PalletPositions.Add(position);
        db.Racks.Add(rack);
        var material = new Material
        {
            MaterialNumber = 40049906, Description = "Material A", DesignType = "XOLDH", PackSize = 24, GrossWeightKg = 0.5m,
            LengthMm = 100, WidthMm = 100, HeightMm = 100, PalletCapacityBoxes = 40, SeasonId = SeasonIds.Rainy
        };
        db.Materials.Add(material);
        db.SaveChanges();
        return (db, position, material);
    }

    private static void AddGrn(AppDbContext db, PalletPosition p, Material m, string reference, bool confirmed = false, bool rejected = false)
    {
        var t = new InwardTransaction { ReferenceNumber = reference, LrNumber = "LR1", VehicleNumber = "MH01", IsRejected = rejected };
        var line = new InwardTransactionLine { MaterialId = m.Id, MfgMonth = 202610, RequestedQuantityBoxes = 5, AllocatedQuantityBoxes = 5 };
        line.Putaways.Add(new InwardPutaway { PalletPositionId = p.Id, QuantityBoxes = 5, IsConfirmed = confirmed });
        t.Lines.Add(line);
        db.InwardTransactions.Add(t);
        db.SaveChanges();
    }

    private static void AddPick(AppDbContext db, PalletPosition p, Material m, string reference, bool confirmed = false, bool rejected = false)
    {
        var t = new PulloutTransaction { ReferenceNumber = reference, VehicleNumber = "GJ27", IsConfirmed = confirmed, IsRejected = rejected };
        var line = new PulloutTransactionLine { MaterialId = m.Id, PlantName = "C042-4", RequestedQuantityBoxes = 3, PickedQuantityBoxes = 3 };
        line.Picks.Add(new PulloutPick { PalletPositionId = p.Id, MfgMonth = 202610, QuantityBoxes = 3 });
        t.Lines.Add(line);
        db.PulloutTransactions.Add(t);
        db.SaveChanges();
    }

    [Fact]
    public async Task NoCriteria_ReturnsNothing()
    {
        var (db, p, m) = Seed();
        AddGrn(db, p, m, "GRN-1");
        var result = await new GetReservationLookupHandler(db).Handle(new(), default);
        Assert.False(result.HasCriteria);
        Assert.Empty(result.Inbound);
        Assert.Empty(result.Outbound);
    }

    [Fact]
    public async Task ByMaterial_ReturnsBothSides_WithDestinationVehicleAndLr()
    {
        var (db, p, m) = Seed();
        AddGrn(db, p, m, "GRN-1");
        AddPick(db, p, m, "PICK-1");

        var result = await new GetReservationLookupHandler(db).Handle(new(MaterialCode: 40049906), default);

        var grn = Assert.Single(result.Inbound);
        Assert.Equal("GRN-1", grn.GrnNumber);
        Assert.Equal("LR1", grn.LrNumber);
        Assert.Equal("MH01", grn.VehicleNumber);
        var pick = Assert.Single(result.Outbound);
        Assert.Equal("PICK-1", pick.PickReference);
        Assert.Equal("C042-4", pick.Destination);
        Assert.Equal("GJ27", pick.VehicleNumber);
    }

    [Fact]
    public async Task ByLocation_MatchesOnlyThatLocation()
    {
        var (db, p, m) = Seed();
        AddPick(db, p, m, "PICK-1");

        Assert.Single((await new GetReservationLookupHandler(db).Handle(new(LocationCode: "A-01-01"), default)).Outbound);
        Assert.Empty((await new GetReservationLookupHandler(db).Handle(new(LocationCode: "Z-99-99"), default)).Outbound);
    }

    [Fact]
    public async Task ByReference_MatchesPrefixAndOnlyThatTransaction()
    {
        var (db, p, m) = Seed();
        AddPick(db, p, m, "PICK-20261102-000015");
        AddPick(db, p, m, "PICK-20261102-000011");

        var result = await new GetReservationLookupHandler(db).Handle(new(PickReference: "PICK-20261102-000015"), default);
        Assert.Equal("PICK-20261102-000015", Assert.Single(result.Outbound).PickReference);
    }

    [Fact]
    public async Task GrnReferenceAlone_ShowsOnlyInboundSide()
    {
        var (db, p, m) = Seed();
        AddGrn(db, p, m, "GRN-1");
        AddPick(db, p, m, "PICK-1");

        var result = await new GetReservationLookupHandler(db).Handle(new(GrnReference: "GRN-1"), default);
        Assert.Single(result.Inbound);
        Assert.Empty(result.Outbound);
    }

    [Fact]
    public async Task ReferenceOptions_ListRecentNonRejectedReferences()
    {
        var (db, p, m) = Seed();
        AddGrn(db, p, m, "GRN-1");
        AddGrn(db, p, m, "GRN-X", rejected: true);
        AddPick(db, p, m, "PICK-1");

        var options = await new GetReservationReferenceOptionsHandler(db).Handle(new(), default);
        Assert.Equal("GRN-1", Assert.Single(options.Grns).Reference);
        Assert.Equal("PICK-1", Assert.Single(options.Picks).Reference);
    }

    [Fact]
    public async Task ConfirmedHiddenByDefault_ShownWhenRequested()
    {
        var (db, p, m) = Seed();
        AddGrn(db, p, m, "GRN-C", confirmed: true);
        AddPick(db, p, m, "PICK-C", confirmed: true);

        var hidden = await new GetReservationLookupHandler(db).Handle(new(MaterialCode: 40049906), default);
        Assert.Empty(hidden.Inbound);
        Assert.Empty(hidden.Outbound);

        var shown = await new GetReservationLookupHandler(db).Handle(new(MaterialCode: 40049906, IncludeConfirmed: true), default);
        Assert.Single(shown.Inbound);
        Assert.Single(shown.Outbound);
    }

    [Fact]
    public async Task RejectedTransactions_AreNeverShown()
    {
        var (db, p, m) = Seed();
        AddGrn(db, p, m, "GRN-R", rejected: true);
        AddPick(db, p, m, "PICK-R", rejected: true);

        var result = await new GetReservationLookupHandler(db).Handle(new(MaterialCode: 40049906, IncludeConfirmed: true), default);
        Assert.Empty(result.Inbound);
        Assert.Empty(result.Outbound);
    }
}
