namespace GodrejWMS.Application.Features.BrandLocationPreferences.Dtos;

public sealed record BrandLocationPreferenceDto(
    int Id,
    int BrandId,
    string BrandCode,
    int WarehouseId,
    string WarehouseCode,
    int RackId,
    string RackCode,
    int FromPalletPositionId,
    string FromLocationCode,
    int ToPalletPositionId,
    string ToLocationCode,
    bool IsActive);

public sealed record BrandLocationOptionDto(
    int Id,
    int WarehouseId,
    string WarehouseCode,
    int RackId,
    string RackCode,
    int Column,
    int Level,
    string LocationCode);
