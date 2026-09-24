namespace GodrejWMS.Application.Features.SkuMovementTypes.Dtos;

public sealed record SkuMovementTypeDto(
    int Id,
    string Code,
    string DisplayName,
    string? Description,
    int SortOrder,
    bool IsActive);
