namespace GodrejWMS.Application.Features.LocationTypes.Dtos;

public sealed record LocationTypeDto(
    int Id,
    string Code,
    string DisplayName,
    string? Description,
    int SortOrder,
    bool IsActive);
