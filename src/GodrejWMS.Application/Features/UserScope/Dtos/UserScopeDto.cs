namespace GodrejWMS.Application.Features.UserScope.Dtos;

public sealed record UserScopeDto(
    string UserId,
    int? WarehouseId,
    string? WarehouseCode,
    string? WarehouseName,
    IReadOnlyList<UserScopeRegionDto> Regions);

public sealed record UserScopeRegionDto(int RegionId, string RegionCode, string RegionName);
