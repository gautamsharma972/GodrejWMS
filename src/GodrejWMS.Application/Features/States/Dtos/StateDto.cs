namespace GodrejWMS.Application.Features.States.Dtos;

public sealed record StateDto(int Id, string Code, string Name, int RegionId, string RegionCode, string RegionName, bool IsActive);
