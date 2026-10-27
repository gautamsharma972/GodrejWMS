namespace GodrejWMS.Application.Features.OrganizationSettings.Dtos;

public sealed record OrganizationSettingsDto(
    int? MaxUsers,
    int? MaxWarehouses,
    int CurrentUserCount,
    int CurrentWarehouseCount);
