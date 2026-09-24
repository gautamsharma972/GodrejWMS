namespace GodrejWMS.Application.Features.DesignTypes.Dtos;

public sealed record DesignTypeDto(int Id, string Code, string? Description, bool IsActive);
