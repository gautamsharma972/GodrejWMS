namespace GodrejWMS.Application.Features.Brands.Dtos;

public sealed record BrandDto(int Id, string Code, string? Description, bool IsActive);
