namespace GodrejWMS.Application.Features.SeasonMapping.Dtos;

public sealed record SeasonMonthMapDto(int Id, int Month, int SeasonId, string SeasonCode, string SeasonName);
