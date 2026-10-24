namespace GodrejWMS.Application.Features.Plants.Dtos;

public sealed record PlantDto(
    int Id,
    string Code,
    string Name,
    int? PlantTypeId,
    string? PlantTypeCode,
    string? PlantTypeName,
    string? Description,
    bool IsActive);
