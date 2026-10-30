namespace GodrejWMS.Application.Features.AopDescriptions.Dtos;

public sealed record AopDescriptionDto(int Id, string Code, string? Description, bool IsActive);
