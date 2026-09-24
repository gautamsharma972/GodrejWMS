namespace GodrejWMS.Application.Features.LocationSubtypes.Dtos;

public sealed record LocationSubtypeDto(
    int Id,
    string Code,
    string DisplayName,
    string? Description,
    int SortOrder,
    bool IsActive);
