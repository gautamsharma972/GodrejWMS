namespace GodrejWMS.Application.Features.ActivityLogs.Dtos;

public sealed record ActivityLogDto(
    int Id,
    DateTimeOffset OccurredAt,
    string? UserName,
    string Action,
    string EntityName,
    string? EntityId,
    string Summary,
    string? OldValuesJson,
    string? NewValuesJson);
