namespace GodrejWMS.Domain.Entities;

public class Warehouse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>State and Region are derived through City (City -> State -> Region) rather than
    /// duplicated on Warehouse directly, per the org-hierarchy data model.</summary>
    public int? CityId { get; set; }
    public City? City { get; set; }

    public int? WarehouseCategoryId { get; set; }
    public WarehouseCategory? WarehouseCategory { get; set; }

    public string? Address { get; set; }

    /// <summary>
    /// When enabled, inward put-away may consolidate different manufacturing periods of the
    /// same material into one pallet position. Enabled by default to preserve existing behavior.
    /// </summary>
    public bool AllowPkmMixing { get; set; } = true;

    public bool IsActive { get; set; } = true;

    /// <summary>Picker login geofencing - vendor-configured only (Platform Admin), never exposed to
    /// the client-facing Warehouses CRUD. When enabled, a user holding the Pickers role may only
    /// sign in to the mobile app from within GeofenceRadiusMeters of this warehouse's coordinates
    /// (or any other assigned warehouse's geofence), checked in GodrejWMS.PickersApi's login
    /// endpoint. Disabled by default so existing deployments are unaffected.</summary>
    public bool GeofenceEnabled { get; set; }
    public double? GeofenceLatitude { get; set; }
    public double? GeofenceLongitude { get; set; }
    public int? GeofenceRadiusMeters { get; set; }
}

public class UserWarehouse
{
    public string UserId { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
}
