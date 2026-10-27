using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.OrganizationSettings.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.OrganizationSettings.Queries;

public sealed record GetOrganizationSettingsQuery : IRequest<OrganizationSettingsDto>;

public sealed class GetOrganizationSettingsValidator : AbstractValidator<GetOrganizationSettingsQuery>
{
    public GetOrganizationSettingsValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can view organization settings.");
    }
}

public sealed class GetOrganizationSettingsHandler(IApplicationDbContext db, IUserManagementService users)
    : IRequestHandler<GetOrganizationSettingsQuery, OrganizationSettingsDto>
{
    public async Task<OrganizationSettingsDto> Handle(GetOrganizationSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await db.OrganizationSettings.AsNoTracking().SingleAsync(cancellationToken);
        var activeUserCount = (await users.ListUsersAsync(cancellationToken)).Count(u => u.IsActive);
        var warehouseCount = await db.Warehouses.CountAsync(cancellationToken);

        return new OrganizationSettingsDto(settings.MaxUsers, settings.MaxWarehouses, activeUserCount, warehouseCount);
    }
}
