namespace GodrejWMS.Application.Features.ZoneTypes.Dtos;

public sealed record ZoneTypeDto(
    int Id,
    string Code,
    string DisplayName,
    string? Description,
    int SortOrder,
    bool IsActive);
