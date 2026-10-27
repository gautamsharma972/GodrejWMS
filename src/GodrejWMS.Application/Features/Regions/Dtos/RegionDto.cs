namespace GodrejWMS.Application.Features.Regions.Dtos;

public sealed record RegionDto(int Id, string Code, string Name, string? Description, bool IsActive);
