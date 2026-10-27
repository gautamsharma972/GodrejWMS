namespace GodrejWMS.Application.Features.Cities.Dtos;

public sealed record CityDto(
    int Id, string Code, string Name,
    int StateId, string StateCode, string StateName,
    int RegionId, string RegionCode, string RegionName,
    bool IsActive);
