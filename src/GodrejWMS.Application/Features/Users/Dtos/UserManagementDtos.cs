namespace GodrejWMS.Application.Features.Users.Dtos;

public sealed record UserListItemDto(
    string UserId,
    string Email,
    string FullName,
    bool IsActive,
    string? Role,
    int? WarehouseId,
    string? WarehouseName,
    IReadOnlyList<string> RegionNames);
