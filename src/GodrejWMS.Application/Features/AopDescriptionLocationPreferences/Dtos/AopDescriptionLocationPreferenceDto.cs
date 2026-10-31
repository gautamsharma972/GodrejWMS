namespace GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Dtos;

public sealed record AopDescriptionLocationPreferenceDto(
    int Id,
    int AopDescriptionId,
    string AopCode,
    string? AopDescription,
    int WarehouseId,
    string WarehouseCode,
    int RackId,
    string RackCode,
    int FromPalletPositionId,
    string FromLocationCode,
    int ToPalletPositionId,
    string ToLocationCode,
    bool IsActive);

public sealed record AopLocationOptionDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    int RackId,
    string RackCode,
    int Column,
    int Level,
    string LocationCode);
