using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.PlatformAdmin.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.PlatformAdmin.Queries;

/// <summary>Every warehouse's Picker-login geofence settings - only for the hidden Platform Admin
/// screen. The client-facing Warehouses CRUD never reads or writes these fields.</summary>
public sealed record GetWarehouseGeofencesQuery : IRequest<IReadOnlyList<WarehouseGeofenceDto>>;

public sealed class GetWarehouseGeofencesValidator : AbstractValidator<GetWarehouseGeofencesQuery>
{
    public GetWarehouseGeofencesValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can view warehouse geofencing.");
    }
}

public sealed class GetWarehouseGeofencesHandler(IApplicationDbContext db)
    : IRequestHandler<GetWarehouseGeofencesQuery, IReadOnlyList<WarehouseGeofenceDto>>
{
    public async Task<IReadOnlyList<WarehouseGeofenceDto>> Handle(GetWarehouseGeofencesQuery request, CancellationToken cancellationToken) =>
        await db.Warehouses
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new WarehouseGeofenceDto(
                w.Id, w.Code, w.Name, w.GeofenceEnabled, w.GeofenceLatitude, w.GeofenceLongitude, w.GeofenceRadiusMeters))
            .ToListAsync(cancellationToken);
}
