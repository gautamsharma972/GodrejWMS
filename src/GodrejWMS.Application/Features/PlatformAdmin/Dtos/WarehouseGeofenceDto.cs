namespace GodrejWMS.Application.Features.PlatformAdmin.Dtos;

public sealed record WarehouseGeofenceDto(
    int WarehouseId,
    string Code,
    string Name,
    bool GeofenceEnabled,
    double? GeofenceLatitude,
    double? GeofenceLongitude,
    int? GeofenceRadiusMeters);
