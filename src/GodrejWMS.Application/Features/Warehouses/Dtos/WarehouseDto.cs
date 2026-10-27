namespace GodrejWMS.Application.Features.Warehouses.Dtos;

public sealed record WarehouseDto(
    int Id, string Code, string Name,
    int? WarehouseCategoryId, string? WarehouseCategoryCode, string? WarehouseCategoryName,
    int? CityId, string? CityName,
    int? StateId, string? StateName,
    int? RegionId, string? RegionName,
    string? Address,
    bool IsActive);
