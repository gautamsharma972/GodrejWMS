namespace GodrejWMS.Application.Features.Seasons.Dtos;

public sealed record SeasonDto(
    int Id,
    string Code,
    string DisplayName,
    string? Description,
    int SortOrder,
    bool IsActive);
