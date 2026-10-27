namespace GodrejWMS.Application.Features.WarehouseCategories.Dtos;

public sealed record WarehouseCategoryDto(int Id, string Code, string Name, string? Description, bool IsActive);
