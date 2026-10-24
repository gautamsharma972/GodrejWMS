namespace GodrejWMS.Application.Features.PlantTypes.Dtos;

public sealed record PlantTypeDto(
    int Id,
    string Code,
    string Name,
    int SortOrder,
    bool IsActive);
